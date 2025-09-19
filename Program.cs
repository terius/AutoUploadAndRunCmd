namespace AutoUploadToFTP;
class Program
{

    static void Main()
    {
        var baseDirectory = AppDomain.CurrentDomain.BaseDirectory.Replace("\\", "");
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
            AppConfig.GetServerConfigurations();
            //foreach (var item in AppConfig.AppSettings.ServerConfigurations)
            //{
            //    Task.Run(() =>
            //    {
            //        var autoRun = new AutoUploadFileAndRunCmdForLinux(item);
            //        autoRun.Run();
            //    });
            //}

            // 创建一个列表来持有所有的 uploader 实例，以便后续可以正确释放
            var uploaders = new List<AutoUploader>();

            foreach (var item in AppConfig.AppSettings.ServerConfigurations)
            {
                // 不需要 Task.Run，因为 FileSystemWatcher 是异步的
                // AutoUploader 的构造函数会启动监控
                var uploader = new AutoUploader(item);
                uploaders.Add(uploader);
            }

            Console.WriteLine("Monitoring started for all configurations. Press any key to exit...");
            Console.ReadKey(); // 等待用户输入以退出程序

            // 程序退出前，释放所有资源
            foreach (var uploader in uploaders)
            {
                uploader.Dispose();
            }
        }
        finally
        {
            // 释放互斥体，允许其他实例在本次运行结束后启动
            mutex.ReleaseMutex();
        }


    }

}
