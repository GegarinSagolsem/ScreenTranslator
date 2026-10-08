using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ScreenTranslator.Tests;

/// <summary>Subtitle mode: every translation in one panel next to the region.</summary>
[TestClass]
public sealed class SubtitleTests
{
    private static readonly Rect Screen = new(0, 0, 1280, 720);
    private static readonly DpiScale Dpi = new(1, 1);

    private static OcrBlock Line(double y)
    {
        var bounds = new Rect(100, y, 400, 24);
        return new OcrBlock("原文", bounds, 24, [bounds]);
    }

    private static (Border Panel, Rect Bounds) Subtitles(Rect region, params string?[] translations)
    {
        var canvas = new Canvas();
        TranslationLayout.DrawSubtitles(canvas, translations.Select((_, i) => Line(region.Y + i * 30)).ToList(),
            translations, region, Screen, Dpi, Brushes.Black);

        var panel = canvas.Children.OfType<Border>().Single();
        panel.Measure(new Size(panel.Width, double.PositiveInfinity));
        return (panel, new Rect(Canvas.GetLeft(panel), Canvas.GetTop(panel), panel.Width, panel.DesiredSize.Height));
    }

    [STATestMethod]
    public void AllTranslationsGoInOnePanelBelowTheRegion()
    {
        var region = new Rect(100, 100, 600, 120);

        var (panel, bounds) = Subtitles(region, "First line.", null, "Second line.");

        Assert.AreEqual("First line.\nSecond line.", ((OutlinedText)panel.Child).Text);
        Assert.IsGreaterThanOrEqualTo(region.Bottom, bounds.Top);
        Assert.IsTrue(bounds.Left <= region.Left && bounds.Right >= region.Right, "panel should span the region");
    }

    [STATestMethod]
    public void PanelMovesAboveWhenTheRegionIsAtTheBottom()
    {
        var region = new Rect(100, 600, 600, 110);

        var (_, bounds) = Subtitles(region, "A translation");

        Assert.IsLessThanOrEqualTo(region.Top, bounds.Bottom);
        Assert.IsGreaterThanOrEqualTo(0, bounds.Top);
    }

    [STATestMethod]
    public void PanelStaysOnScreen()
    {
        var region = new Rect(1100, 0, 170, 715); // tall and narrow at the right edge: no room above or below

        var (_, bounds) = Subtitles(region, "A translation that is long enough to need a couple of lines");

        Assert.IsTrue(Screen.Contains(bounds.TopLeft) && bounds.Right <= Screen.Right + 0.5, $"panel {bounds} is off screen");
    }

    [STATestMethod]
    public void NothingToShowDrawsNothing()
    {
        var canvas = new Canvas();

        TranslationLayout.DrawSubtitles(canvas, [Line(10)], [" "], new Rect(0, 0, 500, 100), Screen, Dpi, Brushes.Black);

        Assert.IsEmpty(canvas.Children);
    }
}
