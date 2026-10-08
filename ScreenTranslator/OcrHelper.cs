using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace ScreenTranslator
{
    public class OcrHelper
    {
        private string _currentLang = "ja";

        public void SetLanguage(string langCode)
        {
            _currentLang = langCode;
        }

        public void CheckJapaneseSupport()
        {
            var language = new Language("ja");
            bool isSupported = OcrEngine.IsLanguageSupported(language);
            System.Diagnostics.Debug.WriteLine($"Japanese OCR supported: {isSupported}");
        }

        public async Task<OcrResult?> RecognizeTextAsync(Bitmap bitmap)
        {
            var softwareBitmap = await ConvertToSoftwareBitmapAsync(bitmap);
            if (softwareBitmap == null) return null;

            var lang = new Language(_currentLang);
            bool supported = OcrEngine.IsLanguageSupported(lang);
            System.Diagnostics.Debug.WriteLine($"Trying OCR lang '{_currentLang}' (BCP47: {lang.LanguageTag}) - supported: {supported}");

            var engine = OcrEngine.TryCreateFromLanguage(lang);
            if (engine == null)
            {
                System.Diagnostics.Debug.WriteLine($"Could not create OCR engine for '{_currentLang}'.");
                return null;
            }

            var result = await engine.RecognizeAsync(softwareBitmap);
            return result;
        }

        private async Task<SoftwareBitmap?> ConvertToSoftwareBitmapAsync(Bitmap bitmap)
        {
            using var memoryStream = new MemoryStream();
            bitmap.Save(memoryStream, ImageFormat.Png);
            memoryStream.Position = 0;

            var randomAccessStream = new InMemoryRandomAccessStream();
            using (var outputStream = randomAccessStream.GetOutputStreamAt(0))
            {
                var writer = new DataWriter(outputStream);
                writer.WriteBytes(memoryStream.ToArray());
                await writer.StoreAsync();
                await outputStream.FlushAsync();
            }

            var decoder = await BitmapDecoder.CreateAsync(randomAccessStream);
            var softwareBitmap = await decoder.GetSoftwareBitmapAsync();

            return softwareBitmap;
        }
    }
}