using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace ScreenTranslator
{
    /// <summary>
    /// White text with a crisp black outline, like video subtitles, so it stays readable on any
    /// background, including when the box behind it is faded or fully see-through.
    /// </summary>
    public sealed class OutlinedText : FrameworkElement
    {
        private static readonly Typeface Face = new(
            new FontFamily("Segoe UI, Yu Gothic UI, Malgun Gothic, Microsoft YaHei UI"),
            FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

        private double _fontSize = 14;
        private FormattedText? _formatted;

        public OutlinedText(string text) => Text = text;

        public string Text { get; }

        /// <summary>Centred text fills the width it's given, so its lines centre within it.</summary>
        public TextAlignment Alignment { get; init; } = TextAlignment.Left;

        public double FontSize
        {
            get => _fontSize;
            set
            {
                _fontSize = value;
                InvalidateMeasure();
                InvalidateVisual();
            }
        }

        // Room around the glyphs for the half of the outline that sits outside them
        private double Pad => OutlineThickness / 2;

        private double OutlineThickness => Math.Max(2.5, _fontSize / 6);

        /// <summary>The width the longest word needs, so a label can be made wide enough not to break it.</summary>
        public double LongestWordWidth(double pixelsPerDip)
        {
            return Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Select(word => new FormattedText(word, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Face,
                    _fontSize, Brushes.White, pixelsPerDip).WidthIncludingTrailingWhitespace)
                .DefaultIfEmpty(0)
                .Max() + 2 * Pad;
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            _formatted = new FormattedText(Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Face,
                _fontSize, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            bool bounded = !double.IsInfinity(availableSize.Width);
            if (bounded)
                _formatted.MaxTextWidth = Math.Max(1, availableSize.Width - 2 * Pad);
            _formatted.TextAlignment = Alignment;
            _formatted.Trimming = TextTrimming.None; // a word wider than the label wraps instead of ending in "…"

            double width = Alignment != TextAlignment.Left && bounded
                ? availableSize.Width
                : _formatted.WidthIncludingTrailingWhitespace + 2 * Pad;
            return new Size(width, _formatted.Height + 2 * Pad);
        }

        protected override void OnRender(DrawingContext dc)
        {
            if (_formatted == null) return;

            var geometry = _formatted.BuildGeometry(new Point(Pad, Pad));
            // Outline first, then the fill on top, so only the outside half of the outline shows
            dc.DrawGeometry(null, new Pen(Brushes.Black, OutlineThickness) { LineJoin = PenLineJoin.Round }, geometry);
            dc.DrawGeometry(Brushes.White, null, geometry);
        }
    }
}
