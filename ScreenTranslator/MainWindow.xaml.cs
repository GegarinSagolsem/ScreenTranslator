using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Interop;

namespace ScreenTranslator
{
    public partial class MainWindow : Window
    {
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_LAYERED = 0x00080000;
        private const int GWL_EXSTYLE = -20;
        private Rect _selectedRegion;

        private System.Windows.Point _startPoint;
        private bool _isSelecting = false;
        private byte[]? _lastFrameBytes;

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        private ScreenCapture _screenCapture = new ScreenCapture();
        private System.Windows.Threading.DispatcherTimer? _captureTimer;
        private OcrHelper _ocrHelper = new OcrHelper();
        private TranslationHelper _translator = new TranslationHelper();

        [DllImport("user32.dll")]
        private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

        private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        private string[] _ocrLangs = { "ja", "zh-Hans", "ko" };
        private string[] _targetLangs = { "EN", "EN", "EN" };
        private int _langIndex = 0;
        private double _overlayOpacity = 0.75;

        private const int HOTKEY_ID_RESELECT = 9000;
        private const int HOTKEY_ID_PAUSE = 9001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint VK_R = 0x52; // R key
        private const uint VK_P = 0x50; // P key
        private const int HOTKEY_ID_OPACITY = 9003;
        private const uint VK_O = 0x4F; // O key
        private const int HOTKEY_ID_LANG_1 = 9010;
        private const int HOTKEY_ID_LANG_2 = 9011;
        private const int HOTKEY_ID_LANG_3 = 9012;
        private const uint VK_1 = 0x31;
        private const uint VK_2 = 0x32;
        private const uint VK_3 = 0x33;

        public MainWindow()
        {
            InitializeComponent();
            this.Width = SystemParameters.PrimaryScreenWidth;
            this.Height = SystemParameters.PrimaryScreenHeight;
            SourceInitialized += MainWindow_SourceInitialized;
        }

        private bool _isPaused = false;

        private void ReselectRegion()
        {
            System.Diagnostics.Debug.WriteLine("Reselect triggered.");

            _captureTimer?.Stop();
            ClearTranslationOverlays();
            SelectionBox.Visibility = Visibility.Collapsed;
            SelectionBox.Width = 0;
            SelectionBox.Height = 0;

            // Turn off click-through so we can drag-select again
            var hwnd = new WindowInteropHelper(this).Handle;
            int extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle & ~WS_EX_TRANSPARENT);
        }
        public void TogglePauseFromTray() => TogglePause();
        public void ReselectRegionFromTray() => ReselectRegion();
        private void TogglePause()
        {
            _isPaused = !_isPaused;
            System.Diagnostics.Debug.WriteLine(_isPaused ? "Paused." : "Resumed.");

            if (_isPaused)
                _captureTimer?.Stop();
            else
                _captureTimer?.Start();
        }

        private (int x, int y, int width, int height) GetPhysicalPixelRegion()
        {
            var source = PresentationSource.FromVisual(this);
            double dpiX = 1.0, dpiY = 1.0;

            if (source?.CompositionTarget != null)
            {
                dpiX = source.CompositionTarget.TransformToDevice.M11;
                dpiY = source.CompositionTarget.TransformToDevice.M22;
            }

            int x = (int)(_selectedRegion.X * dpiX);
            int y = (int)(_selectedRegion.Y * dpiY);
            int width = (int)(_selectedRegion.Width * dpiX);
            int height = (int)(_selectedRegion.Height * dpiY);

            return (x, y, width, height);
        }

