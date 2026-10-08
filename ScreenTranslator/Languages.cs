namespace ScreenTranslator
{
    /// <param name="OcrTag">BCP-47 prefix matched against installed Windows OCR languages.</param>
    /// <param name="DeepLCode">DeepL source_lang code; pinning it stops short lines being misdetected.</param>
    /// <param name="GoogleCode">Google Translate source code.</param>
    /// <param name="NoSpaces">Script is written without spaces, so OCR word boxes are joined directly.</param>
    public record SourceLanguage(string OcrTag, string DeepLCode, string GoogleCode, string Name, bool NoSpaces);

    /// <param name="Code">DeepL target_lang code.</param>
    public record TargetLanguage(string Code, string Name);

    public static class Languages
    {
        // Order matters: index i is bound to Ctrl+Shift+(i+1)
        public static readonly SourceLanguage[] All =
        [
            new("ja", "JA", "ja", "Japanese", NoSpaces: true),
            new("zh-Hans", "ZH", "zh-CN", "Chinese (Simplified)", NoSpaces: true),
            new("ko", "KO", "ko", "Korean", NoSpaces: false),
        ];

        // Common DeepL targets; any other DeepL code still works via config.json
        public static readonly TargetLanguage[] Targets =
        [
            new("EN-US", "English (US)"),
            new("EN-GB", "English (UK)"),
            new("ES", "Spanish"),
            new("FR", "French"),
            new("DE", "German"),
            new("IT", "Italian"),
            new("PT-BR", "Portuguese (Brazil)"),
            new("RU", "Russian"),
            new("PL", "Polish"),
            new("NL", "Dutch"),
            new("TR", "Turkish"),
            new("UK", "Ukrainian"),
            new("ID", "Indonesian"),
            new("JA", "Japanese"),
            new("KO", "Korean"),
            new("ZH-HANS", "Chinese (Simplified)"),
            new("ZH-HANT", "Chinese (Traditional)"),
        ];

        public static SourceLanguage Find(string ocrTag) =>
            All.FirstOrDefault(l => l.OcrTag.Equals(ocrTag, StringComparison.OrdinalIgnoreCase)) ?? All[0];
    }
}
