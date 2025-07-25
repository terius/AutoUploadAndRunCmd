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
        DateTime _checkTime;
        int _tryTime = 5;


        Dictionary<string, DateTime> _checkFileTime = new();
        HashSet<UploadFileInfo> _newFiles = new();
        SftpClient _ftpClient = null;
        SshClient _sshClient = null;
        bool hasChange = true;
        bool isFirst = true;
        string _localCmd;
        public AutoUploadFileAndRunCmdForLinux(
            string host,
            string username,
            string password,
            string localDirectory,
            string remoteDirectory,
            string cmd,
            DateTime checkTime,
            int sshPort = 22,
            int ftpPort = 22,
            bool uploadAll = false,
            string ignorePath = "",
            string localCmd = null
            )
        {

            _host = host;
            _username = username;
            _password = password;
            _localDirectory = localDirectory;
            _remoteDirectory = remoteDirectory;
            _cmd = cmd;
            _sshPort = sshPort;
            _ftpPort = ftpPort;
            _uploadAllFileWhenFirstScan = uploadAll;
            _checkTime = checkTime;
            _ignorePathList = string.IsNullOrEmpty(ignorePath) ? null : ignorePath.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries); ;
            _localCmd = localCmd;
        }

        private void WriteLogAndConsole(string message)
        {
            Console.WriteLine(message);
            Log.Write(message);
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
            if (string.IsNullOrWhiteSpace(_localDirectory))
            {
                WriteLogAndConsole($"Local directory can not be empty!");
                return;
            }
            var aa = File.GetLastWriteTime(_localDirectory);
            if (string.IsNullOrWhiteSpace(_remoteDirectory))
            {
                WriteLogAndConsole($"Remote directory can not be empty!");
                return;
            }



            var isConnected = ConnectToFtp();
            if (!isConnected)
            {
                return;
            }

            var rootDir = new Dir(_localDirectory);


            // 创建一个新的 FileSystemWatcher 实例
            FileSystemWatcher watcher = new(_localDirectory)
            {
                // 设置要监视的事件类型
                NotifyFilter = NotifyFilters.Attributes | NotifyFilters.CreationTime | NotifyFilters.DirectoryName | NotifyFilters.FileName | NotifyFilters.LastAccess
                                  | NotifyFilters.LastWrite | NotifyFilters.Security | NotifyFilters.Size
            };

            // 添加事件处理程序
            watcher.Changed += OnChanged;
            watcher.Created += OnChanged;
            watcher.Deleted += OnChanged;
            watcher.Renamed += OnChanged;

            // 设置为递归监视所有子文件夹
            watcher.IncludeSubdirectories = true;

            // 启动监视
            watcher.EnableRaisingEvents = true;

          



            var uploadResult = true;
            while (true)
            {
                if (!hasChange)
                {
                    Thread.Sleep(1000);
                    continue;
                }

                if (!isFirst)
                {
                    Thread.Sleep(3000);
                }
                if (uploadResult)
                {
                    _newFiles.Clear();
                }
                RunLocalCmd(_localCmd);

                ScanDir(rootDir);
                if (_newFiles.Where(d => d.FileType != 2).ToList().Count > 0)
                {
                    var actionTime = DateTime.Now.AddMinutes(-1).ToString("HH:mm");

                    WriteLogAndConsole("\r\nFiles changes detected...");
                    foreach (var file in _newFiles)
                    {
                        WriteLogAndConsole($"【{DateTime.Now:yyyy-MM-dd HH:mm:ss}】Get {file.FileTypeText}:{file.FileName}");
                    }


                    
                    for (int i = 0; i < 5; i++)
                    {
                        uploadResult = UploadFileUsingSsh();
                        if (uploadResult)
                        {
                            break;
                        }
                        CloseFTP();
                    }
                   
                    if (uploadResult)
                    {
                        Thread.Sleep(3000);
                        var runCmdResult = RunCmd(_cmd);
                        WriteLogAndConsole($"---------------------------Command execution {(runCmdResult ? "successfully" : "failed")}---------------------------");
                    }
                    else
                    {
                        foreach (var failFile in _newFiles)
                        {
                            failFile.FileType = 2;
                        }
                        SetCheckTimeValue(actionTime);
                    }
                }
                hasChange = false;
                WriteLogAndConsole($"Total number of files monitored is {_checkFileTime.Count}");
                if (isFirst)
                {
                    SetCheckTimeValue();
                }
                isFirst = false;

                Thread.Sleep(3000);

            }
        }

        private void OnChanged(object source, FileSystemEventArgs e)
        {
            hasChange = true;
        }

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
                    if (_uploadAllFileWhenFirstScan || !isFirst || fileInfo.LastWriteTime > _checkTime)
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
                    WriteLogAndConsole($"---------------------------File upload {(hasError ? "failed" : "successfully")}---------------------------");
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
            Configuration config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);

            // 获取appSettings节点
            AppSettingsSection appSettings = config.AppSettings;

            // 修改键值对
            appSettings.Settings["CheckTime"].Value = timeValue;

            // 保存修改
            config.Save(ConfigurationSaveMode.Modified);
            ConfigurationManager.RefreshSection("appSettings");
        }
    }
}
