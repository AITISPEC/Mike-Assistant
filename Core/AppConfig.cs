using System;
using System.IO;
using Newtonsoft.Json;

namespace MikeAssistant.Core
{
    public class AppConfig
    {
        public string GoogleApiKey { get; set; } = "";
        public string GoogleSearchEngineId { get; set; } = "32257d363415c4f9b";
        public string LmStudioUrl { get; set; } = "http://localhost:1234";

        public static AppConfig Load()
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "appsettings.json");
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                return JsonConvert.DeserializeObject<AppConfig>(json);
            }
            return new AppConfig();
        }
    }
}