        private void MainWindow_SourceInitialized(object? sender, EventArgs e)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_LAYERED);

            SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE);

            RegisterHotKey(hwnd, HOTKEY_ID_RESELECT, MOD_CONTROL | MOD_SHIFT, VK_R);
            RegisterHotKey(hwnd, HOTKEY_ID_PAUSE, MOD_CONTROL | MOD_SHIFT, VK_P);
            RegisterHotKey(hwnd, HOTKEY_ID_OPACITY, MOD_CONTROL | MOD_SHIFT, VK_O);
            RegisterHotKey(hwnd, HOTKEY_ID_LANG_1, MOD_CONTROL | MOD_SHIFT, VK_1);
            RegisterHotKey(hwnd, HOTKEY_ID_LANG_2, MOD_CONTROL | MOD_SHIFT, VK_2);
            RegisterHotKey(hwnd, HOTKEY_ID_LANG_3, MOD_CONTROL | MOD_SHIFT, VK_3);

            var source = HwndSource.FromHwnd(hwnd);
            source?.AddHook(WndProc);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_HOTKEY = 0x0312;

            if (msg == WM_HOTKEY)
            {
                int id = wParam.ToInt32();

                if (id == HOTKEY_ID_RESELECT)
                {
                    ReselectRegion();
                    handled = true;
                }
                else if (id == HOTKEY_ID_PAUSE)
                {
                    TogglePause();
                    handled = true;
                }
                else if (id == HOTKEY_ID_LANG_1)
                {
                    SetLanguage(0);
                    handled = true;
                }
                else if (id == HOTKEY_ID_LANG_2)
                {
                    SetLanguage(1);
                    handled = true;
                }
                else if (id == HOTKEY_ID_LANG_3)
                {
                    SetLanguage(2);
                    handled = true;
                }
                else if (id == HOTKEY_ID_OPACITY)
                {
                    CycleOpacity();
                    handled = true;
                }
            }

            return IntPtr.Zero;
        }
        private void Window_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _startPoint = e.GetPosition(RootGrid);
            _isSelecting = true;

            SelectionBox.Visibility = Visibility.Visible;
            SelectionBox.Width = 0;
            SelectionBox.Height = 0;
        }

        private void Window_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!_isSelecting) return;

            var currentPoint = e.GetPosition(RootGrid);

            double x = Math.Min(_startPoint.X, currentPoint.X);
            double y = Math.Min(_startPoint.Y, currentPoint.Y);
            double width = Math.Abs(currentPoint.X - _startPoint.X);
            double height = Math.Abs(currentPoint.Y - _startPoint.Y);

            SelectionBox.Margin = new Thickness(x, y, 0, 0);
            SelectionBox.Width = width;
            SelectionBox.Height = height;
        }

        private void Window_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _isSelecting = false;

            _selectedRegion = new Rect(
                SelectionBox.Margin.Left,
                SelectionBox.Margin.Top,
                SelectionBox.Width,
                SelectionBox.Height);

            System.Diagnostics.Debug.WriteLine($"Region locked: {_selectedRegion}");

            var hwnd = new WindowInteropHelper(this).Handle;
            int extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_TRANSPARENT);

            StartCaptureLoop();
        }

        private void StartCaptureLoop()
        {
            _captureTimer?.Stop();

            _captureTimer = new System.Windows.Threading.DispatcherTimer();
            _captureTimer.Interval = TimeSpan.FromMilliseconds(500);
            _captureTimer.Tick += CaptureTimer_Tick;
            _captureTimer.Start();
        }

        private async void CaptureTimer_Tick(object? sender, EventArgs e)
        {
            var (x, y, width, height) = GetPhysicalPixelRegion();

            if (width <= 0 || height <= 0) return;

            var bitmap = _screenCapture.CaptureRegion(x, y, width, height);
            var currentBytes = ScreenCapture.BitmapToBytes(bitmap);

            if (_lastFrameBytes != null && !ScreenCapture.HasChanged(_lastFrameBytes, currentBytes))
            {
                System.Diagnostics.Debug.WriteLine("No change, skipping.");
                return;
            }

            _lastFrameBytes = currentBytes;
            System.Diagnostics.Debug.WriteLine($"CHANGED frame at {DateTime.Now:HH:mm:ss.fff} — running OCR...");

            var result = await _ocrHelper.RecognizeTextAsync(bitmap);
            if (result == null || string.IsNullOrWhiteSpace(result.Text))
            {
                System.Diagnostics.Debug.WriteLine("No text found.");
                return;
            }

            ClearTranslationOverlays();

            var source = PresentationSource.FromVisual(this);
            double dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
            double dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

            foreach (var line in result.Lines)
            {
                var translated = await _translator.TranslateAsync(line.Text);
                if (translated == null) continue;

                System.Diagnostics.Debug.WriteLine($"Original: '{line.Text}' -> Translated: '{translated}'");

                var firstWord = line.Words.FirstOrDefault();
                if (firstWord == null) continue;

                var box = firstWord.BoundingRect;

                double wpfX = (box.X / dpiX) + _selectedRegion.X;
                double wpfY = (box.Y / dpiY) + _selectedRegion.Y;
                double wpfWidth = _selectedRegion.Width;
                double wpfHeight = box.Height / dpiY;

                DrawTranslation(translated, wpfX, wpfY, wpfWidth, wpfHeight);
            }
        }

        private void ClearTranslationOverlays()
        {
            TranslationCanvas.Children.Clear();
        }

        private void DrawTranslation(string text, double x, double y, double width, double height)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Colors.Black) { Opacity = _overlayOpacity },
                Padding = new Thickness(2)
            };

            var textBlock = new TextBlock
            {
                Text = text,
                Foreground = Brushes.White,
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                Width = width
            };

            border.Child = textBlock;

            Canvas.SetLeft(border, x);
            Canvas.SetTop(border, y);

            TranslationCanvas.Children.Add(border);
        }
        private void SetLanguage(int index)
        {
            _langIndex = index;
            _ocrHelper.SetLanguage(_ocrLangs[_langIndex]);
            _translator.SetTargetLang(_targetLangs[_langIndex]);
            System.Diagnostics.Debug.WriteLine($"Language switched to: {_ocrLangs[_langIndex]} -> {_targetLangs[_langIndex]}");
        }

        private void CycleOpacity()
        {
            _overlayOpacity -= 0.25;
            if (_overlayOpacity < 0.25) _overlayOpacity = 1.0;
            System.Diagnostics.Debug.WriteLine($"Opacity set to: {_overlayOpacity}");

            foreach (var child in TranslationCanvas.Children)
            {
                if (child is Border border)
                    border.Background = new SolidColorBrush(Colors.Black) { Opacity = _overlayOpacity };
            }
        }
    }
}