using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScreenTranslator
{
    public class TranslationException(string message) : Exception(message);

    public sealed class TranslationHelper : IDisposable
    {
        private const int MaxTextsPerRequest = 50; // DeepL limit
        private const int MaxCacheEntries = 2000;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(10) };
        private readonly Dictionary<string, string> _cache = new();
        private string _apiKey = "";
        private string? _sourceLang;
        private string _targetLang = "EN-US";

        public void SetApiKey(string apiKey) => _apiKey = apiKey;

        public void SetLanguages(string? sourceLang, string targetLang)
        {
            if (targetLang != _targetLang)
                _cache.Clear(); // cached translations are in the old target language

            _sourceLang = sourceLang;
            _targetLang = targetLang;
        }

        // DeepL Free keys end in ":fx" and only work on the free endpoint
        private string Endpoint => _apiKey.EndsWith(":fx", StringComparison.Ordinal)
            ? "https://api-free.deepl.com/v2/translate"
            : "https://api.deepl.com/v2/translate";

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

            if (_apiKey.Length == 0)
                throw new TranslationException("No DeepL API key set. Right-click the tray icon and choose \"Set DeepL API key\".");

            foreach (var chunk in missing.Chunk(MaxTextsPerRequest))
            {
                var translated = await RequestAsync(chunk.Select(i => texts[i]).ToArray());
                for (int j = 0; j < chunk.Length && j < translated.Length; j++)
                {
                    results[chunk[j]] = translated[j];
                    Remember(texts[chunk[j]], translated[j]);
                }
            }

            return results;
        }

        private async Task<string[]> RequestAsync(string[] texts)
        {
            var body = JsonSerializer.Serialize(new
            {
                text = texts,
                source_lang = _sourceLang,
                target_lang = _targetLang
            }, JsonOptions);

            using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("Authorization", $"DeepL-Auth-Key {_apiKey}");

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(request);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                throw new TranslationException($"Can't reach DeepL: {ex.Message}");
            }

            using (response)
            {
                var responseBody = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    Debug.WriteLine($"DeepL error: {(int)response.StatusCode} - {responseBody}");
                    throw new TranslationException(DescribeError(response.StatusCode));
                }

                using var doc = JsonDocument.Parse(responseBody);
                return doc.RootElement.GetProperty("translations")
                    .EnumerateArray()
                    .Select(t => t.GetProperty("text").GetString() ?? "")
                    .ToArray();
            }
        }

        private static string DescribeError(HttpStatusCode status) => (int)status switch
        {
            401 or 403 => "DeepL rejected the API key. Set a valid key from the tray menu.",
            456 => "Your DeepL character quota for this month is used up.",
            429 => "DeepL is rate-limiting requests. Try a slower capture interval.",
            >= 500 => "DeepL is temporarily unavailable.",
            _ => $"DeepL returned an error ({(int)status} {status})."
        };

        private void Remember(string text, string translation)
        {
            if (_cache.Count >= MaxCacheEntries)
                _cache.Clear();
            _cache[text] = translation;
        }

        public void Dispose() => _httpClient.Dispose();
    }
}
