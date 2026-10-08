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

            var placements = blocks.Select((_, i) => Place(blocks, i, region, dpi)).ToList();
            for (int i = 0; i < blocks.Count; i++)
            {
                var text = translations[i];
                if (string.IsNullOrWhiteSpace(text)) continue;

                var placement = placements[i];
                double maxBottom = RoomBelow(blocks, placements, i, region, dpi);
                var label = CreateLabel(text, blocks[i], placement, maxBottom, dpi, background, textScale);
                canvas.Children.Add(label);
                if (!blocks[i].Vertical)
                    CoverLinesLeftOf(canvas, blocks[i], placement.Left, region, dpi, background);
            }
        }

        /// <summary>
        /// Subtitle mode: every translation in one centred panel below the region (above it, or inside its
        /// bottom edge, when there's no room), so the original text stays fully visible.
        /// </summary>
        /// <param name="screen">The area the panel must stay inside, in DIPs (the overlay window).</param>
        public static void DrawSubtitles(Canvas canvas, IReadOnlyList<OcrBlock> blocks, IReadOnlyList<string?> translations,
                                         Rect region, Rect screen, DpiScale dpi, Brush background, double textScale = 1.0)
        {
            canvas.Children.Clear();
            var text = string.Join("\n", translations.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t!.Trim()));
            if (text.Length == 0) return;

            double lineHeight = blocks.Count == 0 ? 20 : blocks.Select(b => b.LineHeight).Order().ElementAt(blocks.Count / 2) / dpi.DpiScaleY;
            double width = Math.Min(Math.Max(region.Width, 360), screen.Width);
            double left = Math.Clamp(region.X + (region.Width - width) / 2, screen.Left, screen.Right - width);

            var panel = new Border
            {
                Background = background,
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 6, 12, 8),
                Width = width,
                Child = new OutlinedText(text)
                {
                    FontSize = Math.Clamp(lineHeight * 0.8, 14, MaxFontSize) * textScale,
                    Alignment = TextAlignment.Center
                }
            };
            panel.Measure(new Size(width, double.PositiveInfinity));
            double height = panel.DesiredSize.Height;

            const double gap = 6;
            double top = region.Bottom + gap + height <= screen.Bottom ? region.Bottom + gap
                : region.Top - gap - height >= screen.Top ? region.Top - gap - height
                : Math.Max(screen.Top, region.Bottom - gap - height);

            Canvas.SetLeft(panel, left);
            Canvas.SetTop(panel, top);
            canvas.Children.Add(panel);
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

        /// <summary>Where a label goes, in DIPs. Labels keep this position and width range, and grow downwards.</summary>
        private readonly record struct Placement(double Left, double Top, double MinWidth, double MaxWidth);

        private static Placement Place(IReadOnlyList<OcrBlock> blocks, int index, Rect region, DpiScale dpi)
        {
            var block = blocks[index];
            double top = region.Y + block.Bounds.Top / dpi.DpiScaleY;

            if (!block.Vertical)
            {
                double left = region.X + block.FirstLine.Left / dpi.DpiScaleX;

                // Grow rightwards to fit the translation, but stop before a block beside this one (two columns, UI)
                double rightEdge = region.Right;
                foreach (var other in blocks)
                {
                    bool sameRow = other.Bounds.Top < block.Bounds.Bottom && other.Bounds.Bottom > block.Bounds.Top;
                    double otherLeft = region.X + other.Bounds.Left / dpi.DpiScaleX;
                    if (other != block && sameRow && otherLeft > left)
                        rightEdge = Math.Min(rightEdge, otherLeft - 4);
                }

                double maxWidth = Math.Max(rightEdge - left, 80);
                double minWidth = Math.Min((block.Bounds.Right - block.FirstLine.Left) / dpi.DpiScaleX, maxWidth);
                return new Placement(left, top, minWidth, maxWidth);
            }

            // Vertical text: a horizontal label centred over the columns, widened when they're too narrow for English
            double charSize = block.LineHeight / dpi.DpiScaleX;
            double width = Math.Min(Math.Max(block.Bounds.Width / dpi.DpiScaleX, 6 * charSize), region.Width);
            double centre = region.X + (block.Bounds.Left + block.Bounds.Width / 2) / dpi.DpiScaleX;
            double labelLeft = Math.Clamp(centre - width / 2, region.X, region.Right - width);
            return new Placement(labelLeft, top, width, width);
        }

        /// <summary>
        /// The lowest point (in DIPs) a label may reach: the top of the next block whose label could share
        /// its horizontal space, or the region's bottom. Compares where labels can extend, not where the
        /// source text sits: a short line's label grows rightwards to fit its translation.
        /// </summary>
        private static double RoomBelow(IReadOnlyList<OcrBlock> blocks, IReadOnlyList<Placement> placements, int index,
                                        Rect region, DpiScale dpi)
        {
            var me = placements[index];
            double halfLine = blocks[index].LineHeight / dpi.DpiScaleY / 2;
            double bottom = region.Bottom;

            foreach (var other in placements)
            {
                bool below = other.Top > me.Top + halfLine;
                bool sideBySide = other.Left >= me.Left + me.MaxWidth || other.Left + other.MaxWidth <= me.Left;
                if (below && !sideBySide)
                    bottom = Math.Min(bottom, other.Top);
            }

            return bottom;
        }

        private static Border CreateLabel(string text, OcrBlock block, Placement placement, double maxBottom, DpiScale dpi,
                                          Brush background, double textScale)
        {
            double maxWidth = placement.MaxWidth;
            double maxHeight = maxBottom - placement.Top - 1;

            var content = new OutlinedText(text);
            var label = new Border
            {
                Background = background,
                Padding = new Thickness(2, 0, 2, 0),
                CornerRadius = new CornerRadius(2),
                // At least as big as the original text, so the translation covers all of it
                MinWidth = placement.MinWidth,
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

            Canvas.SetLeft(label, placement.Left);
            Canvas.SetTop(label, placement.Top);
            return label;
        }
    }
}
