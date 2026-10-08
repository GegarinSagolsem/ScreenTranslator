namespace ScreenTranslator
{
    /// <param name="OcrTag">BCP-47 prefix matched against installed Windows OCR languages.</param>
    /// <param name="DeepLCode">DeepL source_lang code; pinning it stops short lines being misdetected.</param>
    /// <param name="NoSpaces">Script is written without spaces, so OCR word boxes are joined directly.</param>
    public record SourceLanguage(string OcrTag, string DeepLCode, string Name, bool NoSpaces);

    public static class Languages
    {
        // Order matters: index i is bound to Ctrl+Shift+(i+1)
        public static readonly SourceLanguage[] All =
        [
            new("ja", "JA", "Japanese", NoSpaces: true),
            new("zh-Hans", "ZH", "Chinese (Simplified)", NoSpaces: true),
            new("ko", "KO", "Korean", NoSpaces: false),
        ];

        public static SourceLanguage Find(string ocrTag) =>
            All.FirstOrDefault(l => l.OcrTag.Equals(ocrTag, StringComparison.OrdinalIgnoreCase)) ?? All[0];
    }
}
