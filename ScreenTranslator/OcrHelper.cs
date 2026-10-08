using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace ScreenTranslator
{
    /// <summary>One line, or several wrapped lines merged into one sentence block.</summary>
    /// <param name="Bounds">Box in pixels of the captured bitmap.</param>
    /// <param name="LineHeight">Height of a single line in pixels, used to size the overlay font.</param>
    public record OcrBlock(string Text, System.Windows.Rect Bounds, double LineHeight);

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
                lines.Add(new OcrBlock(text, bounds, bounds.Height));
            }

            return mergeLines ? MergeLines(lines, language.NoSpaces) : lines;
        }

        /// <summary>
        /// Joins lines that wrap onto the next one, so DeepL sees whole sentences instead of fragments.
        /// Line B continues line A when they share a font size, B sits right under A, they're
        /// left-aligned or centred together, and A runs to the right edge of the text (it wrapped).
        /// That last rule keeps menus and lists, whose items end short, as separate entries.
        /// </summary>
        public static IReadOnlyList<OcrBlock> MergeLines(IReadOnlyList<OcrBlock> lines, bool noSpaces)
        {
            var blocks = new List<(StringBuilder Text, System.Windows.Rect Bounds, OcrBlock Last)>();

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
                    blocks.Add((new StringBuilder(line.Text), line.Bounds, line));
                    continue;
                }

                var (text, bounds, _) = blocks[target];
                if (!noSpaces || (text.Length > 0 && line.Text.Length > 0 && IsLatin(text[^1]) && IsLatin(line.Text[0])))
                    text.Append(' ');
                text.Append(line.Text);
                bounds.Union(line.Bounds);
                blocks[target] = (text, bounds, line);
            }

            return blocks
                .Select(b => new OcrBlock(b.Text.ToString(), b.Bounds, b.Last.LineHeight))
                .ToList();
        }

        private static bool Continues(OcrBlock above, OcrBlock below, IReadOnlyList<OcrBlock> all)
        {
            var a = above.Bounds;
            var b = below.Bounds;
            double h = Math.Max(a.Height, b.Height);

            double ratio = a.Height / Math.Max(b.Height, 1);
            if (ratio < 0.7 || ratio > 1.4) return false; // different font size

            double gap = b.Top - a.Bottom;
            if (gap < -0.3 * h || gap > 0.6 * h) return false; // not the very next line

            bool leftAligned = Math.Abs(b.Left - a.Left) < 1.5 * h;
            bool centred = Math.Abs((b.Left + b.Right) / 2 - (a.Left + a.Right) / 2) < 1.5 * h;
            if (!leftAligned && !centred) return false;

            // Right edge of the text this line belongs to: the widest line in the same font size
            double textRight = all
                .Where(l => l.Bounds.Height / Math.Max(a.Height, 1) is > 0.7 and < 1.4)
                .Max(l => l.Bounds.Right);
            return a.Right >= textRight - 2 * h;
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
