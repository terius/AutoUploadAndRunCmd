using Renci.SshNet;
using System.Configuration;
using System.Diagnostics;
using System.Net.Sockets;

namespace AutoUploadToFTP
{
    internal class AutoUploadFileAndRunCmdForLinux
    {
        readonly string _host;
        readonly string _username;
        readonly string _password;
        readonly string _localDirectory;
        readonly string _remoteDirectory;
        readonly string _cmd;
        readonly int _sshPort;
        readonly int _ftpPort;
        readonly bool _uploadAllFileWhenFirstScan;
        readonly string[] _ignorePathList;
        DateTime? _checkTime;



        Dictionary<string, DateTime> _checkFileTime = new();
        HashSet<UploadFileInfo> _newFiles = new();
        SftpClient _ftpClient = null;
        SshClient _sshClient = null;
        //    bool hasChange = true;
        bool isFirst = true;
        string _localCmd;
        string _name;
        Config _config;
        public AutoUploadFileAndRunCmdForLinux(Config config)
        {
            _config = config;
            _name = config.Name;
            _host = config.Host;
            _username = config.Username;
            _password = config.Password;
            _localDirectory = config.Localpath;
            _remoteDirectory = config.Remotepath;
            _cmd = config.Cmd;
            _sshPort = config.Sshport;
            _ftpPort = config.Ftpport;
            _uploadAllFileWhenFirstScan = config.UploadAllFilesWhenAppStart;
            _checkTime = config.CheckTime;
            _ignorePathList = config.IgnorePathList;
            _localCmd = config.LocalCmd;
        }

        private void WriteLogAndConsole(string message)
        {
            var msg = $"【{_name}】{message}";
            Console.WriteLine(msg);
            Log.Write(msg);
        }

        private bool ConnectToFtp()
        {
            var isConnected = false;
            try
            {
                if (_ftpClient != null && _ftpClient.IsConnected)
                {
                    return true;
                }
                WriteLogAndConsole($"Start establishing an FTP connection to server {_host}");
                _ftpClient = new SftpClient(_host, _ftpPort, _username, _password);
                _ftpClient.Connect();
                isConnected = _ftpClient.IsConnected;
                var connectMsg = isConnected ? "successful" : "failed";
                WriteLogAndConsole($"FTP connection {connectMsg} ");
                Thread.Sleep(1000);
            }
            catch (Exception ex)
            {
                WriteLogAndConsole($"FTP connection failed,error:{ex.Message}");
            }
            return isConnected;
        }

