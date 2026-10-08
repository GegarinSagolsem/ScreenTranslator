using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace ScreenTranslator
{
    public class AppConfig
    {
        public string DeepLApiKey { get; set; } = "";
        public string SourceLanguage { get; set; } = "ja";
        public string TargetLanguage { get; set; } = "EN-US";
        public double OverlayOpacity { get; set; } = 0.75;
        public int CaptureIntervalMs { get; set; } = 500;
        public bool GameBarHotkeyEnabled { get; set; } = true;
        public List<GlossaryEntry> Glossary { get; set; } = new();
        public Dictionary<string, WidgetState> GameBarWidgets { get; set; } = new();

        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        public static string ConfigPath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ScreenTranslator", "config.json");

        // DEEPL_API_KEY wins over the saved key, so dev machines never need it on disk
        public string GetApiKey()
        {
            var fromEnv = Environment.GetEnvironmentVariable("DEEPL_API_KEY");
            return string.IsNullOrWhiteSpace(fromEnv) ? DeepLApiKey.Trim() : fromEnv.Trim();
        }

        public static AppConfig Load()
        {
            AppConfig config;
            try
            {
                config = File.Exists(ConfigPath)
                    ? JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigPath)) ?? new AppConfig()
                    : new AppConfig();
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                Debug.WriteLine($"Config unreadable, using defaults: {ex.Message}");
                config = new AppConfig();
            }

            config.OverlayOpacity = Math.Clamp(config.OverlayOpacity, 0.25, 1.0);
            config.CaptureIntervalMs = Math.Clamp(config.CaptureIntervalMs, 200, 2000);
            config.Glossary ??= new();
            config.GameBarWidgets ??= new();
            return config;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
                File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, JsonOptions));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Debug.WriteLine($"Could not save config: {ex.Message}");
            }
        }
    }

    /// <summary>A term that must always translate to exactly <see cref="Target"/>, e.g. a character name.</summary>
    public class GlossaryEntry
    {
        public string Source { get; set; } = "";
        public string Target { get; set; } = "";
    }

    /// <summary>Where a game bar widget sits and whether it is open or pinned.</summary>
    public class WidgetState
    {
        public double X { get; set; }
        public double Y { get; set; }
        public bool Visible { get; set; }
        public bool Pinned { get; set; }
    }
}
