using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScreenTranslator
{
    public class AppConfig
    {
        /// <summary>Null only in configs from before services existed; <see cref="Load"/> fills it in.</summary>
        [JsonConverter(typeof(JsonStringEnumConverter<TranslationService>))]
        public TranslationService? Service { get; set; }

        // Each user's own keys (the app never ships one), encrypted with Windows DPAPI so only this
        // Windows account on this PC can read them. Base64 in config.json.
        public string DeepLApiKeyProtected { get; set; } = "";
        public string GoogleCloudApiKeyProtected { get; set; } = "";

        /// <summary>Plain-text keys from older configs: read once, encrypted, and never written again.</summary>
        [JsonPropertyName("DeepLApiKey")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? LegacyDeepLApiKey { get; set; }

        [JsonPropertyName("GoogleCloudApiKey")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? LegacyGoogleCloudApiKey { get; set; }

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
        private static readonly byte[] KeyEntropy = Encoding.UTF8.GetBytes("ScreenTranslator API key");

        private string _path = ConfigPath;

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

            return GetSavedApiKey(service);
        }

        /// <summary>The key saved in config.json, decrypted; ignores the environment variable.</summary>
        public string GetSavedApiKey(TranslationService service) => service switch
        {
            TranslationService.DeepL => Unprotect(DeepLApiKeyProtected),
            TranslationService.GoogleCloud => Unprotect(GoogleCloudApiKeyProtected),
            _ => ""
        };

        public Hotkey GetHotkey(string commandId) =>
            Hotkey.Parse(Hotkeys.TryGetValue(commandId, out var keys)
                ? keys
                : HotkeyCommands.All.FirstOrDefault(c => c.Id == commandId)?.DefaultKeys);

        public void SetApiKey(TranslationService service, string key)
        {
            if (service == TranslationService.DeepL) DeepLApiKeyProtected = Protect(key.Trim());
            else if (service == TranslationService.GoogleCloud) GoogleCloudApiKeyProtected = Protect(key.Trim());
        }

        private static string Protect(string key) =>
            key.Length == 0
                ? ""
                : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(key), KeyEntropy, DataProtectionScope.CurrentUser));

        private static string Unprotect(string protectedKey)
        {
            if (protectedKey.Length == 0) return "";
            try
            {
                return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(protectedKey), KeyEntropy, DataProtectionScope.CurrentUser));
            }
            catch (Exception ex) when (ex is CryptographicException or FormatException)
            {
                // e.g. config.json copied from another PC or Windows account
                Log.Warn("A saved API key can't be decrypted on this Windows account; enter it again in Settings");
                return "";
            }
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
            config._path = path;
            config.Glossary ??= new();
            config.GameBarWidgets ??= new();
            config.Hotkeys ??= new();
            config.DeepLApiKeyProtected ??= "";
            config.GoogleCloudApiKeyProtected ??= "";

            // Encrypt plain-text keys left by older versions, and get them off the disk straight away
            bool hadPlainTextKeys = config.LegacyDeepLApiKey != null || config.LegacyGoogleCloudApiKey != null;
            if (hadPlainTextKeys)
            {
                if (!string.IsNullOrWhiteSpace(config.LegacyDeepLApiKey))
                    config.SetApiKey(TranslationService.DeepL, config.LegacyDeepLApiKey);
                if (!string.IsNullOrWhiteSpace(config.LegacyGoogleCloudApiKey))
                    config.SetApiKey(TranslationService.GoogleCloud, config.LegacyGoogleCloudApiKey);
                config.LegacyDeepLApiKey = config.LegacyGoogleCloudApiKey = null;
            }

            if (config.LegacyGameBarHotkeyEnabled == false)
                config.Hotkeys.TryAdd(HotkeyCommands.GameBar, "");
            config.LegacyGameBarHotkeyEnabled = null;

            // Older configs only knew DeepL: keep anyone who set a key on it, everyone else gets free Google
            config.Service ??= string.IsNullOrWhiteSpace(config.GetApiKey(TranslationService.DeepL))
                ? TranslationService.GoogleFree
                : TranslationService.DeepL;

            if (hadPlainTextKeys)
            {
                config.Save();
                Log.Info("Encrypted the API keys saved by an older version");
            }
            return config;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.WriteAllText(_path, JsonSerializer.Serialize(this, JsonOptions));
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
