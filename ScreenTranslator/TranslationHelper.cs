using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace ScreenTranslator
{
    public class TranslationHelper
    {
        private readonly HttpClient _httpClient = new HttpClient();
        private readonly Dictionary<string, string> _cache = new Dictionary<string, string>();
        private string _targetLang = "EN";

        public void SetTargetLang(string langCode)
        {
            _targetLang = langCode;
            _cache.Clear(); // old cached translations are in the wrong language now
        }

        public async Task<string?> TranslateAsync(string text, string? targetLang = null)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            var lang = targetLang ?? _targetLang;

            // Check cache first
            if (_cache.TryGetValue(text, out var cachedTranslation))
            {
                System.Diagnostics.Debug.WriteLine($"Cache hit for: '{text}'");
                return cachedTranslation;
            }

            var url = "https://api-free.deepl.com/v2/translate";

            var requestBody = new
            {
                text = new[] { text },
                target_lang = lang
            };

            var jsonContent = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("Authorization", $"DeepL-Auth-Key {Secrets.DeepLApiKey}");
            request.Content = content;

            try
            {
                var response = await _httpClient.SendAsync(request);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    System.Diagnostics.Debug.WriteLine($"DeepL error: {response.StatusCode} - {responseBody}");
                    return null;
                }

                using var doc = JsonDocument.Parse(responseBody);
                var translatedText = doc.RootElement
                    .GetProperty("translations")[0]
                    .GetProperty("text")
                    .GetString();

                if (translatedText != null)
                {
                    _cache[text] = translatedText;
                }

                return translatedText;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Translation exception: {ex.Message}");
                return null;
            }
        }
    }
}