        public void Run()
        {
            WriteLogAndConsole($"Start monitoring folders: {_localDirectory}\r\nRemote server: {_host}\r\nSSH Port: {_sshPort},FTP Port: {_ftpPort}");
            if (string.IsNullOrWhiteSpace(_localDirectory) || string.IsNullOrWhiteSpace(_remoteDirectory))
            {
                WriteLogAndConsole($"Local or remote directory can not be empty!");
                return;
            }



            //if (!ConnectToFtp())
            //{
            //    return;
            //}

            var rootDir = new Dir(_localDirectory);


            //// 创建一个新的 FileSystemWatcher 实例
            //FileSystemWatcher watcher = new(_localDirectory)
            //{
            //    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName,
            //    IncludeSubdirectories = true,
            //    EnableRaisingEvents = true
            //};

            //// 添加事件处理程序
            //watcher.Changed += (s, e) => WriteLogAndConsole($"[Watcher] Change detected: {e.FullPath}, Type: {e.ChangeType}");
            //watcher.Created += (s, e) => WriteLogAndConsole($"[Watcher] Change detected: {e.FullPath}, Type: {e.ChangeType}");
            //watcher.Deleted += (s, e) => WriteLogAndConsole($"[Watcher] Change detected: {e.FullPath}, Type: {e.ChangeType}");
            //watcher.Renamed += (s, e) => WriteLogAndConsole($"[Watcher] Change detected: {e.OldFullPath} renamed to {e.FullPath}, Type: {e.ChangeType}");

            //// 设置为递归监视所有子文件夹
            //watcher.IncludeSubdirectories = true;

            //// 启动监视
            //watcher.EnableRaisingEvents = true;





            // --- 核心逻辑修改 ---
            var uploadResult = true;
            while (true)
            {
                // 每次循环都清理待上传列表
                // 注意：如果上次上传失败，这里需要决策是否重试。
                // 当前逻辑是如果上次失败，则不清理，下次会合并新的变更一起重试。
                if (uploadResult)
                {
                    _newFiles.Clear();
                }

                // 运行本地命令（如果需要）
                RunLocalCmd(_localCmd);

                // 每次循环都主动扫描目录
                // WriteLogAndConsole("\r\nScanning for file changes...");
                ScanDir(rootDir);

                if (_newFiles.Any(d => d.FileType != 2)) // FileType 2 表示上传失败的文件
                {
                    var actionTime = DateTime.Now.AddMinutes(-1).ToString("HH:mm");

                    WriteLogAndConsole($"Files changes detected({DateTime.Now:yyyy-MM-dd HH:mm:ss}):");
                    foreach (var file in _newFiles.Where(f => f.FileType != 2))
                    {
                        WriteLogAndConsole($"  - [{file.FileTypeText}] {file.FileName}");
                    }

                    uploadResult = false; // 先假设上传会失败
                    for (int i = 0; i < 5; i++)
                    {
                        if (UploadFileUsingSsh())
                        {
                            uploadResult = true;
                            break;
                        }
                        WriteLogAndConsole($"Upload attempt {i + 1} failed. Retrying in 3 seconds...");
                        CloseFTP(); // 确保关闭旧连接
                        Thread.Sleep(3000);
                    }

                    if (uploadResult)
                    {
                        WriteLogAndConsole("Upload successful.");
                        Thread.Sleep(1000); // 等待一下，确保文件系统稳定
                        var runCmdResult = RunCmd(_cmd);
                        WriteLogAndConsole($"---------------------------Command execution {(runCmdResult ? "successfully" : "failed")}---------------------------");
                    }
                    else
                    {
                        WriteLogAndConsole("Upload failed after multiple retries.");
                        foreach (var failFile in _newFiles)
                        {
                            // 标记为失败，以便下次扫描时重试
                            failFile.FileType = 2;
                        }
                        SetCheckTimeValue(actionTime);
                    }
                }
                else
                {
                    //  WriteLogAndConsole("No changes detected.");
                }

                if (isFirst)
                {
                    WriteLogAndConsole($"Initial scan complete. Total files monitored: {_checkFileTime.Count}");
                    SetCheckTimeValue();
                    isFirst = false;
                }

                // 在每次扫描循环后，固定休眠一段时间，例如 5 秒
                int pollingIntervalSeconds = 5;
                //    WriteLogAndConsole($"Waiting for {pollingIntervalSeconds} seconds before next scan...\n");
                Thread.Sleep(pollingIntervalSeconds * 1000);
            }
        }

        //private void OnChanged(object source, FileSystemEventArgs e)
        //{
        //    hasChange = true;
        //}

        void CloseFTP()
        {
            if (_ftpClient != null && _ftpClient.IsConnected)
            {
                _ftpClient.Disconnect();
            }
        }

