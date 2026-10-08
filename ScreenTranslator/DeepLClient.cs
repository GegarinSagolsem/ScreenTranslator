using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScreenTranslator
{
    public record DeepLUsage(long CharacterCount, long CharacterLimit)
    {
        // Pro accounts without a spending cap report a huge placeholder limit
        public bool Unlimited => CharacterLimit >= 1_000_000_000_000;
        public double Fraction => CharacterLimit > 0 ? Math.Min(1.0, (double)CharacterCount / CharacterLimit) : 0;
    }

    /// <summary>DeepL API with the user's own key (Free or Pro), plus glossary sync and usage.</summary>
    internal sealed class DeepLClient(HttpClient http) : ITranslationClient
    {
        private const int MaxTextsPerRequest = 50; // DeepL limit
        private const string GlossaryPrefix = "ScreenTranslator ";
        private static readonly TimeSpan UsageRefreshInterval = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan GlossaryRetryDelay = TimeSpan.FromSeconds(30);

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private string _apiKey = "";
        private DateTime _lastUsageRefresh = DateTime.MinValue;

        // Glossary terms as DeepL TSV, plus the server-side glossary currently matching them
        private string _glossaryTsv = "";
        private string _glossaryHash = "";
        private string? _glossaryName;
        private string? _glossaryId;
        private DateTime _nextGlossaryAttempt = DateTime.MinValue;

        /// <summary>Characters used this billing period; null until fetched or when there is no key.</summary>
        public DeepLUsage? Usage { get; private set; }

        public event Action? UsageChanged;

        public void SetApiKey(string apiKey)
        {
            if (apiKey == _apiKey) return;

            _apiKey = apiKey;
            _glossaryName = null; // glossaries live on the account, so look them up again
            Usage = null; // belonged to the old key
            UsageChanged?.Invoke();
        }

        /// <returns>True when the terms changed, so cached translations are stale.</returns>
        public bool SetGlossary(IEnumerable<GlossaryEntry> entries)
        {
            // TSV can't hold tabs or newlines, and DeepL rejects duplicate sources
            static string Clean(string s) => s.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim();

            var lines = entries
                .Select(e => (Source: Clean(e.Source), Target: Clean(e.Target)))
                .Where(e => e.Source.Length > 0 && e.Target.Length > 0)
                .GroupBy(e => e.Source)
                .Select(g => $"{g.Key}\t{g.Last().Target}")
                .Order(StringComparer.Ordinal);

            var tsv = string.Join("\n", lines);
            if (tsv == _glossaryTsv) return false;

            _glossaryTsv = tsv;
            _glossaryHash = tsv.Length == 0
                ? ""
                : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(tsv)))[..10].ToLowerInvariant();
            _glossaryName = null;
            _nextGlossaryAttempt = DateTime.MinValue;
            return true;
        }

        // DeepL Free keys end in ":fx" and only work on the free endpoint
        private string BaseUrl => _apiKey.EndsWith(":fx", StringComparison.Ordinal)
            ? "https://api-free.deepl.com"
            : "https://api.deepl.com";

        public async Task<string[]> TranslateAsync(IReadOnlyList<string> texts, SourceLanguage source, string targetCode)
        {
            if (_apiKey.Length == 0)
                throw new TranslationException("No DeepL API key set. Open the game bar › Settings and paste your key, or pick the free Google option.");

            var glossaryId = await EnsureGlossaryAsync(source.DeepLCode, targetCode);

            var results = new List<string>(texts.Count);
            foreach (var chunk in texts.Chunk(MaxTextsPerRequest))
                results.AddRange(await RequestAsync(chunk, source.DeepLCode, targetCode, glossaryId));

            if (DateTime.UtcNow - _lastUsageRefresh > UsageRefreshInterval)
                _ = RefreshUsageAsync();

            return results.ToArray();
        }

        public async Task RefreshUsageAsync()
        {
            var apiKey = _apiKey;
            if (apiKey.Length == 0) return;

            _lastUsageRefresh = DateTime.UtcNow;
            try
            {
                using var response = await SendAsync(HttpMethod.Get, "/v2/usage");
                var body = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    Log.Warn($"DeepL usage error: {(int)response.StatusCode} - {body}");
                    return;
                }

                if (apiKey != _apiKey) return; // key changed while we were waiting

                using var doc = JsonDocument.Parse(body);
                Usage = new DeepLUsage(
                    doc.RootElement.GetProperty("character_count").GetInt64(),
                    doc.RootElement.GetProperty("character_limit").GetInt64());
                UsageChanged?.Invoke();
            }
            catch (Exception ex) when (IsTransient(ex) || ex is JsonException or KeyNotFoundException)
            {
                Log.Warn($"DeepL usage check failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Returns the id of a DeepL glossary holding the current terms for this language pair,
        /// creating it if needed, or null when there are no terms or DeepL can't be reached.
        /// DeepL glossaries are used (rather than pre-substituting terms) because they keep the grammar
        /// intact ("Rufy's crew") and DeepL won't "correct" a custom name back to its usual spelling.
        /// </summary>
        private async Task<string?> EnsureGlossaryAsync(string sourceLang, string targetLang)
        {
            if (_glossaryTsv.Length == 0) return null;

            // Glossaries are keyed by base language: EN-US → en, ZH-HANS → zh
            string source = sourceLang.Split('-')[0].ToLowerInvariant();
            string target = targetLang.Split('-')[0].ToLowerInvariant();
            string name = $"{GlossaryPrefix}{source}-{target} {_glossaryHash}";
            if (name == _glossaryName) return _glossaryId;
            if (DateTime.UtcNow < _nextGlossaryAttempt) return null;

            try
            {
                string? id = null;

                // Reuse the glossary from an earlier session; delete our stale ones for this pair
                using (var list = await SendAsync(HttpMethod.Get, "/v2/glossaries"))
                {
                    list.EnsureSuccessStatusCode();
                    using var doc = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
                    foreach (var g in doc.RootElement.GetProperty("glossaries").EnumerateArray())
                    {
                        var gName = g.GetProperty("name").GetString() ?? "";
                        var gId = g.GetProperty("glossary_id").GetString()!;
                        if (gName == name && id == null)
                            id = gId;
                        else if (gName.StartsWith($"{GlossaryPrefix}{source}-{target} ", StringComparison.Ordinal))
                            await DeleteGlossaryAsync(gId);
                    }
                }

                if (id == null)
                {
                    using var created = await SendAsync(HttpMethod.Post, "/v2/glossaries", new
                    {
                        name,
                        source_lang = source,
                        target_lang = target,
                        entries = _glossaryTsv,
                        entries_format = "tsv"
                    });
                    var body = await created.Content.ReadAsStringAsync();
                    if (!created.IsSuccessStatusCode)
                    {
                        // e.g. a language pair DeepL has no glossary support for: translate without it
                        Log.Warn($"DeepL glossary create failed: {(int)created.StatusCode} - {body}");
                        _glossaryName = name;
                        _glossaryId = null;
                        return null;
                    }

                    using var doc = JsonDocument.Parse(body);
                    id = doc.RootElement.GetProperty("glossary_id").GetString();
                }

                _glossaryName = name;
                _glossaryId = id;
                return id;
            }
            catch (Exception ex) when (IsTransient(ex) || ex is JsonException or KeyNotFoundException)
            {
                Log.Warn($"DeepL glossary sync failed: {ex.Message}");
                _nextGlossaryAttempt = DateTime.UtcNow + GlossaryRetryDelay;
                return null;
            }
        }

        private async Task DeleteGlossaryAsync(string id)
        {
            using var response = await SendAsync(HttpMethod.Delete, $"/v2/glossaries/{id}");
            Log.Info($"Deleted stale DeepL glossary: {(int)response.StatusCode}");
        }

        private async Task<string[]> RequestAsync(string[] texts, string sourceLang, string targetLang, string? glossaryId)
        {
            HttpResponseMessage response;
            try
            {
                response = await SendAsync(HttpMethod.Post, "/v2/translate", new
                {
                    text = texts,
                    source_lang = sourceLang,
                    target_lang = targetLang,
                    glossary_id = glossaryId
                });
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
                    Log.Warn($"DeepL error: {(int)response.StatusCode} - {responseBody}");
                    if (glossaryId != null && response.StatusCode == HttpStatusCode.NotFound)
                        _glossaryName = null; // glossary was deleted elsewhere; recreate on the next frame
                    throw new TranslationException(DescribeError(response.StatusCode));
                }

                using var doc = JsonDocument.Parse(responseBody);
                return doc.RootElement.GetProperty("translations")
                    .EnumerateArray()
                    .Select(t => t.GetProperty("text").GetString() ?? "")
                    .ToArray();
            }
        }

        private Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? jsonBody = null)
        {
            var request = new HttpRequestMessage(method, BaseUrl + path);
            request.Headers.Add("Authorization", $"DeepL-Auth-Key {_apiKey}");
            if (jsonBody != null)
                request.Content = new StringContent(JsonSerializer.Serialize(jsonBody, JsonOptions), Encoding.UTF8, "application/json");
            return http.SendAsync(request);
        }

        private static bool IsTransient(Exception ex) =>
            ex is HttpRequestException or TaskCanceledException or ObjectDisposedException;

        private static string DescribeError(HttpStatusCode status) => (int)status switch
        {
            401 or 403 => "DeepL rejected the API key. Check it in the game bar › Settings.",
            456 => "Your DeepL character quota for this month is used up.",
            429 => "DeepL is rate-limiting requests. Try a slower capture interval.",
            >= 500 => "DeepL is temporarily unavailable.",
            _ => $"DeepL returned an error ({(int)status} {status})."
        };
    }
}
