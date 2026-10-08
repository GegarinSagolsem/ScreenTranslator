using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScreenTranslator
{
    public class AppConfig
    {
        /// <summary>Null only in configs from before services existed; <see cref="Load"/> fills it in.</summary>
        [JsonConverter(typeof(JsonStringEnumConverter<TranslationService>))]
        public TranslationService? Service { get; set; }

        // Each user's own keys; the app never ships one
        public string DeepLApiKey { get; set; } = "";
        public string GoogleCloudApiKey { get; set; } = "";

        public string SourceLanguage { get; set; } = "ja";
        public string TargetLanguage { get; set; } = "EN-US";
        public double OverlayOpacity { get; set; } = 0.75;
        public double TextScale { get; set; } = 1.0;
        public int CaptureIntervalMs { get; set; } = 500;
        public bool MergeLines { get; set; } = true;
        public bool VerticalText { get; set; }

        /// <summary>Command id → "Ctrl+Shift+R". Missing means the default; "" means turned off.</summary>
        public Dictionary<string, string> Hotkeys { get; set; } = new();

        /// <summary>Read from older configs only (replaced by clearing the GameBar hotkey).</summary>
        [JsonPropertyName("GameBarHotkeyEnabled")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? LegacyGameBarHotkeyEnabled { get; set; }
        public List<GlossaryEntry> Glossary { get; set; } = new();
        public Dictionary<string, WidgetState> GameBarWidgets { get; set; } = new();

        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        public static string ConfigPath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ScreenTranslator", "config.json");

        public TranslationService ActiveService => Service ?? TranslationService.GoogleFree;

        /// <summary>Environment variable that overrides the saved key, so dev machines never need it on disk.</summary>
        public static string? KeyVariable(TranslationService service) => service switch
        {
            TranslationService.DeepL => "DEEPL_API_KEY",
            TranslationService.GoogleCloud => "GOOGLE_TRANSLATE_API_KEY",
            _ => null
        };

        public string GetApiKey(TranslationService service)
        {
            var variable = KeyVariable(service);
            var fromEnv = variable == null ? null : Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(fromEnv)) return fromEnv.Trim();

            return service switch
            {
                TranslationService.DeepL => DeepLApiKey.Trim(),
                TranslationService.GoogleCloud => GoogleCloudApiKey.Trim(),
                _ => ""
            };
        }

        public Hotkey GetHotkey(string commandId) =>
            Hotkey.Parse(Hotkeys.TryGetValue(commandId, out var keys)
                ? keys
                : HotkeyCommands.All.FirstOrDefault(c => c.Id == commandId)?.DefaultKeys);

        public void SetApiKey(TranslationService service, string key)
        {
            if (service == TranslationService.DeepL) DeepLApiKey = key.Trim();
            else if (service == TranslationService.GoogleCloud) GoogleCloudApiKey = key.Trim();
        }

        public static AppConfig Load() => LoadFrom(ConfigPath);

        internal static AppConfig LoadFrom(string path)
        {
            AppConfig config;
            try
            {
                config = File.Exists(path)
                    ? JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path)) ?? new AppConfig()
                    : new AppConfig();
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                Log.Warn($"Config unreadable, using defaults: {ex.Message}");
                config = new AppConfig();
            }

            config.OverlayOpacity = Math.Clamp(config.OverlayOpacity, 0.0, 1.0);
            config.TextScale = Math.Clamp(config.TextScale, TranslationLayout.MinTextScale, TranslationLayout.MaxTextScale);
            config.CaptureIntervalMs = Math.Clamp(config.CaptureIntervalMs, 200, 2000);
            config.Glossary ??= new();
            config.GameBarWidgets ??= new();
            config.Hotkeys ??= new();

            if (config.LegacyGameBarHotkeyEnabled == false)
                config.Hotkeys.TryAdd(HotkeyCommands.GameBar, "");
            config.LegacyGameBarHotkeyEnabled = null;

            // Older configs only knew DeepL: keep anyone who set a key on it, everyone else gets free Google
            config.Service ??= string.IsNullOrWhiteSpace(config.GetApiKey(TranslationService.DeepL))
                ? TranslationService.GoogleFree
                : TranslationService.DeepL;
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
                Log.Warn($"Could not save config: {ex.Message}");
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
