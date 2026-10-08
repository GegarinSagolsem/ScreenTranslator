using System.Drawing;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace ScreenTranslator
{
    /// <summary>One line, or several wrapped lines merged into one sentence block.</summary>
    /// <param name="Bounds">Box in pixels of the captured bitmap, covering every line.</param>
    /// <param name="LineHeight">Height of a single line in pixels, used to size the overlay font.</param>
    /// <param name="Lines">Each line's box, in reading order.</param>
    /// <param name="Vertical">Text runs in columns (top to bottom, right to left); LineHeight is then the column width.</param>
    public record OcrBlock(string Text, System.Windows.Rect Bounds, double LineHeight, IReadOnlyList<System.Windows.Rect> Lines,
                           bool Vertical = false)
    {
        /// <summary>The label starts here, not at the block's left edge, which can sit under a photo the text wraps around.</summary>
        public System.Windows.Rect FirstLine => Lines[0];
    }

    /// <summary>How readily <see cref="OcrHelper.MergeLines"/> joins neighbouring lines into one block.</summary>
    /// <param name="MinWrappedChars">A line shorter than this many characters is never treated as wrapped.</param>
    /// <param name="MaxGap">Largest gap between lines, as a share of the line height.</param>
    /// <param name="MaxSizeRatio">Largest difference in line height still counted as the same font size.</param>
    public sealed record MergeRules(int MinWrappedChars, double MaxGap, double MaxSizeRatio)
    {
        public static readonly MergeRules Horizontal = new(MinWrappedChars: 6, MaxGap: 1.0, MaxSizeRatio: 1.4);

        // Vertical books space columns widely, columns are often short, and a column of kana and
        // punctuation is visibly narrower than one with kanji
        public static readonly MergeRules Vertical = new(MinWrappedChars: 3, MaxGap: 1.5, MaxSizeRatio: 1.7);
    }

    public class OcrHelper
    {
        private OcrEngine? _engine;

        public SourceLanguage CurrentLanguage { get; private set; } = Languages.All[0];

        /// <summary>Returns false when the Windows OCR pack for the language is not installed.</summary>
        public bool SetLanguage(SourceLanguage language)
        {
            CurrentLanguage = language;

            // Installed tags carry a region (e.g. "zh-Hans-CN"), so match by prefix
            var installed = OcrEngine.AvailableRecognizerLanguages.FirstOrDefault(l =>
                l.LanguageTag.Equals(language.OcrTag, StringComparison.OrdinalIgnoreCase) ||
                l.LanguageTag.StartsWith(language.OcrTag + "-", StringComparison.OrdinalIgnoreCase));

            _engine = installed != null ? OcrEngine.TryCreateFromLanguage(installed) : null;
            Log.Info($"OCR language '{language.OcrTag}' -> {installed?.LanguageTag ?? "not installed"}");
            return _engine != null;
        }

        /// <param name="verticalText">
        /// Read the region as vertical columns via <see cref="VerticalText"/>. Needed for Chinese; Japanese
        /// vertical text is recognised natively either way.
        /// </param>
        public async Task<IReadOnlyList<OcrBlock>> RecognizeAsync(Bitmap bitmap, bool mergeLines, bool verticalText = false)
        {
            var engine = _engine;
            var language = CurrentLanguage;
            if (engine == null) return [];

            var lines = verticalText
                ? await RecognizeColumnsAsync(engine, bitmap, language)
                : await RecognizeLinesAsync(engine, bitmap, language);
            if (!mergeLines) return lines;

            // Columns merge with the same rules as lines, turned 90°
            var horizontal = MergeLines(lines.Where(l => !l.Vertical).ToList(), language.NoSpaces, MergeRules.Horizontal);
            var vertical = MergeLines(lines.Where(l => l.Vertical).Select(Transpose).ToList(), language.NoSpaces, MergeRules.Vertical)
                .Select(Untranspose);
            return [.. horizontal, .. vertical];
        }

        // Windows OCR misreads text under ~16 px. When a first pass finds only small text (or none),
        // an enlarged copy is read too and usually wins.
        private const double SmallTextHeight = 20;
        private const double ComfortableTextHeight = 32;
        private const double MaxEnlargedPixels = 16_000_000; // bounds the extra memory and OCR time

        private static async Task<List<OcrBlock>> RecognizeLinesAsync(OcrEngine engine, Bitmap bitmap, SourceLanguage language)
        {
            var lines = await RecognizeAtScaleAsync(engine, bitmap, language, FitScale(bitmap, 1.0));

            double typicalHeight = lines.Count == 0 ? 0 : lines.Select(l => l.LineHeight).Order().ElementAt(lines.Count / 2);
            if (lines.Count > 0 && typicalHeight >= SmallTextHeight) return lines;

            double wanted = lines.Count == 0 ? 3 : Math.Clamp(ComfortableTextHeight / typicalHeight, 1.5, 4);
            double scale = FitScale(bitmap, wanted);
            if (scale < 1.25) return lines;

            // The enlarged read is the more accurate one; misread tiny text can even come out longer
            // (garbage characters), so only fall back if enlarging lost a good part of the text
            var enlarged = await RecognizeAtScaleAsync(engine, bitmap, language, scale);
            return CharacterCount(enlarged) * 10 >= CharacterCount(lines) * 7 ? enlarged : lines;
        }

        /// <summary>The wanted scale, limited by the engine's maximum image size and a pixel budget.</summary>
        private static double FitScale(Bitmap bitmap, double wanted) => Math.Min(wanted, Math.Min(
            (double)OcrEngine.MaxImageDimension / Math.Max(bitmap.Width, bitmap.Height),
            Math.Sqrt(MaxEnlargedPixels / ((double)bitmap.Width * bitmap.Height))));

        private static int CharacterCount(IEnumerable<OcrBlock> lines) => lines.Sum(l => l.Text.Count(c => !char.IsWhiteSpace(c)));

        private static async Task<List<OcrBlock>> RecognizeAtScaleAsync(OcrEngine engine, Bitmap bitmap, SourceLanguage language, double scale)
        {
            using var scaled = Math.Abs(scale - 1.0) < 0.01 ? null : Resize(bitmap, scale);
            var source = scaled ?? bitmap;

            var pixels = ScreenCapture.BitmapToBytes(source);
            using var softwareBitmap = SoftwareBitmap.CreateCopyFromBuffer(
                pixels.AsBuffer(), BitmapPixelFormat.Bgra8, source.Width, source.Height);

            var result = await engine.RecognizeAsync(softwareBitmap);

            var lines = new List<OcrBlock>();
            foreach (var line in result.Lines)
            {
                if (line.Words.Count == 0) continue;

                double left = line.Words.Min(w => w.BoundingRect.X);
                double top = line.Words.Min(w => w.BoundingRect.Y);
                double right = line.Words.Max(w => w.BoundingRect.X + w.BoundingRect.Width);
                double bottom = line.Words.Max(w => w.BoundingRect.Y + w.BoundingRect.Height);

                var text = JoinWords(line.Words.Select(w => w.Text), language.NoSpaces);
                var bounds = new System.Windows.Rect(left / scale, top / scale, (right - left) / scale, (bottom - top) / scale);

                // Japanese OCR reads vertical columns natively and returns each as a tall, narrow line
                bool vertical = line.Words.Count >= 2 && bounds.Height > bounds.Width * 2;
                lines.Add(new OcrBlock(text, bounds, vertical ? bounds.Width : bounds.Height, [bounds], vertical));
            }

            return lines;
        }

        private static Bitmap Resize(Bitmap source, double scale)
        {
            var resized = new Bitmap(Math.Max(1, (int)(source.Width * scale)), Math.Max(1, (int)(source.Height * scale)),
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(resized);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic; // smooth edges read best
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            g.DrawImage(source, 0, 0, resized.Width, resized.Height);
            return resized;
        }

        private static async Task<List<OcrBlock>> RecognizeColumnsAsync(OcrEngine engine, Bitmap bitmap, SourceLanguage language)
        {
            using var layout = VerticalText.Rearrange(bitmap);
            if (layout == null) return [];

            var rowLines = await RecognizeLinesAsync(engine, layout.Image, language);

            var columns = new List<OcrBlock>();
            foreach (var row in layout.Rows)
            {
                // Each column became one horizontal line; OCR may still split it into pieces
                var pieces = rowLines
                    .Where(l => Math.Abs(l.Bounds.Top + l.Bounds.Height / 2 - row.RowCenterY) < row.RowHeight * 0.75)
                    .OrderBy(l => l.Bounds.Left)
                    .Select(l => l.Text);
                var text = string.Join(language.NoSpaces ? "" : " ", pieces);
                if (text.Length == 0) continue;

                // A line can hold a short column appended to the one before it; hand each column its share
                // of the characters (exactness doesn't matter once merging joins them back in order)
                var shares = new string[row.Sources.Count];
                int end = text.Length;
                for (int i = row.Sources.Count - 1; i > 0; i--)
                {
                    int take = Math.Min(row.CellCounts[i], Math.Max(0, end - 1));
                    shares[i] = text[(end - take)..end];
                    end -= take;
                }
                shares[0] = text[..end];

                for (int i = 0; i < row.Sources.Count; i++)
                {
                    if (shares[i].Length == 0) continue;
                    var c = row.Sources[i];
                    var source = new System.Windows.Rect(c.X, c.Y, c.Width, c.Height);
                    columns.Add(new OcrBlock(shares[i], source, source.Width, [source], Vertical: true));
                }
            }

            return columns;
        }

        // A column turned 90° clockwise reads like a line: its top becomes the line's left edge and the
        // next column to the left becomes the next line down. Lets columns reuse MergeLines.
        private static System.Windows.Rect Turn(System.Windows.Rect r) => new(r.Top, -r.Right, r.Height, r.Width);

        private static System.Windows.Rect TurnBack(System.Windows.Rect r) => new(-r.Y - r.Height, r.X, r.Height, r.Width);

        internal static OcrBlock Transpose(OcrBlock b) => b with { Bounds = Turn(b.Bounds), Lines = b.Lines.Select(Turn).ToList() };

        internal static OcrBlock Untranspose(OcrBlock b) =>
            b with { Bounds = TurnBack(b.Bounds), Lines = b.Lines.Select(TurnBack).ToList(), Vertical = true };

        /// <summary>
        /// Joins lines that wrap onto the next one, so DeepL sees whole sentences instead of fragments.
        /// Line B continues line A when they share a font size, B sits right under A, they line up
        /// (left, centred, or B wraps under a photo to the left), and A is long and runs to the right
        /// edge of its column (it wrapped). That last rule keeps menus and lists, whose items end short,
        /// as separate entries.
        /// </summary>
        public static IReadOnlyList<OcrBlock> MergeLines(IReadOnlyList<OcrBlock> lines, bool noSpaces, MergeRules? rules = null)
        {
            var blocks = new List<(StringBuilder Text, System.Windows.Rect Bounds, List<System.Windows.Rect> Lines, OcrBlock Last)>();

            foreach (var line in lines.OrderBy(l => l.Bounds.Top))
            {
                int target = -1;
                for (int i = blocks.Count - 1; i >= 0 && target < 0; i--)
                {
                    if (Continues(blocks[i].Last, line, lines, rules ?? MergeRules.Horizontal))
                        target = i;
                }

                if (target < 0)
                {
                    blocks.Add((new StringBuilder(line.Text), line.Bounds, [line.Bounds], line));
                    continue;
                }

                var (text, bounds, members, _) = blocks[target];
                if (!noSpaces || (text.Length > 0 && line.Text.Length > 0 && IsLatin(text[^1]) && IsLatin(line.Text[0])))
                    text.Append(' ');
                text.Append(line.Text);
                bounds.Union(line.Bounds);
                members.Add(line.Bounds);
                blocks[target] = (text, bounds, members, line);
            }

            return blocks
                // The tallest line best reflects the font size: a last line of kana or punctuation runs small
                .Select(b => new OcrBlock(b.Text.ToString(), b.Bounds, b.Lines.Max(l => l.Height), b.Lines))
                .ToList();
        }

        private static bool Continues(OcrBlock above, OcrBlock below, IReadOnlyList<OcrBlock> all, MergeRules rules)
        {
            var a = above.Bounds;
            var b = below.Bounds;
            double h = Math.Max(a.Height, b.Height);

            double ratio = a.Height / Math.Max(b.Height, 1);
            if (ratio < 1 / rules.MaxSizeRatio || ratio > rules.MaxSizeRatio) return false; // different font size

            // Web pages use ~1.6 line spacing, which leaves a gap of up to ~0.8 of a line
            double gap = b.Top - a.Bottom;
            if (gap < -0.3 * h || gap > rules.MaxGap * h) return false; // not the very next line

            bool leftAligned = Math.Abs(b.Left - a.Left) < 1.5 * h;
            bool centred = Math.Abs((b.Left + b.Right) / 2 - (a.Left + a.Right) / 2) < 1.5 * h;
            bool wrapsUnderPhoto = b.Left < a.Left - 1.5 * h; // text flowing on underneath a floated image
            if (!leftAligned && !centred && !wrapsUnderPhoto) return false;

            // A wrapped line is long and reaches the right edge of its column; list and menu items end short
            if (a.Width < rules.MinWrappedChars * h) return false;
            double columnRight = all
                .Where(l => l.Bounds.Left < a.Right && l.Bounds.Right > a.Left              // same column
                            && Math.Abs(l.Bounds.Top - a.Top) < 6 * h                     // nearby
                            && l.Bounds.Height / Math.Max(a.Height, 1) is var r && r > 1 / rules.MaxSizeRatio && r < rules.MaxSizeRatio)
                .Max(l => l.Bounds.Right);
            return a.Right >= columnRight - 2 * h;
        }

        // Windows OCR puts a space between every CJK word box ("こ ん に ち は"), which
        // wrecks translation. Join them directly, keeping spaces only between Latin words.
        private static string JoinWords(IEnumerable<string> words, bool noSpaces)
        {
            if (!noSpaces) return string.Join(' ', words);

            var sb = new StringBuilder();
            foreach (var word in words)
            {
                if (word.Length == 0) continue;
                if (sb.Length > 0 && IsLatin(sb[^1]) && IsLatin(word[0]))
                    sb.Append(' ');
                sb.Append(word);
            }
            return sb.ToString();
        }

        private static bool IsLatin(char c) => c < 0x0250 && char.IsLetterOrDigit(c);
    }
}
