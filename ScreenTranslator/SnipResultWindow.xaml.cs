using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace ScreenTranslator
{
    /// <summary>
    /// The card a one-shot snip shows: the translation (with a copy button) and the original text,
    /// next to the snipped area. Closes on Esc, the ✕, or a click anywhere else.
    /// </summary>
    public partial class SnipResultWindow : Window
    {
        private Rect _area;

        public SnipResultWindow()
        {
            InitializeComponent();
            SourceInitialized += (_, _) =>
            {
                // Keep the card out of the continuous translation's screenshots
                var hwnd = new WindowInteropHelper(this).Handle;
                NativeMethods.SetWindowDisplayAffinity(hwnd, NativeMethods.WDA_EXCLUDEFROMCAPTURE);
            };
            SizeChanged += (_, _) => KeepOnScreen();
        }

        /// <summary>Shows the card just below <paramref name="area"/> (screen DIPs), or above it if there's no room.</summary>
        public void ShowNear(Rect area)
        {
            _area = area;
            Width = Math.Clamp(area.Width + 24, 360, 560);
            Left = area.Left - 12;
            Top = area.Bottom - 4;
            Show();
            Activate();
            Dispatcher.BeginInvoke(KeepOnScreen, DispatcherPriority.Loaded);
        }

        public void ShowResult(string original, string translation)
        {
            TranslationText.Text = translation;
            OriginalText.Text = original;
            OriginalText.Visibility = Visibility.Visible;
            CopyButton.IsEnabled = translation.Length > 0;
        }

        public void ShowMessage(string message) => TranslationText.Text = message;

        private void KeepOnScreen()
        {
            if (!IsLoaded) return;

            var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
            if (_area.Bottom - 4 + ActualHeight > screen.Bottom)
                Top = Math.Max(screen.Top, _area.Top - ActualHeight + 4);
            Left = Math.Clamp(Left, screen.Left, Math.Max(screen.Left, screen.Right - ActualWidth));
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(TranslationText.Text);
                CopyButton.Content = "Copied";
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                CopyButton.Content = "Clipboard busy, try again";
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) Close();
        }

        private void Window_Deactivated(object? sender, EventArgs e)
        {
            if (IsVisible) Close();
        }
    }
}
