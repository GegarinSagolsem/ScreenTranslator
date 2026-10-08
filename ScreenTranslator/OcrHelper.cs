using System.Diagnostics;
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
    /// <param name="Lines">Each line's box, top to bottom.</param>
    public record OcrBlock(string Text, System.Windows.Rect Bounds, double LineHeight, IReadOnlyList<System.Windows.Rect> Lines)
    {
        /// <summary>The label starts here, not at the block's left edge, which can sit under a photo the text wraps around.</summary>
        public System.Windows.Rect FirstLine => Lines[0];
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
            Debug.WriteLine($"OCR language '{language.OcrTag}' -> {installed?.LanguageTag ?? "not installed"}");
            return _engine != null;
        }

        public async Task<IReadOnlyList<OcrBlock>> RecognizeAsync(Bitmap bitmap, bool mergeLines)
        {
            var engine = _engine;
            var language = CurrentLanguage;
            if (engine == null) return [];

            // The engine rejects images larger than MaxImageDimension, so shrink big regions
            double scale = Math.Min(1.0, (double)OcrEngine.MaxImageDimension / Math.Max(bitmap.Width, bitmap.Height));
            using var scaled = scale < 1.0
                ? new Bitmap(bitmap, Math.Max(1, (int)(bitmap.Width * scale)), Math.Max(1, (int)(bitmap.Height * scale)))
                : null;
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
                lines.Add(new OcrBlock(text, bounds, bounds.Height, [bounds]));
            }

            return mergeLines ? MergeLines(lines, language.NoSpaces) : lines;
        }

        /// <summary>
        /// Joins lines that wrap onto the next one, so DeepL sees whole sentences instead of fragments.
        /// Line B continues line A when they share a font size, B sits right under A, they line up
        /// (left, centred, or B wraps under a photo to the left), and A is long and runs to the right
        /// edge of its column (it wrapped). That last rule keeps menus and lists, whose items end short,
        /// as separate entries.
        /// </summary>
        public static IReadOnlyList<OcrBlock> MergeLines(IReadOnlyList<OcrBlock> lines, bool noSpaces)
        {
            var blocks = new List<(StringBuilder Text, System.Windows.Rect Bounds, List<System.Windows.Rect> Lines, OcrBlock Last)>();

            foreach (var line in lines.OrderBy(l => l.Bounds.Top))
            {
                int target = -1;
                for (int i = blocks.Count - 1; i >= 0 && target < 0; i--)
                {
                    if (Continues(blocks[i].Last, line, lines))
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
                .Select(b => new OcrBlock(b.Text.ToString(), b.Bounds, b.Last.LineHeight, b.Lines))
                .ToList();
        }

        private static bool Continues(OcrBlock above, OcrBlock below, IReadOnlyList<OcrBlock> all)
        {
            var a = above.Bounds;
            var b = below.Bounds;
            double h = Math.Max(a.Height, b.Height);

            double ratio = a.Height / Math.Max(b.Height, 1);
            if (ratio < 0.7 || ratio > 1.4) return false; // different font size

            // Web pages use ~1.6 line spacing, which leaves a gap of up to ~0.8 of a line
            double gap = b.Top - a.Bottom;
            if (gap < -0.3 * h || gap > 1.0 * h) return false; // not the very next line

            bool leftAligned = Math.Abs(b.Left - a.Left) < 1.5 * h;
            bool centred = Math.Abs((b.Left + b.Right) / 2 - (a.Left + a.Right) / 2) < 1.5 * h;
            bool wrapsUnderPhoto = b.Left < a.Left - 1.5 * h; // text flowing on underneath a floated image
            if (!leftAligned && !centred && !wrapsUnderPhoto) return false;

            // A wrapped line is long and reaches the right edge of its column; list and menu items end short
            if (a.Width < 6 * h) return false;
            double columnRight = all
                .Where(l => l.Bounds.Left < a.Right && l.Bounds.Right > a.Left              // same column
                            && Math.Abs(l.Bounds.Top - a.Top) < 6 * h                     // nearby
                            && l.Bounds.Height / Math.Max(a.Height, 1) is > 0.7 and < 1.4) // same font size
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
