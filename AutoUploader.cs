namespace AutoUploadToFTP
{
    using Renci.SshNet;
    using System.Collections.Concurrent;
    using System.Diagnostics;
    // 确保你有这些 using

    internal class AutoUploader : IDisposable
    {
        private readonly Config _config;
        private readonly FileSystemWatcher _watcher;
        // 使用线程安全的队列来收集变更事件
        private readonly ConcurrentQueue<string> _changedFiles = new();
        private readonly Timer _debounceTimer;
        private volatile bool _isProcessing = false; // 确保同一时间只有一个上传任务

        public AutoUploader(Config config)
        {
            _config = config;

            // 确保本地目录存在
            if (!Directory.Exists(_config.Localpath))
            {
                WriteLogAndConsole($"Error: Directory not found: {_config.Localpath}");
                return;
            }

            _watcher = new FileSystemWatcher(_config.Localpath)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName,
                IncludeSubdirectories = true,
                EnableRaisingEvents = true
            };

            // 订阅事件
            _watcher.Changed += OnFileChanged;
            _watcher.Created += OnFileChanged;
            _watcher.Deleted += OnFileChanged; // 注意：删除操作需要特殊处理
            _watcher.Renamed += OnFileRenamed;

            // 设置防抖计时器，延迟2秒处理，避免事件风暴
            _debounceTimer = new Timer(ProcessChanges, null, Timeout.Infinite, Timeout.Infinite);

            WriteLogAndConsole($"Started monitoring folder: {_config.Localpath}");

            // 如果配置要求启动时上传所有文件
            if (_config.UploadAllFilesWhenAppStart)
            {
                WriteLogAndConsole("Initial scan triggered by 'UploadAllFilesWhenAppStart'.");
                InitialScanAndUpload();
            }
            else if (_config.CheckTime.HasValue)
            {
                WriteLogAndConsole($"开始上传所有更新时间大于{_config.CheckTime.Value:yyyy-MM-dd HH:mm:ss}的文件");
                InitialScanBiggerThenCheckTimeAndUpload(_config.CheckTime.Value);
                SetCheckTimeValue();
            }
        }

        private void SetCheckTimeValue(string timeValue = "")
        {
            if (_config.CheckTimeStr.Equals(timeValue))
            {
                return;
            }
            _config.CheckTimeStr = timeValue;
            AppConfig.UpdateConfig();
        }

        private void WriteLogAndConsole(string message)
        {
            var msg = $"【{_config.Name}】{message}";
            Console.WriteLine(msg);
            // Log.Write(msg); // 假设你有Log类
        }

        // 初始扫描（仅在需要时运行一次）
        private void InitialScanAndUpload()
        {
            var allFiles = Directory.GetFiles(_config.Localpath,"*", SearchOption.AllDirectories)
                                    .Where(f => !IsInIgnorePath(f));
            _changedFiles.Clear(); // 清空队列
            foreach (var file in allFiles)
            {
                _changedFiles.Enqueue(file);
            }

            if (!_changedFiles.IsEmpty)
            {
                WriteLogAndConsole($"Initial scan found {_changedFiles.Count} files to upload.");
                // 立即处理
                ProcessChanges(null);
            }
        }

        private void InitialScanBiggerThenCheckTimeAndUpload(DateTime checkTime)
        {
            var allFiles = new DirectoryInfo(_config.Localpath).GetFiles("*", SearchOption.AllDirectories)
                                    .Where(f => !IsInIgnorePath(f.FullName) && f.LastWriteTime > checkTime);
            _changedFiles.Clear(); // 清空队列
            foreach (var file in allFiles)
            {
                _changedFiles.Enqueue(file.FullName);
            }

            if (!_changedFiles.IsEmpty)
            {
                WriteLogAndConsole($"Initial scan found {_changedFiles.Count} files to upload.");
                // 立即处理
                ProcessChanges(null);
            }
        }


        private string ToDirectory(string fileName)
        {
            return new FileInfo(fileName).DirectoryName;
        }

        private void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            if (IsInIgnorePath(e.FullPath)) return;

            // 只关心文件，不关心目录本身的变化
            if (File.Exists(e.FullPath))
            {
                //  WriteLogAndConsole($"[Watcher] Change detected: {e.FullPath}, Type: {e.ChangeType}");
                _changedFiles.Enqueue(e.FullPath);
                // 重置计时器，2秒后触发处理
                _debounceTimer.Change(2000, Timeout.Infinite);
            }
        }

        private void OnFileRenamed(object sender, RenamedEventArgs e)
        {
            if (IsInIgnorePath(e.FullPath)) return;

            // WriteLogAndConsole($"[Watcher] Renamed: {e.OldFullPath} to {e.FullPath}");
            _changedFiles.Enqueue(e.FullPath); // 将新文件加入队列
                                               // 注意：你可能还需要处理远程服务器上旧文件的删除
            _debounceTimer.Change(2000, Timeout.Infinite);
        }

        private bool IsInIgnorePath(string path)
        {
            if (_config.IgnorePathList == null || !_config.IgnorePathList.Any()) return false;

            var dirName= ToDirectory(path);
            // 将windows路径转为linux路径风格，方便匹配
            var relativePath = Path.GetRelativePath(_config.Localpath, dirName).Replace("\\", "/");
            return _config.IgnorePathList.Any(ignore => relativePath.Contains(ignore, StringComparison.OrdinalIgnoreCase));
        }


        private void ProcessChanges(object state)
        {
            if (_isProcessing || _changedFiles.IsEmpty)
            {
                return;
            }

            lock (this)
            {
                if (_isProcessing) return;
                _isProcessing = true;
            }

            try
            {
                // 将队列中的所有文件取出，形成本次处理的批次
                var filesToProcess = new HashSet<string>();
                while (_changedFiles.TryDequeue(out var file))
                {
                    filesToProcess.Add(file);
                }

                if (!filesToProcess.Any()) return;

                WriteLogAndConsole($"Processing {filesToProcess.Count} file changes({DateTime.Now:yyyy-MM-dd HH:mm:ss})...");

                // 1. 运行本地命令
                RunLocalCmd(_config.LocalCmd);

                // 2. 上传文件 (带重试)
                bool uploadSuccess = false;
                for (int i = 0; i < 3; i++) // 重试3次
                {
                    if (UploadFiles(filesToProcess))
                    {
                        uploadSuccess = true;
                        break;
                    }
                    WriteLogAndConsole($"Upload attempt {i + 1} failed. Retrying in 3 seconds...");
                    Thread.Sleep(3000);
                }

                if (uploadSuccess)
                {
                    WriteLogAndConsole("Upload successful.");
                    // 3. 运行远程命令
                    bool cmdSuccess = RunRemoteCmd(_config.Cmd);
                    WriteLogAndConsole($"---------------------------Command execution {(cmdSuccess ? "successfully" : "failed")}---------------------------");
                }
                else
                {
                    WriteLogAndConsole("Upload failed after multiple retries. Changes will be queued for next event.");
                    // 将处理失败的文件重新放回队列前端（或另一个优先队列）
                    foreach (var file in filesToProcess)
                    {
                        _changedFiles.Enqueue(file);
                    }
                }
            }
            finally
            {
                _isProcessing = false;
            }
        }

        private bool UploadFiles(HashSet<string> files)
        {
            // 使用 using 语句确保 SftpClient 被正确释放
            try
            {
                using (var sftp = new SftpClient(_config.Host, _config.Ftpport, _config.Username, _config.Password))
                {
                    sftp.Connect();
                    if (!sftp.IsConnected)
                    {
                        WriteLogAndConsole("SFTP connection failed.");
                        return false;
                    }

                    WriteLogAndConsole("SFTP connected. Starting file upload...");

                    var createdRemoteDirs = new HashSet<string>();

                    foreach (var localPath in files)
                    {
                        if (!File.Exists(localPath)) continue; // 文件可能在处理前被删除了

                        string relativePath = Path.GetRelativePath(_config.Localpath, localPath);
                        string remotePath = Path.Combine(_config.Remotepath, relativePath).Replace('\\', '/');
                        string remoteDir = Path.GetDirectoryName(remotePath).Replace('\\', '/');

                        // 创建远程目录 (如果需要且未创建过)
                        if (!createdRemoteDirs.Contains(remoteDir))
                        {
                            CreateRemoteDirectoryRecursively(sftp, remoteDir);
                            createdRemoteDirs.Add(remoteDir);
                        }

                        using (var fileStream = File.OpenRead(localPath))
                        {
                            sftp.UploadFile(fileStream, remotePath, true);
                            WriteLogAndConsole($"  - Uploaded: {localPath} -> {remotePath}");
                        }
                    }
                    return true;
                }
            }
            catch (Exception ex)
            {
                WriteLogAndConsole($"Error during SFTP upload: {ex.Message}");
                return false;
            }
        }

        private void CreateRemoteDirectoryRecursively(SftpClient sftp, string path)
        {
            string current = "";
            if (path[0] == '/')
            {
                path = path.Substring(1);
            }

            foreach (string dir in path.Split('/'))
            {
                current = Path.Combine(current, dir).Replace('\\', '/');
                if (!sftp.Exists(current))
                {
                    sftp.CreateDirectory(current);
                }
            }
        }


        private bool RunRemoteCmd(string cmd)
        {
            if (string.IsNullOrWhiteSpace(cmd)) return true; // 没有命令也算成功

            try
            {
                // 同样，使用 using 确保资源释放
                using (var ssh = new SshClient(_config.Host, _config.Sshport, _config.Username, _config.Password))
                {
                    ssh.Connect();
                    if (!ssh.IsConnected)
                    {
                        WriteLogAndConsole("SSH connection failed.");
                        return false;
                    }

                    WriteLogAndConsole($"Executing remote command: {cmd}");
                    var command = ssh.RunCommand(cmd);

                    if (!string.IsNullOrWhiteSpace(command.Error))
                    {
                        WriteLogAndConsole($"Command execution failed. Error: {command.Error}");
                        return false;
                    }

                    WriteLogAndConsole($"Command result: {command.Result}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                WriteLogAndConsole($"Error during SSH command execution: {ex.Message}");
                return false;
            }
        }

        // RunLocalCmd 基本保持不变，可以作为辅助方法
        public bool RunLocalCmd(string cmd)
        {
            if (string.IsNullOrWhiteSpace(cmd))
            {
                return true;
            }
            Console.WriteLine($"Begin Run LocalCommand:{cmd}");
            ProcessStartInfo processInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/C {cmd}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using (Process process = new Process { StartInfo = processInfo })
            {
                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (!string.IsNullOrWhiteSpace(error))
                {
                    Console.WriteLine($"Error:{error}");
                    return false;
                }
                Console.WriteLine($"Output:{output}");
                return true;
            }
        }


        public void Dispose()
        {
            _watcher?.Dispose();
            _debounceTimer?.Dispose();
        }
    }
}
