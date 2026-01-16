using System;
using System.IO;
using System.Text.Json;

namespace DramaticAdhan
{
    public class AppConfig
    {
        public string City { get; set; } = "Mecca";
        public string Country { get; set; } = "Saudi Arabia";
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }

        public static string GetConfigPath() =>
            Path.Combine(AppContext.BaseDirectory, "config.json");

        public void Save()
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(GetConfigPath(), JsonSerializer.Serialize(this, options));
        }

        public static AppConfig Load()
        {
            string path = GetConfigPath();
            if (!File.Exists(path))
                return new AppConfig();

            try
            {
                string json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
            }
            catch
            {
                return new AppConfig();
            }
        }
    }
}
