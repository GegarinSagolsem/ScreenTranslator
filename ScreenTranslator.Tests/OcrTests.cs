using System.Drawing;
using System.Drawing.Text;
using Windows.Media.Ocr;

namespace ScreenTranslator.Tests;

/// <summary>
/// Real Windows OCR on rendered text. Skipped (inconclusive) on machines without the OCR language pack,
/// such as GitHub's build servers.
/// </summary>
[TestClass]
public sealed class OcrTests
{
    private static OcrHelper ReaderFor(SourceLanguage language)
    {
        var prefix = language.OcrTag;
        if (!OcrEngine.AvailableRecognizerLanguages.Any(l => l.LanguageTag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            Assert.Inconclusive($"The Windows OCR pack for {language.Name} isn't installed here.");

        var ocr = new OcrHelper();
        ocr.SetLanguage(language);
        return ocr;
    }

    private static Bitmap Horizontal(string[] lines, string font, int size = 30)
    {
        int pitch = (int)(size * 1.6); // typical line spacing
        var bmp = new Bitmap(60 + lines.Max(l => l.Length) * (size + 4), 40 + lines.Length * pitch);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.White);
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        using var f = new Font(font, size, GraphicsUnit.Pixel);
        for (int i = 0; i < lines.Length; i++) g.DrawString(lines[i], f, Brushes.Black, 20, 20 + i * pitch);
        return bmp;
    }

    /// <summary>Columns read top to bottom, right to left, with upright glyphs.</summary>
    internal static Bitmap Vertical(string text, string font, int perColumn, int size = 28)
    {
        var columns = Enumerable.Range(0, (text.Length + perColumn - 1) / perColumn)
            .Select(i => text.Substring(i * perColumn, Math.Min(perColumn, text.Length - i * perColumn))).ToList();
        int pitchX = (int)(size * 1.8), pitchY = (int)(size * 1.15);
        var bmp = new Bitmap(80 + columns.Count * pitchX, 70 + perColumn * pitchY);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.White);
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using var f = new Font(font, size, GraphicsUnit.Pixel);
        for (int c = 0; c < columns.Count; c++)
            for (int i = 0; i < columns[c].Length; i++)
            {
                var ch = columns[c][i].ToString();
                var width = g.MeasureString(ch, f).Width;
                g.DrawString(ch, f, Brushes.Black, bmp.Width - 50 - size - c * pitchX + (size - width) / 2 + 4, 34 + i * pitchY);
            }
        return bmp;
    }

    private static string Squash(string s) => new(s.Where(c => !char.IsWhiteSpace(c) && !char.IsPunctuation(c)).ToArray());

    [TestMethod]
    public async Task JapaneseIsReadWithoutSpacesBetweenCharacters()
    {
        var ocr = ReaderFor(Languages.All[0]);
        using var image = Horizontal(["今日はいい天気ですね。"], "Yu Gothic UI");

        var blocks = await ocr.RecognizeAsync(image, mergeLines: true);

        Assert.AreEqual("今日はいい天気ですね", Squash(string.Concat(blocks.Select(b => b.Text))));
        Assert.DoesNotContain(" ", blocks[0].Text);
    }

    [TestMethod]
    [DataRow(1, "Microsoft YaHei UI", "我们今天去公园散步吧，天气很好。", 12)]
    [DataRow(0, "Yu Gothic UI", "関門トンネルは本州と九州を結ぶ海底トンネルである。", 14)]
    public async Task SmallTextIsReadAccurately(int language, string font, string text, int size)
    {
        var ocr = ReaderFor(Languages.All[language]);
        using var image = Horizontal([text], font, size);

        var blocks = await ocr.RecognizeAsync(image, mergeLines: true);

        Assert.AreEqual(Squash(text), Squash(string.Concat(blocks.Select(b => b.Text))));
    }

    [TestMethod]
    public async Task WrappedJapaneseParagraphBecomesOneBlock()
    {
        var ocr = ReaderFor(Languages.All[0]);
        using var image = Horizontal(["俺は海賊王になる男だ！どんなに強い敵が相手でも、", "仲間と一緒なら絶対に負けない。"], "Yu Gothic UI");

        var blocks = await ocr.RecognizeAsync(image, mergeLines: true);

        Assert.HasCount(1, blocks);
    }

    [TestMethod]
    public async Task VerticalJapaneseIsReadColumnByColumn()
    {
        var ocr = ReaderFor(Languages.All[0]);
        const string text = "吾輩は猫である。名前はまだ無い。どこで生れたかとんと見当がつかぬ。";
        using var image = Vertical(text, "Yu Gothic UI", 10);

        var blocks = await ocr.RecognizeAsync(image, mergeLines: true, verticalText: true);

        Assert.HasCount(1, blocks);
        Assert.IsTrue(blocks[0].Vertical);
        Assert.AreEqual(Squash(text), Squash(blocks[0].Text));
    }

    [TestMethod]
    public async Task VerticalChineseIsReadColumnByColumn()
    {
        var ocr = ReaderFor(Languages.All[1]);
        const string text = "从前有一座山，山里有一座庙，庙里有一个老和尚在给小和尚讲故事。";
        using var image = Vertical(text, "Microsoft YaHei UI", 12);

        var blocks = await ocr.RecognizeAsync(image, mergeLines: true, verticalText: true);

        Assert.HasCount(1, blocks);
        Assert.AreEqual(Squash(text), Squash(blocks[0].Text));
    }
}
