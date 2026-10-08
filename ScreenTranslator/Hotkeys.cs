using System.Windows.Input;

namespace ScreenTranslator
{
    /// <summary>A global key combination such as Ctrl+Shift+R, stored as text in config.json.</summary>
    public readonly record struct Hotkey(ModifierKeys Modifiers, Key Key)
    {
        public bool IsNone => Key == Key.None;

        public uint VirtualKey => (uint)KeyInterop.VirtualKeyFromKey(Key);

        public uint Win32Modifiers =>
            (Modifiers.HasFlag(ModifierKeys.Control) ? NativeMethods.MOD_CONTROL : 0) |
            (Modifiers.HasFlag(ModifierKeys.Shift) ? NativeMethods.MOD_SHIFT : 0) |
            (Modifiers.HasFlag(ModifierKeys.Alt) ? NativeMethods.MOD_ALT : 0) |
            (Modifiers.HasFlag(ModifierKeys.Windows) ? NativeMethods.MOD_WIN : 0) |
            NativeMethods.MOD_NOREPEAT;

        /// <summary>
        /// Global hotkeys swallow their keys in every app, so a plain letter (or Shift+letter) would stop
        /// people typing it. Require Ctrl, Alt or Win, or an F-key.
        /// </summary>
        public bool IsSafeGlobal =>
            !IsNone && (Modifiers.HasFlag(ModifierKeys.Control) || Modifiers.HasFlag(ModifierKeys.Alt) ||
                        Modifiers.HasFlag(ModifierKeys.Windows) || Key is >= Key.F1 and <= Key.F24);

        public override string ToString() =>
            IsNone ? "" : string.Join("+", ModifierNames(Modifiers).Append(KeyName(Key)));

        /// <summary>What's held so far while recording a new hotkey, e.g. "Ctrl+Shift+…".</summary>
        public static string Pending(ModifierKeys modifiers) => string.Join("+", ModifierNames(modifiers).Append("…"));

        private static IEnumerable<string> ModifierNames(ModifierKeys modifiers)
        {
            if (modifiers.HasFlag(ModifierKeys.Windows)) yield return "Win";
            if (modifiers.HasFlag(ModifierKeys.Control)) yield return "Ctrl";
            if (modifiers.HasFlag(ModifierKeys.Alt)) yield return "Alt";
            if (modifiers.HasFlag(ModifierKeys.Shift)) yield return "Shift";
        }

        public static Hotkey Parse(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return default;

            var modifiers = ModifierKeys.None;
            var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts[..^1])
            {
                modifiers |= part.ToLowerInvariant() switch
                {
                    "ctrl" or "control" => ModifierKeys.Control,
                    "shift" => ModifierKeys.Shift,
                    "alt" => ModifierKeys.Alt,
                    "win" or "windows" => ModifierKeys.Windows,
                    _ => ModifierKeys.None
                };
            }

            var keyText = parts[^1];
            Key key = keyText.Length == 1 && char.IsDigit(keyText[0])
                ? Key.D0 + (keyText[0] - '0')
                : Enum.TryParse<Key>(keyText, ignoreCase: true, out var parsed) ? parsed : Key.None;
            return new Hotkey(modifiers, key);
        }

        private static string KeyName(Key key) => key switch
        {
            >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
            >= Key.NumPad0 and <= Key.NumPad9 => $"NumPad{key - Key.NumPad0}",
            _ => key.ToString()
        };
    }

    /// <summary>Something the user can bind to a hotkey.</summary>
    /// <param name="Id">Key in config.json's "Hotkeys".</param>
    public record HotkeyCommand(string Id, string Label, string DefaultKeys);

    public static class HotkeyCommands
    {
        public const string GameBar = "GameBar";
        public const string SelectRegion = "SelectRegion";
        public const string PauseResume = "PauseResume";
        public const string CycleOpacity = "CycleOpacity";
        public const string Peek = "Peek";
        public const string LanguagePrefix = "Language:";

        public static readonly IReadOnlyList<HotkeyCommand> All =
        [
            new(GameBar, "Open / close the game bar", "Ctrl+Shift+G"),
            new(SelectRegion, "Select region", "Ctrl+Shift+R"),
            new(PauseResume, "Pause / resume", "Ctrl+Shift+P"),
            new(CycleOpacity, "Cycle background opacity", "Ctrl+Shift+O"),
            new(Peek, "Peek at the original (hold)", "Ctrl+Shift+H"),
            ..Languages.All.Select((l, i) => new HotkeyCommand(LanguagePrefix + l.OcrTag, l.Name, $"Ctrl+Shift+{i + 1}")),
        ];
    }
}
