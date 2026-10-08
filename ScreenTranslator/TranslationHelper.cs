using System.Net.Http;

namespace ScreenTranslator
{
    public class TranslationException(string message) : Exception(message);

    public enum TranslationService { GoogleFree, DeepL, GoogleCloud }

    /// <param name="KeyUrl">Where users get their own key; null when no key is needed.</param>
    public record TranslationServiceInfo(TranslationService Service, string Name, string Description, string? KeyUrl)
    {
        public static readonly TranslationServiceInfo[] All =
        [
            new(TranslationService.GoogleFree, "Google Translate (free, no key)",
                "Works right away with no signup. Uses Google's free web endpoint, which can be rate-limited under heavy use.",
                null),
            new(TranslationService.DeepL, "DeepL (your own API key)",
                "Best quality, and the only service that supports the glossary. DeepL's free API plan is enough for most people.",
                "https://www.deepl.com/your-account/keys"),
            new(TranslationService.GoogleCloud, "Google Cloud Translation (your own API key)",
                "Google's official API, with a free monthly allowance. Needs a Google Cloud project with the Cloud Translation API enabled.",
                "https://console.cloud.google.com/apis/credentials"),
        ];

        public static TranslationServiceInfo Of(TranslationService service) => All.First(s => s.Service == service);
    }

    /// <summary>A translation provider. Result i matches texts[i]; failures throw <see cref="TranslationException"/>.</summary>
    internal interface ITranslationClient
    {
        Task<string[]> TranslateAsync(IReadOnlyList<string> texts, SourceLanguage source, string targetCode);
    }

    /// <summary>
    /// Translates OCR'd text with the chosen service: caches results, skips blanks and batches the
    /// rest into as few requests as the service allows.
    /// </summary>
    public sealed class TranslationHelper : IDisposable
    {
        private const int MaxCacheEntries = 2000;

        private readonly HttpClient _http;
        private readonly Dictionary<string, string> _cache = new();
        private readonly DeepLClient _deepL;
        private ITranslationClient _client;
        private SourceLanguage _source = Languages.All[0];
        private string _target = "EN-US";

        public TranslationHelper() : this(new HttpClientHandler())
        {
        }

        /// <summary>Tests pass a fake handler so no real requests are made.</summary>
        internal TranslationHelper(HttpMessageHandler handler)
        {
            _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
            _deepL = new DeepLClient(_http);
            _deepL.UsageChanged += () => UsageChanged?.Invoke();
            _client = new GoogleFreeClient(_http);
        }

        public TranslationService Service { get; private set; } = TranslationService.GoogleFree;

        /// <summary>DeepL characters used this billing period; null for other services or until fetched.</summary>
        public DeepLUsage? Usage => Service == TranslationService.DeepL ? _deepL.Usage : null;

        public event Action? UsageChanged;

        public void Configure(TranslationService service, string apiKey)
        {
            if (service != Service)
                _cache.Clear(); // different services word things differently

            Service = service;
            _deepL.SetApiKey(service == TranslationService.DeepL ? apiKey : "");
            _client = service switch
            {
                TranslationService.DeepL => _deepL,
                TranslationService.GoogleCloud => new GoogleCloudClient(_http, apiKey),
                _ => new GoogleFreeClient(_http)
            };
            UsageChanged?.Invoke();
        }

        public void SetLanguages(SourceLanguage source, string targetCode)
        {
            if (targetCode != _target)
                _cache.Clear(); // cached translations are in the old target language

            _source = source;
            _target = targetCode;
        }

        /// <summary>Glossary terms; only DeepL can apply them.</summary>
        public void SetGlossary(IEnumerable<GlossaryEntry> entries)
        {
            if (_deepL.SetGlossary(entries) && Service == TranslationService.DeepL)
                _cache.Clear(); // cached translations predate the new terms
        }

        public Task RefreshUsageAsync() =>
            Service == TranslationService.DeepL ? _deepL.RefreshUsageAsync() : Task.CompletedTask;

        /// <summary>
        /// Translates all texts in as few requests as possible. Result i matches texts[i].
        /// Throws <see cref="TranslationException"/> with a user-facing message on failure.
        /// </summary>
        public async Task<string?[]> TranslateAsync(IReadOnlyList<string> texts)
        {
            var results = new string?[texts.Count];
            var missing = new List<int>();

            for (int i = 0; i < texts.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(texts[i])) continue;

                if (_cache.TryGetValue(texts[i], out var cached))
                    results[i] = cached;
                else
                    missing.Add(i);
            }

            if (missing.Count == 0) return results;

            var translated = await _client.TranslateAsync(missing.Select(i => texts[i]).ToList(), _source, _target);
            for (int j = 0; j < missing.Count && j < translated.Length; j++)
            {
                results[missing[j]] = translated[j];
                Remember(texts[missing[j]], translated[j]);
            }

            return results;
        }

        private void Remember(string text, string translation)
        {
            if (_cache.Count >= MaxCacheEntries)
                _cache.Clear();
            _cache[text] = translation;
        }

        public void Dispose() => _http.Dispose();
    }
}
