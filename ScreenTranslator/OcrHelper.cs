using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace ScreenTranslator
{
    /// <param name="Bounds">Line box in pixels of the captured bitmap.</param>
    public record OcrLineResult(string Text, System.Windows.Rect Bounds);

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

        public async Task<IReadOnlyList<OcrLineResult>> RecognizeAsync(Bitmap bitmap)
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

            var lines = new List<OcrLineResult>();
            foreach (var line in result.Lines)
            {
                if (line.Words.Count == 0) continue;

                double left = line.Words.Min(w => w.BoundingRect.X);
                double top = line.Words.Min(w => w.BoundingRect.Y);
                double right = line.Words.Max(w => w.BoundingRect.X + w.BoundingRect.Width);
                double bottom = line.Words.Max(w => w.BoundingRect.Y + w.BoundingRect.Height);

                var text = JoinWords(line.Words.Select(w => w.Text), language.NoSpaces);
                var bounds = new System.Windows.Rect(left / scale, top / scale, (right - left) / scale, (bottom - top) / scale);
                lines.Add(new OcrLineResult(text, bounds));
            }

            return lines;
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
