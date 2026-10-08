using System.Diagnostics;
using System.IO;

namespace ScreenTranslator
{
    /// <summary>
    /// Plain-text log in %AppData%\ScreenTranslator\logs for bug reports. It records what happened
    /// (errors, settings, timings) but never screen text, translations or API keys.
    /// </summary>
    public static class Log
    {
        private const long MaxBytes = 1_000_000; // then the file rolls over to *.old.log
        private static readonly object Gate = new();

        public static string Folder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ScreenTranslator", "logs");

        public static string FilePath => Path.Combine(Folder, "screentranslator.log");

        public static void Info(string message) => Write("INFO ", message);

        public static void Warn(string message) => Write("WARN ", message);

        public static void Error(string message, Exception? ex = null) =>
            Write("ERROR", ex == null ? message : $"{message}: {ex}");

        private static void Write(string level, string message)
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {message}";
            Debug.WriteLine(line);

            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(Folder);
                    var file = new FileInfo(FilePath);
                    if (file.Exists && file.Length > MaxBytes)
                        File.Move(FilePath, Path.ChangeExtension(FilePath, ".old.log"), overwrite: true);
                    File.AppendAllText(FilePath, line + Environment.NewLine);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Logging must never take the app down
            }
        }
    }
}
