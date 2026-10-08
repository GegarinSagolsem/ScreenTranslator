using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ScreenTranslator
{
    /// <summary>
    /// Places translation labels over their source text. English usually needs more room than
    /// Japanese, so each label may only grow down to the next block of text and shrinks its font
    /// until it fits there, instead of spilling over the label below.
    /// </summary>
    public static class TranslationLayout
    {
        public const double MinTextScale = 0.5;
        public const double MaxTextScale = 2.0;

        private const double MinFontSize = 10;
        private const double MaxFontSize = 28;
        private const double SmallestFontSize = 7; // floor even at the smallest text size setting

        /// <param name="textScale">The user's text size setting, multiplying the automatic font size.</param>
        public static void Draw(Canvas canvas, IReadOnlyList<OcrBlock> blocks, IReadOnlyList<string?> translations,
                                Rect region, DpiScale dpi, Brush background, double textScale = 1.0)
        {
            canvas.Children.Clear();

            for (int i = 0; i < blocks.Count; i++)
            {
                var text = translations[i];
                if (string.IsNullOrWhiteSpace(text)) continue;

                double maxBottom = RoomBelow(blocks, i, region, dpi);
                var label = CreateLabel(text, blocks[i], maxBottom, region, dpi, background, textScale);
                canvas.Children.Add(label);
                CoverLinesLeftOf(canvas, blocks[i], Canvas.GetLeft(label), region, dpi, background);
            }
        }

        /// <summary>
        /// A paragraph that wraps under a photo has lines starting left of its label (which sits beside
        /// the photo). Cover those parts with the same background, so no stray original text shows.
        /// </summary>
        private static void CoverLinesLeftOf(Canvas canvas, OcrBlock block, double labelLeft, Rect region, DpiScale dpi, Brush background)
        {
            foreach (var line in block.Lines)
            {
                double left = region.X + line.Left / dpi.DpiScaleX;
                double right = Math.Min(region.X + line.Right / dpi.DpiScaleX, labelLeft);
                if (right - left < 4) continue; // slivers are already under the label's padding

                var cover = new Border
                {
                    Background = background,
                    CornerRadius = new CornerRadius(2),
                    Width = right - left,
                    Height = line.Height / dpi.DpiScaleY + 2
                };
                Canvas.SetLeft(cover, left);
                Canvas.SetTop(cover, region.Y + line.Top / dpi.DpiScaleY - 1);
                canvas.Children.Add(cover);
            }
        }

        /// <summary>The lowest point (in DIPs) a label may reach: the top of the next block beneath it, or the region's bottom.</summary>
        private static double RoomBelow(IReadOnlyList<OcrBlock> blocks, int index, Rect region, DpiScale dpi)
        {
            var block = blocks[index];
            double left = block.FirstLine.Left;
            double bottom = region.Height * dpi.DpiScaleY;

            foreach (var other in blocks)
            {
                // Labels can grow to the region's right edge, so anything below that isn't entirely to the left is in the way
                if (other.Bounds.Top > block.Bounds.Top + block.LineHeight / 2 && other.Bounds.Right > left)
                    bottom = Math.Min(bottom, other.Bounds.Top);
            }

            return region.Y + bottom / dpi.DpiScaleY;
        }

        private static Border CreateLabel(string text, OcrBlock block, double maxBottom, Rect region, DpiScale dpi,
                                          Brush background, double textScale)
        {
            double left = region.X + block.FirstLine.Left / dpi.DpiScaleX;
            double top = region.Y + block.Bounds.Top / dpi.DpiScaleY;
            double maxWidth = Math.Max(region.Right - left, 80);
            double maxHeight = maxBottom - top - 1;

            var content = new OutlinedText(text);
            var label = new Border
            {
                Background = background,
                Padding = new Thickness(2, 0, 2, 0),
                CornerRadius = new CornerRadius(2),
                // At least as big as the original text, so the translation covers all of it
                MinWidth = Math.Min((block.Bounds.Right - block.FirstLine.Left) / dpi.DpiScaleX, maxWidth),
                MaxWidth = maxWidth,
                MinHeight = Math.Max(0, Math.Min(block.Bounds.Height / dpi.DpiScaleY + 2, maxHeight)),
                Child = content
            };

            // Start near the source text's size (scaled by the user's text size), then shrink until the
            // label fits above the next block, but never below the chosen size's floor
            double minFontSize = Math.Max(SmallestFontSize, MinFontSize * textScale);
            double fontSize = Math.Clamp(block.LineHeight / dpi.DpiScaleY * 0.8, MinFontSize, MaxFontSize) * textScale;
            while (true)
            {
                content.FontSize = fontSize;
                label.InvalidateMeasure(); // Border would otherwise reuse its last measurement
                label.Measure(new Size(maxWidth, double.PositiveInfinity));
                if (label.DesiredSize.Height <= maxHeight || fontSize <= minFontSize) break;
                fontSize = Math.Max(minFontSize, fontSize - 1);
            }

            Canvas.SetLeft(label, left);
            Canvas.SetTop(label, top);
            return label;
        }
    }
}
