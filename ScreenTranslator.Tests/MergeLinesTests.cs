using System.Windows;

namespace ScreenTranslator.Tests;

[TestClass]
public sealed class MergeLinesTests
{
    private static OcrBlock Line(string text, double x, double y, double width, double height = 20)
    {
        var bounds = new Rect(x, y, width, height);
        return new OcrBlock(text, bounds, height, [bounds]);
    }

    [TestMethod]
    public void WrappedParagraphBecomesOneSentence()
    {
        var blocks = OcrHelper.MergeLines(
        [
            Line("今日は", 20, 20, 600),
            Line("いい天気", 20, 52, 598),
            Line("ですね。", 20, 84, 200),
        ], noSpaces: true);

        Assert.HasCount(1, blocks);
        Assert.AreEqual("今日はいい天気ですね。", blocks[0].Text);
        Assert.AreEqual(new Rect(20, 20, 600, 84), blocks[0].Bounds);
    }

    [TestMethod]
    public void WebPageLineSpacingStillMerges() // line-height 1.6 leaves gaps of ~0.75 of a line
    {
        var blocks = OcrHelper.MergeLines([Line("あ", 20, 20, 600), Line("い", 20, 55, 300)], noSpaces: true);

        Assert.HasCount(1, blocks);
    }

    [TestMethod]
    public void MenuItemsStaySeparate()
    {
        var blocks = OcrHelper.MergeLines(
        [
            Line("はじめから", 20, 20, 100),
            Line("つづきから", 20, 55, 100),
            Line("設定", 20, 90, 40),
        ], noSpaces: true);

        Assert.HasCount(3, blocks);
    }

    [TestMethod]
    public void ParagraphBreakSplitsBlocks()
    {
        var blocks = OcrHelper.MergeLines([Line("あ", 20, 20, 600), Line("い", 20, 70, 600)], noSpaces: true);

        Assert.HasCount(2, blocks);
    }

    [TestMethod]
    public void HeadingAndBodyTextStaySeparate()
    {
        var blocks = OcrHelper.MergeLines([Line("見出し", 20, 20, 600, 34), Line("本文", 20, 62, 600)], noSpaces: true);

        Assert.HasCount(2, blocks);
    }

    [TestMethod]
    public void TextWrappingUnderAPhotoContinues()
    {
        var blocks = OcrHelper.MergeLines(
        [
            Line("一行目", 380, 20, 600),
            Line("二行目", 380, 52, 600),
            Line("三行目", 20, 84, 300), // flows on underneath the photo on the left
        ], noSpaces: true);

        Assert.HasCount(1, blocks);
        Assert.AreEqual(380, blocks[0].FirstLine.X, "the label should start beside the photo, not under it");
    }

    [TestMethod]
    public void KoreanLinesAreJoinedWithASpace()
    {
        var blocks = OcrHelper.MergeLines([Line("안녕하세요", 20, 20, 600), Line("세계", 20, 52, 100)], noSpaces: false);

        Assert.AreEqual("안녕하세요 세계", blocks[0].Text);
    }

    [TestMethod]
    public void BlockFontSizeComesFromItsTallestLine()
    {
        var blocks = OcrHelper.MergeLines([Line("あ", 20, 20, 600, 24), Line("い", 20, 52, 300, 18)], noSpaces: true);

        Assert.AreEqual(24, blocks[0].LineHeight);
    }

    [TestMethod]
    public void VerticalColumnsMergeRightToLeft()
    {
        static OcrBlock Column(string text, double x, double height, double width = 24)
        {
            var bounds = new Rect(x, 30, width, height);
            return new OcrBlock(text, bounds, width, [bounds], Vertical: true);
        }

        var columns = new[] { Column("一列目", 200, 280), Column("二列目", 155, 280, 22), Column("る。", 110, 80, 17) };

        var blocks = OcrHelper.MergeLines(columns.Select(OcrHelper.Transpose).ToList(), noSpaces: true, MergeRules.Vertical)
            .Select(OcrHelper.Untranspose).ToList();

        Assert.HasCount(1, blocks);
        Assert.AreEqual("一列目二列目る。", blocks[0].Text);
        Assert.IsTrue(blocks[0].Vertical);
        Assert.AreEqual(new Rect(110, 30, 114, 280), blocks[0].Bounds);
    }
}
