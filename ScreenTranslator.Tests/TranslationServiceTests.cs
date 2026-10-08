using System.Net;
using System.Net.Http;
using System.Text;
using System.Web;

namespace ScreenTranslator.Tests;

/// <summary>The translation services, against a fake network: no real requests are made.</summary>
[TestClass]
public sealed class TranslationServiceTests
{
    private static readonly SourceLanguage Japanese = Languages.All[0];

    private sealed class FakeNetwork(Func<HttpRequestMessage, string, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            lock (Requests) Requests.Add((request, body));
            return respond(request, body);
        }
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static string[] FormValues(string body, string name) =>
        HttpUtility.ParseQueryString(body).GetValues(name) ?? [];

    // Google free: answers with one translation per q=, as "T(<original>)"
    private static HttpResponseMessage EchoGoogle(string body) =>
        Json("[" + string.Join(",", FormValues(body, "q").Select(q => $"\"T({q})\"")) + "]");

    [TestMethod]
    public async Task GoogleFreeSendsEveryTextInOneRequest()
    {
        var network = new FakeNetwork((_, body) => EchoGoogle(body));
        var client = new GoogleFreeClient(new HttpClient(network));

        var result = await client.TranslateAsync(["おはよう", "ありがとう"], Japanese, "EN-US");

        CollectionAssert.AreEqual(new[] { "T(おはよう)", "T(ありがとう)" }, result);
        Assert.HasCount(1, network.Requests);
        Assert.AreEqual("ja", FormValues(network.Requests[0].Body, "sl").Single());
        Assert.AreEqual("en", FormValues(network.Requests[0].Body, "tl").Single());
    }

    [TestMethod]
    public async Task GoogleFreeReadsTranslationLanguagePairs()
    {
        var client = new GoogleFreeClient(new HttpClient(new FakeNetwork((_, _) => Json("[[\"hello\",\"ja\"]]"))));

        var result = await client.TranslateAsync(["こんにちは"], Japanese, "EN-US");

        Assert.AreEqual("hello", result.Single());
    }

    [TestMethod]
    public async Task GoogleFreeFallsBackWhenRateLimited()
    {
        var network = new FakeNetwork((request, body) => request.RequestUri!.Host == "translate.googleapis.com"
            ? new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent("<html>Sorry...</html>") }
            : EchoGoogle(body));
        var client = new GoogleFreeClient(new HttpClient(network));

        var result = await client.TranslateAsync(["おはよう"], Japanese, "EN-US");

        Assert.AreEqual("T(おはよう)", result.Single());
        Assert.HasCount(2, network.Requests);
    }

    [TestMethod]
    public async Task GoogleFreeExplainsWhenEveryEndpointIsRateLimited()
    {
        var client = new GoogleFreeClient(new HttpClient(new FakeNetwork((_, _) => new HttpResponseMessage(HttpStatusCode.TooManyRequests))));

        var error = await Assert.ThrowsExactlyAsync<TranslationException>(() => client.TranslateAsync(["おはよう"], Japanese, "EN-US"));

        Assert.Contains("rate-limiting", error.Message);
    }

    [TestMethod]
    public async Task GoogleFreeSplitsVeryLongBatches()
    {
        var network = new FakeNetwork((_, body) => EchoGoogle(body));
        var client = new GoogleFreeClient(new HttpClient(network));
        var texts = Enumerable.Range(0, 3).Select(i => new string((char)('あ' + i), 2500)).ToArray();

        var result = await client.TranslateAsync(texts, Japanese, "EN-US");

        Assert.HasCount(3, result);
        Assert.HasCount(3, network.Requests);
        Assert.AreEqual($"T({texts[2]})", result[2]);
    }

    [TestMethod]
    [DataRow("EN-US", "en")]
    [DataRow("ZH-HANS", "zh-CN")]
    [DataRow("ZH-HANT", "zh-TW")]
    [DataRow("PT-BR", "pt")]
    [DataRow("DE", "de")]
    public void MapsTargetLanguagesToGoogleCodes(string appCode, string googleCode) =>
        Assert.AreEqual(googleCode, GoogleCodes.Target(appCode));

    [TestMethod]
    public async Task GoogleCloudSendsTheKeyInAHeaderNotTheUrl()
    {
        var network = new FakeNetwork((_, _) => Json("{\"data\":{\"translations\":[{\"translatedText\":\"good morning\"}]}}"));
        var client = new GoogleCloudClient(new HttpClient(network), "secret-key");

        var result = await client.TranslateAsync(["おはよう"], Japanese, "EN-US");

        Assert.AreEqual("good morning", result.Single());
        var request = network.Requests.Single().Request;
        Assert.DoesNotContain("secret-key", request.RequestUri!.ToString());
        Assert.AreEqual("secret-key", request.Headers.GetValues("X-goog-api-key").Single());
    }

    [TestMethod]
    public async Task GoogleCloudExplainsAnInvalidKey()
    {
        var client = new GoogleCloudClient(new HttpClient(new FakeNetwork((_, _) =>
            Json("{\"error\":{\"message\":\"API key not valid. Please pass a valid API key.\"}}", HttpStatusCode.BadRequest))), "bad");

        var error = await Assert.ThrowsExactlyAsync<TranslationException>(() => client.TranslateAsync(["おはよう"], Japanese, "EN-US"));

        Assert.Contains("rejected the API key", error.Message);
    }

    [TestMethod]
    public async Task DeepLFreeKeysUseTheFreeEndpoint()
    {
        var network = new FakeNetwork((request, _) => request.RequestUri!.AbsolutePath == "/v2/usage"
            ? Json("{\"character_count\":5,\"character_limit\":500000}")
            : Json("{\"translations\":[{\"text\":\"good morning\"}]}"));
        var client = new DeepLClient(new HttpClient(network));
        client.SetApiKey("abc:fx");

        var result = await client.TranslateAsync(["おはよう"], Japanese, "EN-US");

        Assert.AreEqual("good morning", result.Single());
        var translate = network.Requests.First(r => r.Request.RequestUri!.AbsolutePath == "/v2/translate");
        Assert.AreEqual("api-free.deepl.com", translate.Request.RequestUri!.Host);
        Assert.AreEqual("DeepL-Auth-Key abc:fx", translate.Request.Headers.Authorization!.ToString());
        Assert.Contains("\"source_lang\":\"JA\"", translate.Body);
    }

    [TestMethod]
    public async Task DeepLExplainsAnExhaustedQuota()
    {
        var client = new DeepLClient(new HttpClient(new FakeNetwork((_, _) => new HttpResponseMessage((HttpStatusCode)456))));
        client.SetApiKey("abc");

        var error = await Assert.ThrowsExactlyAsync<TranslationException>(() => client.TranslateAsync(["おはよう"], Japanese, "EN-US"));

        Assert.Contains("quota", error.Message);
    }

    [TestMethod]
    public async Task RepeatedTextComesFromTheCacheAndBlanksAreSkipped()
    {
        var network = new FakeNetwork((_, body) => EchoGoogle(body));
        using var translator = new TranslationHelper(network);
        translator.SetLanguages(Japanese, "EN-US");

        var first = await translator.TranslateAsync(["", "  ", "おはよう"]);
        var second = await translator.TranslateAsync(["おはよう"]);

        CollectionAssert.AreEqual(new string?[] { null, null, "T(おはよう)" }, first);
        Assert.AreEqual("T(おはよう)", second.Single());
        Assert.HasCount(1, network.Requests);
        CollectionAssert.AreEqual(new[] { "おはよう" }, FormValues(network.Requests[0].Body, "q"));
    }
}
