using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;

namespace AutoUploadToFTP
{
    public static class AppConfig
    {
        public static IConfiguration Configuration { get; }

        public static AppSettingsRoot AppSettings { get; private set; }
        static AppConfig()
        {
            Configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();
        }

        public static void GetServerConfigurations()
        {
            AppSettings = new AppSettingsRoot();
            AppSettings.ServerConfigurations = Configuration.GetSection("ServerConfigurations").Get<List<Config>>();
        }
        private static readonly string _filePath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");
        public static void UpdateConfig()
        {
            string updatedJson = JsonConvert.SerializeObject(AppSettings, Formatting.Indented);

            // 4. 写回文件
            File.WriteAllText(_filePath, updatedJson);
        }
    }
}
