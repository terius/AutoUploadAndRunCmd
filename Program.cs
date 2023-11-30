namespace AutoUploadToFTP;
class Program
{

    static void Main()
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
        var autoRun = new AutoUploadFileAndRunCmdForLinux(host, username, password, localFilePath, remoteDirectory,
            cmd, sshPort, ftpPort, uploadAllFilesWhenFirstScan);
        autoRun.Run();

    }



    private static string GetAppSettingValue(string key)
    {
        return System.Configuration.ConfigurationManager.AppSettings[key];
    }

}
