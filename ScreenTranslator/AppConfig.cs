using System;
using System.IO;
using System.Text.Json;

namespace ScreenTranslator
{
    public class AppConfig
    {
        public string DeepLApiKey { get; set; } = "";

        private static string ConfigPath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ScreenTranslator", "config.json");

        public static AppConfig Load()
        {
            if (!File.Exists(ConfigPath))
                return new AppConfig();

            var json = File.ReadAllText(ConfigPath);
            return JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
        }

        public void Save()
        {
            var dir = Path.GetDirectoryName(ConfigPath)!;
            Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(this);
            File.WriteAllText(ConfigPath, json);
        }
    }
}