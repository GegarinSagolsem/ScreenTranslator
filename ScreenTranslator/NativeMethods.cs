using System.Runtime.InteropServices;

namespace ScreenTranslator
{
    internal static class NativeMethods
    {
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TRANSPARENT = 0x00000020;
        public const int WS_EX_LAYERED = 0x00080000;

        public const int WM_HOTKEY = 0x0312;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_SHIFT = 0x0004;
        public const uint MOD_NOREPEAT = 0x4000; // holding the keys fires once, not repeatedly

        // Hides the window from screenshots, so the overlay never OCRs its own translations
        public const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

        [DllImport("user32.dll")]
        public static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll")]
        public static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        [DllImport("user32.dll")]
        public static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

        [DllImport("user32.dll")]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        /// <summary>Lets mouse input pass straight through the window to whatever is underneath.</summary>
        public static void SetClickThrough(IntPtr hwnd, bool enabled)
        {
            if (hwnd == IntPtr.Zero) return;

            int style = GetWindowLong(hwnd, GWL_EXSTYLE);
            style = enabled ? style | WS_EX_TRANSPARENT : style & ~WS_EX_TRANSPARENT;
            SetWindowLong(hwnd, GWL_EXSTYLE, style);
        }
    }
}
