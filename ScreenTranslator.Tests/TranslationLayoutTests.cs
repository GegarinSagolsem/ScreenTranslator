using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ScreenTranslator.Tests;

/// <summary>Label placement, rendered with real WPF elements (needs an STA thread).</summary>
[TestClass]
public sealed class TranslationLayoutTests
{
    private static readonly Rect Region = new(0, 0, 1000, 400);
    private static readonly DpiScale Dpi = new(1, 1);

    private static OcrBlock Line(double x, double y, double width, double height = 22)
    {
        var bounds = new Rect(x, y, width, height);
        return new OcrBlock("原文", bounds, height, [bounds]);
    }

    private static List<Border> Labels(Canvas canvas) =>
        canvas.Children.OfType<Border>().Where(b => b.Child is OutlinedText).ToList();

    private static Rect Bounds(FrameworkElement e)
    {
        e.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double width = Math.Clamp(e.DesiredSize.Width, e.MinWidth, e.MaxWidth);
        return new Rect(Canvas.GetLeft(e), Canvas.GetTop(e), width, e.DesiredSize.Height);
    }

    private static void AssertNoOverlaps(IReadOnlyList<Border> labels)
    {
        var rects = labels.Select(Bounds).ToList();
        for (int i = 0; i < rects.Count; i++)
            for (int j = i + 1; j < rects.Count; j++)
            {
                var overlap = Rect.Intersect(rects[i], rects[j]);
                Assert.IsTrue(overlap.IsEmpty || overlap.Width < 1 || overlap.Height < 1, $"labels {i} and {j} overlap");
            }
    }

    [STATestMethod]
    public void LongTranslationsOfTightLinesNeverOverlap()
    {
        var blocks = new[] { Line(20, 20, 300), Line(20, 52, 300), Line(20, 84, 300), Line(400, 52, 200) };
        var translations = blocks.Select(_ => "A much longer English translation that needs several lines to fit in").ToArray();
        var canvas = new Canvas();

        TranslationLayout.Draw(canvas, blocks, translations, Region, Dpi, Brushes.Black);

        Assert.HasCount(4, Labels(canvas));
        AssertNoOverlaps(Labels(canvas));
    }

    [STATestMethod]
    public void LabelsCoverTheirSourceText()
    {
        var block = Line(20, 20, 600, 60);
        var canvas = new Canvas();

        TranslationLayout.Draw(canvas, [block], ["Short"], Region, Dpi, Brushes.Black);

        var label = Bounds(Labels(canvas).Single());
        Assert.IsTrue(label.Width >= block.Bounds.Width - 1 && label.Height >= block.Bounds.Height, $"label {label} doesn't cover {block.Bounds}");
    }

    [STATestMethod]
    public void BiggerTextSizeGivesBiggerText()
    {
        double FontAt(double scale)
        {
            var canvas = new Canvas();
            TranslationLayout.Draw(canvas, [Line(20, 20, 600, 30)], ["Hi"], Region, Dpi, Brushes.Black, scale);
            return ((OutlinedText)Labels(canvas).Single().Child).FontSize;
        }

        Assert.IsGreaterThan(FontAt(1.0), FontAt(1.5));
        Assert.IsLessThan(FontAt(1.0), FontAt(0.6));
    }

    [STATestMethod]
    public void BlankTranslationsDrawNothing()
    {
        var canvas = new Canvas();

        TranslationLayout.Draw(canvas, [Line(20, 20, 300)], [" "], Region, Dpi, Brushes.Black);

        Assert.IsEmpty(canvas.Children);
    }

    private static OcrBlock InBubble(Rect text, Rect bubble) => new("吹き出し", text, 12, [text], Vertical: true, Bubble: bubble);

    [STATestMethod]
    public void BubbleLabelsStayInsideTheirBubblesAndCoverTheText()
    {
        var blocks = new[]
        {
            InBubble(new Rect(130, 40, 40, 90), new Rect(110, 20, 90, 130)),
            InBubble(new Rect(230, 40, 40, 90), new Rect(210, 20, 90, 130)), // the bubble right beside it
        };
        var canvas = new Canvas();

        TranslationLayout.Draw(canvas, blocks, ["Why are the brothers tying Sanji up?", "Sanji was taken away"], Region, Dpi, Brushes.Black);

        var labels = Labels(canvas);
        AssertNoOverlaps(labels);
        for (int i = 0; i < blocks.Length; i++)
        {
            var label = Bounds(labels[i]);
            Assert.IsTrue(blocks[i].Bubble!.Value.Contains(label), $"label {label} leaves its bubble {blocks[i].Bubble}");
            Assert.IsTrue(label.Contains(blocks[i].Bounds), $"label {label} doesn't cover the text {blocks[i].Bounds}");
        }
    }

    [STATestMethod]
    public void ALongWordWidensASmallBubblesLabelInsteadOfBreaking()
    {
        var canvas = new Canvas();

        TranslationLayout.Draw(canvas, [InBubble(new Rect(510, 40, 14, 50), new Rect(500, 30, 34, 70))], ["quadruplets"],
            Region, Dpi, Brushes.Black);

        var text = (OutlinedText)Labels(canvas).Single().Child;
        Assert.IsLessThan(text.FontSize * 2, text.DesiredSize.Height, "the word was broken over two lines");
    }

    [STATestMethod]
    public void VerticalColumnsGetALabelWideEnoughForEnglish()
    {
        var column = new Rect(500, 20, 26, 300);
        var block = new OcrBlock("縦書き", column, 26, [column], Vertical: true);
        var canvas = new Canvas();

        TranslationLayout.Draw(canvas, [block], ["A vertical sentence"], Region, Dpi, Brushes.Black);

        var label = Bounds(Labels(canvas).Single());
        Assert.IsGreaterThanOrEqualTo(6 * 26 - 1, label.Width);
        Assert.IsTrue(label.Left <= column.Left && label.Right >= column.Right, "label should be centred over the column");
    }
}
