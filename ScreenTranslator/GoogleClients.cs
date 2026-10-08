using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace ScreenTranslator
{
    internal static class GoogleCodes
    {
        /// <summary>Maps the app's DeepL-style target codes to Google's (EN-US → en, ZH-HANT → zh-TW).</summary>
        public static string Target(string code) => code.ToUpperInvariant() switch
        {
            "ZH" or "ZH-HANS" => "zh-CN",
            "ZH-HANT" => "zh-TW",
            "PT-BR" or "PT-PT" => "pt",
            var other => other.Split('-')[0].ToLowerInvariant()
        };
    }

    /// <summary>
    /// Google Translate's free web endpoint (the one browser extensions use): no key or signup,
    /// but unofficial, so Google may rate-limit it under heavy use.
    /// </summary>
    internal sealed class GoogleFreeClient(HttpClient http) : ITranslationClient
    {
        // translate_a/t takes many q= values per request and answers with one result per q.
        // The second host is a fallback for when the first one is rate-limited.
        private static readonly string[] Endpoints =
        [
            "https://translate.googleapis.com/translate_a/t?client=gtx",
            "https://clients5.google.com/translate_a/t?client=dict-chrome-ex",
        ];

        private const int MaxCharsPerRequest = 4000;

        public async Task<string[]> TranslateAsync(IReadOnlyList<string> texts, SourceLanguage source, string targetCode)
        {
            var results = new List<string>(texts.Count);
            foreach (var batch in BatchByLength(texts))
                results.AddRange(await RequestAsync(batch, source.GoogleCode, GoogleCodes.Target(targetCode)));
            return results.ToArray();
        }

        private async Task<string[]> RequestAsync(List<string> texts, string sourceLang, string targetLang)
        {
            HttpStatusCode? lastStatus = null;
            foreach (var endpoint in Endpoints)
            {
                var form = new List<KeyValuePair<string, string>> { new("sl", sourceLang), new("tl", targetLang) };
                form.AddRange(texts.Select(t => new KeyValuePair<string, string>("q", t)));

                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = new FormUrlEncodedContent(form) };
                request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36");

                try
                {
                    using var response = await http.SendAsync(request);
                    var body = await response.Content.ReadAsStringAsync();
                    if (response.IsSuccessStatusCode && TryParse(body, texts.Count, out var translations))
                        return translations;

                    lastStatus = response.StatusCode;
                    Log.Warn($"Google free endpoint {new Uri(endpoint).Host} returned {(int)response.StatusCode}");
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                {
                    Log.Warn($"Google free endpoint {new Uri(endpoint).Host} failed: {ex.Message}");
                }
            }

            throw new TranslationException(lastStatus == HttpStatusCode.TooManyRequests
                ? "Google's free translator is rate-limiting this network. Wait a few minutes, or use your own DeepL or Google Cloud key in the game bar › Settings."
                : "Can't reach Google Translate. Check your internet connection.");
        }

        /// <summary>The reply is a JSON array with one entry per q: a string, or [translation, detectedLanguage].</summary>
        private static bool TryParse(string body, int expected, out string[] translations)
        {
            translations = [];
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.ValueKind != JsonValueKind.Array) return false;

                translations = doc.RootElement.EnumerateArray()
                    .Select(e => e.ValueKind == JsonValueKind.Array ? e[0].GetString() ?? "" : e.GetString() ?? "")
                    .ToArray();
                return translations.Length == expected;
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or IndexOutOfRangeException)
            {
                return false; // e.g. an HTML "unusual traffic" page
            }
        }

        private static IEnumerable<List<string>> BatchByLength(IReadOnlyList<string> texts)
        {
            var batch = new List<string>();
            int length = 0;
            foreach (var text in texts)
            {
                if (batch.Count > 0 && length + text.Length > MaxCharsPerRequest)
                {
                    yield return batch;
                    batch = [];
                    length = 0;
                }
                batch.Add(text);
                length += text.Length;
            }
            if (batch.Count > 0) yield return batch;
        }
    }

    /// <summary>Google Cloud Translation (Basic, v2) with the user's own API key.</summary>
    internal sealed class GoogleCloudClient(HttpClient http, string apiKey) : ITranslationClient
    {
        private const int MaxTextsPerRequest = 128; // API limit

        public async Task<string[]> TranslateAsync(IReadOnlyList<string> texts, SourceLanguage source, string targetCode)
        {
            if (apiKey.Length == 0)
                throw new TranslationException("No Google Cloud API key set. Open the game bar › Settings and paste your key, or pick the free Google option.");

            var results = new List<string>(texts.Count);
            foreach (var chunk in texts.Chunk(MaxTextsPerRequest))
                results.AddRange(await RequestAsync(chunk, source.GoogleCode, GoogleCodes.Target(targetCode)));
            return results.ToArray();
        }

        private async Task<string[]> RequestAsync(string[] texts, string sourceLang, string targetLang)
        {
            var body = JsonSerializer.Serialize(new { q = texts, source = sourceLang, target = targetLang, format = "text" });
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://translation.googleapis.com/language/translate/v2")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            // A header rather than ?key= in the URL, which can end up in proxy and network logs
            request.Headers.Add("X-goog-api-key", apiKey);

            HttpResponseMessage response;
            try
            {
                response = await http.SendAsync(request);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                throw new TranslationException($"Can't reach Google Cloud Translation: {ex.Message}");
            }

            using (response)
            {
                var responseBody = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    var reason = ErrorMessage(responseBody);
                    Log.Warn($"Google Cloud error: {(int)response.StatusCode} - {reason}");
                    throw new TranslationException((int)response.StatusCode switch
                    {
                        400 when reason.Contains("API key", StringComparison.OrdinalIgnoreCase) =>
                            "Google Cloud rejected the API key. Check it in the game bar › Settings.",
                        403 => $"Google Cloud refused the request: {reason}",
                        429 => "Your Google Cloud translation quota is used up for now.",
                        >= 500 => "Google Cloud Translation is temporarily unavailable.",
                        _ => $"Google Cloud returned an error ({(int)response.StatusCode}): {reason}"
                    });
                }

                using var doc = JsonDocument.Parse(responseBody);
                return doc.RootElement.GetProperty("data").GetProperty("translations")
                    .EnumerateArray()
                    .Select(t => t.GetProperty("translatedText").GetString() ?? "")
                    .ToArray();
            }
        }

        private static string ErrorMessage(string body)
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                return doc.RootElement.GetProperty("error").GetProperty("message").GetString() ?? body;
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
            {
                return body.Length > 200 ? body[..200] : body;
            }
        }
    }
}
