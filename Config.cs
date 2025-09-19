

using Newtonsoft.Json;

namespace AutoUploadToFTP
{
    public class Config
    {
        public string Name { get; set; }

        public string Host { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public int Sshport { get; set; } = 22;
        public int Ftpport { get; set; } = 22;
        public string Localpath { get; set; }
        public string Remotepath { get; set; }
        public string Cmd { get; set; }
        public bool UploadAllFilesWhenAppStart { get; set; }
        public string CheckTimeStr { get; set; }
        [JsonIgnore]
        public DateTime? CheckTime
        {
            get
            {
                DateTime? checkTime = null;
                if (!string.IsNullOrWhiteSpace(CheckTimeStr))
                {
                    var timeList = CheckTimeStr.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
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
                return checkTime;

            }
        }

        public string IgnorePath { get; set; }
        public string LocalCmd { get; set; }
        [JsonIgnore]
        public string[] IgnorePathList => string.IsNullOrWhiteSpace(IgnorePath) ? null : IgnorePath.Replace("\\", "/").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public class AppSettingsRoot
    {
        public List<Config> ServerConfigurations { get; set; }
    }
}
