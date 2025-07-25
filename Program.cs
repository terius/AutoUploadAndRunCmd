namespace AutoUploadToFTP;
class Program
{
    // private const string AppMutexName = "Global\\{8AA7CD65-FD7F-4F97-BE11-1A59E2B16E18}";
    static void Main()
    {
        var baseDirectory = AppDomain.CurrentDomain.BaseDirectory.Replace("\\","");
        var appMutexName = "Global\\{" + baseDirectory + "}";
        Mutex mutex = new Mutex(true, appMutexName, out bool createdNew);

        if (!createdNew)
        {
            // 如果互斥体已存在，则说明程序已在运行
            Console.WriteLine("应用程序已经在运行了。按任意键退出。");
            Console.ReadKey();
            return; // 退出当前实例
        }

        // 3. 确保在程序退出时释放互斥体
        // 使用 try...finally 结构可以保证即使程序发生异常，互斥体也能被正确释放。
        try
        {
            string host = GetAppSettingValue("Host");
            string username = GetAppSettingValue("Username") ?? "root";
            string password = GetAppSettingValue("Password");
            string sshPortStr = GetAppSettingValue("Sshport");
            string ftpPortStr = GetAppSettingValue("Ftpport");
            int sshPort = 22;
            if (!string.IsNullOrWhiteSpace(sshPortStr))
            {
                sshPort = int.Parse(sshPortStr);
            }
            int ftpPort = 22;
            if (!string.IsNullOrWhiteSpace(ftpPortStr))
            {
                ftpPort = int.Parse(ftpPortStr);
            }
            string localFilePath = GetAppSettingValue("Localpath");
            string remoteDirectory = GetAppSettingValue("Remotepath");
            string cmd = GetAppSettingValue("Cmd");
            bool uploadAllFilesWhenFirstScan = GetAppSettingValue("UploadAllFilesWhenAppStart") == "1";
            var checkTime = DateTime.Now;
            var sTimeStr = GetAppSettingValue("CheckTime");
            if (!string.IsNullOrWhiteSpace(sTimeStr))
            {
                var timeList = sTimeStr.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (timeList.Length == 3)
                {
                    checkTime = DateTime.Today.AddHours(double.Parse(timeList[0])).AddMinutes(double.Parse(timeList[1])).AddSeconds(double.Parse(timeList[2]));
                }
                else if (timeList.Length == 2)
                {
                    checkTime = DateTime.Today.AddHours(double.Parse(timeList[0])).AddMinutes(double.Parse(timeList[1]));
                }
                else if (timeList.Length == 1)
                {
                    checkTime = DateTime.Today.AddHours(double.Parse(timeList[0]));
                }
            }
            var ignorePath = GetAppSettingValue("IgnorePath");
            var localCmd = GetAppSettingValue("LocalCmd");
            var autoRun = new AutoUploadFileAndRunCmdForLinux(host, username, password, localFilePath, remoteDirectory,
                cmd, checkTime, sshPort, ftpPort, uploadAllFilesWhenFirstScan, ignorePath, localCmd);
            autoRun.Run();
            Console.WriteLine("Press any key to exit...");
            Console.ReadLine();
        }
        finally
        {
            // 释放互斥体，允许其他实例在本次运行结束后启动
            mutex.ReleaseMutex();
            mutex.Dispose();
        }


    }



    private static string GetAppSettingValue(string key)
    {
        return System.Configuration.ConfigurationManager.AppSettings[key];
    }

}