        private bool CheckInIgnorePath(string path)
        {
            if (_ignorePathList == null)
            {
                return false;
            }
            foreach (var file in _ignorePathList)
            {
                if (path.Contains(file, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

            }
            return false;
        }

        void ScanDir(Dir dir, string pathName = null)
        {
            var path = dir.Name;
            if (!Directory.Exists(path) || CheckInIgnorePath(path))
            {
                return;
            }
            var files = Directory.GetFiles(path);
            var subDirs = Directory.GetDirectories(path);

            foreach (var fileName in files)
            {
                var fileInfo = new FileInfo(fileName);
                var dirAndFileName = pathName != null ? $"{pathName}|||{fileInfo.Name}" : fileInfo.Name;

                if (!_checkFileTime.ContainsKey(dirAndFileName))
                {
                    if (_uploadAllFileWhenFirstScan || !isFirst || (_checkTime.HasValue && fileInfo.LastWriteTime > _checkTime.Value))
                    {
                        _newFiles.RemoveWhere(d => d.FileName == fileName);
                        _newFiles.Add(new UploadFileInfo { FileName = fileName, Path = pathName?.Replace("|||", "/"), FileType = 0 });
                    }
                }
                else if (fileInfo.LastWriteTime > _checkFileTime[dirAndFileName])
                {
                    _newFiles.RemoveWhere(d => d.FileName == fileName);
                    _newFiles.Add(new UploadFileInfo { FileName = fileName, Path = pathName?.Replace("|||", "/"), FileType = 1 });
                }

                _checkFileTime[dirAndFileName] = fileInfo.LastWriteTime;
                dir.AddFile(fileInfo);
            }





            foreach (var dirName in subDirs)
            {
                var subDir = new Dir(dirName);
                var diName = Path.GetFileNameWithoutExtension(dirName);
                ScanDir(subDir, pathName != null ? pathName + "|||" + diName : diName);
                dir.AddDir(subDir);
            }
        }

        string JoinToLinuxPath(params string[] strs)
        {
            var notEmpty = strs.Where(d => !string.IsNullOrWhiteSpace(d)).Select(d => d.TrimEnd('/'));
            return string.Join('/', notEmpty);
        }


        public void CreatePath(string path, HashSet<string> createPathList)
        {
            if (!string.IsNullOrWhiteSpace(path) && !createPathList.Contains(path))
            {
                RunCmd($"cd {_remoteDirectory} && [ ! -d \"{path}\" ] &&  mkdir -p \"{path}\"");
                createPathList.Add(path);
            }

        }

        bool UploadFileUsingSsh()
        {
            //if (_tryTime-- <= 0)
            //{
            //    Console.WriteLine($"Try times exceeded");
            //    return false;
            //}
            bool result = false;
            try
            {
                ConnectToFtp();
                //if (_ftpClient == null || !_ftpClient.IsConnected)
                //{
                //    WriteLogAndConsole("Start establishing an FTP connection to server...");

                //    _ftpClient = new SftpClient(_host, _ftpPort, _username, _password);
                //    _ftpClient.Connect();
                //    WriteLogAndConsole("FTP connection successful");
                //}

                if (_ftpClient.IsConnected)
                {
                    WriteLogAndConsole("Start sending files to  server");
                    bool hasError = false;
                    HashSet<string> createPathList = new();
                    foreach (var file in _newFiles)
                    {
                        try
                        {
                            var fileName = file.FileName;
                            CreatePath(file.Path, createPathList);
                            using (var fileStream = File.OpenRead(fileName))
                            {
                                var remotePath = JoinToLinuxPath(_remoteDirectory, file.Path, Path.GetFileName(fileName));
                                // Upload the file to the remote directory
                                _ftpClient.UploadFile(fileStream, remotePath, true);
                            }
                        }
                        catch (SocketException socketExp)
                        {
                            WriteLogAndConsole($"File {file} upload failed, reason: {socketExp.Message}, started trying to reconnect...");
                            hasError = true;
                            break;
                        }
                        catch (Exception ex)
                        {
                            WriteLogAndConsole($"File {file} upload failed, reason:{ex.Message}");
                            hasError = true;
                            break;

                        }

                    }
                    WriteLogAndConsole($"---------------------------File upload {(hasError ? "failed" : "successfully")}---------------------------\r\n");
                    result = !hasError;
                }
                else
                {
                    WriteLogAndConsole($"Server {_host} connection failure");

                }
            }
            catch (Exception ex)
            {
                WriteLogAndConsole($"FTP connection error occurred，reason：{ex.Message}");
                result = false;
            }

            return result;

        }

        bool RunCmd(string cmd)
        {
            if (string.IsNullOrWhiteSpace(cmd))
            {
                WriteLogAndConsole("Cmd is empty,not executed");
                return false;
            }
            if (_sshClient == null || !_sshClient.IsConnected)
            {
                WriteLogAndConsole("Start establishing an SSH connection to server...");
                _sshClient = new SshClient(_host, _sshPort, _username, _password);
                _sshClient.Connect();
                WriteLogAndConsole($"SSH connection successful");
            }

            if (_sshClient.IsConnected)
            {
                WriteLogAndConsole($"Start executing commands:{cmd}");
                // 执行远程命令
                var command = _sshClient.RunCommand(cmd);

                if (!string.IsNullOrWhiteSpace(command.Error.Trim()))
                {
                    WriteLogAndConsole($"Execution failed. Error message:{command.Error.Trim()}");
                    return false;
                }


                if (!string.IsNullOrWhiteSpace(command.Result.Trim()))
                {
                    WriteLogAndConsole($"Execution result:{command.Result.Trim()}");
                }

                return true;

            }

            return false;
        }

        public bool RunLocalCmd(string cmd = null)
        {
            if (string.IsNullOrWhiteSpace(cmd))
            {
                return true;
            }
            //// 源路径和目标路径
            //string sourcePath = @"D:\work\serverpublish\appsettings.Production.json";
            //  string destinationPath = @"C:\Users\Pingbin\Desktop\temp\0522";

            //// xcopy 命令参数
            // string xcopyArgs = $@"""{sourcePath}"" ""{destinationPath}"" /Y /R /C /H";

            Console.WriteLine($"Begin Run LocalCommand:{cmd}");
            // 创建并配置 ProcessStartInfo
            ProcessStartInfo processInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/C {cmd}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            // 启动进程
            using (Process process = new Process { StartInfo = processInfo })
            {
                process.Start();

                // 读取输出和错误流
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();

                // 等待进程结束
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

        private void SetCheckTimeValue(string timeValue = "")
        {
            if (_config.CheckTimeStr.Equals(timeValue))
            {
                return;
            }
            _config.CheckTimeStr = timeValue;
            AppConfig.UpdateConfig();
        }
    }
}
