using System;
using System.IO;
using System.Text.Json;

namespace DramaticAdhan
{
    public class AppConfig
    {
        public bool IsFirstRun { get; set; } = true;

        public string City { get; set; } = "Mecca";
        public string Country { get; set; } = "Saudi Arabia";

        public double? Latitude { get; set; }
        public double? Longitude { get; set; }

        public bool IsShia { get; set; } = false;

        private static string ConfigPath =>
            Path.Combine(AppContext.BaseDirectory, "config.json");

        public static AppConfig Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                    return JsonSerializer.Deserialize<AppConfig>(
                        File.ReadAllText(ConfigPath)) ?? new AppConfig();
            }
            catch { }

            return new AppConfig();
        }

        public void Save()
        {
            try
            {
                File.WriteAllText(
                    ConfigPath,
                    JsonSerializer.Serialize(this, new JsonSerializerOptions
                    {
                        WriteIndented = true
                    }));
            }
            catch { }
        }
    }
}