using System.Drawing;
using Windows.Media.Ocr;

namespace ScreenTranslator.Tests;

/// <summary>Vertical text on a full manga page: speech bubbles are read, drawings are ignored.</summary>
[TestClass]
public sealed class MangaTests
{
    private static bool Covers(Rectangle found, Rectangle expected)
    {
        var overlap = Rectangle.Intersect(found, expected);
        return overlap.Width * overlap.Height >= 0.6 * expected.Width * expected.Height;
    }

    [TestMethod]
    public void FindsEveryBubbleAndIgnoresTheArt()
    {
        using var page = MangaPage.Draw(out var expected);

        var bubbles = SpeechBubbles.Find(page);
        try
        {
            Assert.HasCount(expected.Count, bubbles);
            foreach (var (_, area) in expected)
                Assert.IsTrue(bubbles.Any(b => Covers(b.Bounds, area)), $"no bubble found at {area}");
        }
        finally
        {
            bubbles.ForEach(b => b.Dispose());
        }
    }

    [TestMethod]
    public void PlainTextPageHasNoBubbles()
    {
        using var page = OcrTests.Vertical("吾輩は猫である。名前はまだ無い。どこで生れたかとんと見当がつかぬ。", "Yu Gothic UI", 10);

        Assert.IsEmpty(SpeechBubbles.Find(page));
    }

    [TestMethod]
    [DataRow("とこへ行くの", true)]
    [DataRow("嵐が来た。", true)]
    [DataRow("|-|_", false)]
    [DataRow("a", false)]
    [DataRow("ノ", false)]
    public void TellsTextFromDrawingNoise(string text, bool isText) =>
        Assert.AreEqual(isText, OcrHelper.LooksLikeText(text));

    [TestMethod]
    public async Task EachBubbleBecomesOneBlock()
    {
        if (!OcrEngine.AvailableRecognizerLanguages.Any(l => l.LanguageTag.StartsWith("ja", StringComparison.OrdinalIgnoreCase)))
            Assert.Inconclusive("The Windows OCR pack for Japanese isn't installed here.");

        using var page = MangaPage.Draw(out var expected);
        var ocr = new OcrHelper();
        ocr.SetLanguage(Languages.All[0]);

        var blocks = await ocr.RecognizeAsync(page, mergeLines: true, verticalText: true);

        Assert.HasCount(expected.Count, blocks);
        foreach (var (text, area) in expected)
        {
            var block = blocks.Single(b => Covers(new Rectangle((int)b.Bounds.X, (int)b.Bounds.Y, (int)b.Bounds.Width, (int)b.Bounds.Height), area));
            Assert.IsTrue(block.Vertical);
            Assert.IsGreaterThanOrEqualTo(0.75, Similarity(text, block.Text), $"read '{block.Text}' for '{text}'");
        }
    }

    /// <summary>Share of characters in common, ignoring punctuation (OCR often swaps … for ・ and similar).</summary>
    private static double Similarity(string expected, string actual)
    {
        static string Letters(string s) => new(s.Where(char.IsLetter).ToArray());
        var remaining = Letters(actual).ToList();
        int matched = Letters(expected).Count(c => remaining.Remove(c));
        return (double)matched / Math.Max(1, Letters(expected).Length);
    }
}
