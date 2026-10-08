using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace ScreenTranslator
{
    public partial class MainWindow : Window
    {
        private const double MinSelectionSize = 12;

        private static readonly Brush DimBrush = new SolidColorBrush(Color.FromArgb(0x44, 0, 0, 0));

        private readonly AppConfig _config;
        private readonly OcrHelper _ocr = new();
        private readonly TranslationHelper _translator = new();
        private readonly DispatcherTimer _captureTimer = new();
        private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(1.5) };
        private readonly Dictionary<int, Action> _hotkeys = new();
        private readonly SolidColorBrush _overlayBrush;
        private IntPtr _hwnd;

        private Rect _selectedRegion = Rect.Empty;
        private Rect _previousRegion = Rect.Empty;
        private Point _startPoint;
        private bool _isChoosingRegion = true;
        private bool _isDragging;
        private bool _isPaused;
        private bool _isProcessing;
        private byte[]? _lastFrameBytes;

        // Bumped whenever region/language/key changes so in-flight frames get discarded
        private int _settingsVersion;

        public MainWindow(AppConfig config)
        {
            InitializeComponent();
            _config = config;
            _overlayBrush = new SolidColorBrush(Colors.Black) { Opacity = config.OverlayOpacity };

            Width = SystemParameters.PrimaryScreenWidth;
            Height = SystemParameters.PrimaryScreenHeight;

            _captureTimer.Interval = TimeSpan.FromMilliseconds(config.CaptureIntervalMs);
            _captureTimer.Tick += CaptureTimer_Tick;
            _statusTimer.Tick += (_, _) => { _statusTimer.Stop(); StatusPanel.Visibility = Visibility.Collapsed; };

            _translator.SetApiKey(config.GetApiKey());
            ApplyLanguage(Languages.Find(config.SourceLanguage));

            SourceInitialized += MainWindow_SourceInitialized;
            Closed += MainWindow_Closed;
        }

        public SourceLanguage CurrentLanguage => _ocr.CurrentLanguage;

        private void MainWindow_SourceInitialized(object? sender, EventArgs e)
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            int style = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE);
            NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE, style | NativeMethods.WS_EX_LAYERED);
            NativeMethods.SetWindowDisplayAffinity(_hwnd, NativeMethods.WDA_EXCLUDEFROMCAPTURE);

            HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
            RegisterHotkeys();
        }

        private void MainWindow_Closed(object? sender, EventArgs e)
        {
            _captureTimer.Stop();
            foreach (var id in _hotkeys.Keys)
                NativeMethods.UnregisterHotKey(_hwnd, id);
            _translator.Dispose();
        }

        #region Hotkeys

        private void RegisterHotkeys()
        {
            var failed = new List<string>();

            void Register(int id, char key, Action action)
            {
                const uint modifiers = NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT | NativeMethods.MOD_NOREPEAT;
                if (NativeMethods.RegisterHotKey(_hwnd, id, modifiers, key))
                    _hotkeys[id] = action;
                else
                    failed.Add($"Ctrl+Shift+{key}");
            }

            Register(1, 'R', BeginSelection);
            Register(2, 'P', TogglePause);
            Register(3, 'O', CycleOpacity);
            for (int i = 0; i < Languages.All.Length; i++)
            {
                int index = i;
                Register(10 + i, (char)('1' + i), () => SetLanguage(index));
            }

            if (failed.Count > 0)
                App.Notify("Hotkeys unavailable", $"Another app is already using {string.Join(", ", failed)}. Use the tray menu instead.");
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == NativeMethods.WM_HOTKEY && _hotkeys.TryGetValue(wParam.ToInt32(), out var action))
            {
                action();
                handled = true;
            }

            return IntPtr.Zero;
        }

        #endregion

        #region Region selection

        public void BeginSelection()
        {
            _captureTimer.Stop();
            _settingsVersion++;
            if (!_selectedRegion.IsEmpty)
                _previousRegion = _selectedRegion;

            TranslationCanvas.Children.Clear();
            SelectionBox.Visibility = Visibility.Collapsed;
            HintText.Text = _previousRegion.IsEmpty
                ? "Drag to select the area to translate"
                : "Drag to select a new area  ·  Esc to keep the current one";
            SetChoosingRegion(true);
            Activate();
        }

        private void SetChoosingRegion(bool choosing)
        {
            _isChoosingRegion = choosing;
            RootGrid.Background = choosing ? DimBrush : Brushes.Transparent;
            HintPanel.Visibility = choosing ? Visibility.Visible : Visibility.Collapsed;
            if (choosing) StatusPanel.Visibility = Visibility.Collapsed;

            // Click-through while translating, so the app underneath stays usable
            int style = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE);
            style = choosing ? style & ~NativeMethods.WS_EX_TRANSPARENT : style | NativeMethods.WS_EX_TRANSPARENT;
            NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE, style);
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!_isChoosingRegion || e.ChangedButton != MouseButton.Left) return;

            _startPoint = e.GetPosition(RootGrid);
            _isDragging = true;
            CaptureMouse();
            ShowSelectionBox(new Rect(_startPoint, _startPoint));
        }

        private void Window_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging)
                ShowSelectionBox(new Rect(_startPoint, e.GetPosition(RootGrid)));
        }

        private void Window_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDragging || e.ChangedButton != MouseButton.Left) return;

            _isDragging = false;
            ReleaseMouseCapture();

            var region = new Rect(_startPoint, e.GetPosition(RootGrid));
            if (region.Width < MinSelectionSize || region.Height < MinSelectionSize)
            {
                // A click, not a drag: stay in selection mode
                SelectionBox.Visibility = Visibility.Collapsed;
                return;
            }

            StartTranslating(region);
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape || !_isChoosingRegion) return;

            if (_isDragging)
            {
                _isDragging = false;
                ReleaseMouseCapture();
                SelectionBox.Visibility = Visibility.Collapsed;
            }
            else if (!_previousRegion.IsEmpty)
            {
                StartTranslating(_previousRegion);
            }
        }

        private void ShowSelectionBox(Rect rect)
        {
            SelectionBox.Margin = new Thickness(rect.X, rect.Y, 0, 0);
            SelectionBox.Width = rect.Width;
            SelectionBox.Height = rect.Height;
            SelectionBox.Visibility = Visibility.Visible;
        }

        private void StartTranslating(Rect region)
        {
            Debug.WriteLine($"Region locked: {region}");
            _selectedRegion = region;
            ShowSelectionBox(region);
            SetChoosingRegion(false);

            _isPaused = false;
            SelectionBox.Stroke = Brushes.Red;
            ResetFrame();
            _captureTimer.Start();
        }

        #endregion

        #region Capture → OCR → translate

        private async void CaptureTimer_Tick(object? sender, EventArgs e)
        {
            // OCR + DeepL can take longer than one tick; never run two frames at once
            if (_isProcessing) return;

            _isProcessing = true;
            try
            {
                await ProcessFrameAsync();
            }
            catch (TranslationException ex)
            {
                App.Notify("Translation failed", ex.Message);
            }
            catch (Exception ex)
            {
                // e.g. CopyFromScreen fails while the UAC / lock screen is up
                Debug.WriteLine($"Frame failed: {ex}");
            }
            finally
            {
                _isProcessing = false;
            }
        }

        private async Task ProcessFrameAsync()
        {
            int version = _settingsVersion;
            var region = _selectedRegion;
            var dpi = VisualTreeHelper.GetDpi(this);

            int x = (int)Math.Round((Left + region.X) * dpi.DpiScaleX);
            int y = (int)Math.Round((Top + region.Y) * dpi.DpiScaleY);
            int width = (int)Math.Round(region.Width * dpi.DpiScaleX);
            int height = (int)Math.Round(region.Height * dpi.DpiScaleY);
            if (width <= 0 || height <= 0) return;

            var lines = await Task.Run(() => CaptureAndRecognizeAsync(x, y, width, height));
            if (lines == null) return; // screen unchanged

            if (version != _settingsVersion)
            {
                ResetFrame(); // settings changed mid-frame; redo it next tick
                return;
            }

            if (lines.Count == 0)
            {
                TranslationCanvas.Children.Clear(); // text is gone, so drop stale overlays
                return;
            }

            var translations = await _translator.TranslateAsync(lines.Select(l => l.Text).ToList());
            if (version != _settingsVersion) return;

            // Swap all overlays at once so they don't flicker in line by line
            TranslationCanvas.Children.Clear();
            for (int i = 0; i < lines.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(translations[i]))
                    DrawTranslation(translations[i]!, lines[i].Bounds, region, dpi);
            }
        }

        private async Task<IReadOnlyList<OcrLineResult>?> CaptureAndRecognizeAsync(int x, int y, int width, int height)
        {
            using var bitmap = ScreenCapture.CaptureRegion(x, y, width, height);
            var pixels = ScreenCapture.BitmapToBytes(bitmap);

            var last = _lastFrameBytes;
            if (last != null && !ScreenCapture.HasChanged(last, pixels))
                return null;

            _lastFrameBytes = pixels;
            return await _ocr.RecognizeAsync(bitmap);
        }

        private void ResetFrame()
        {
            _settingsVersion++;
            _lastFrameBytes = null;
        }

        private void DrawTranslation(string text, Rect pixelBounds, Rect region, DpiScale dpi)
        {
            double left = region.X + pixelBounds.X / dpi.DpiScaleX;
            double top = region.Y + pixelBounds.Y / dpi.DpiScaleY;
            double lineHeight = pixelBounds.Height / dpi.DpiScaleY;

            var label = new Border
            {
                Background = _overlayBrush,
                Padding = new Thickness(3, 1, 3, 1),
                CornerRadius = new CornerRadius(2),
                MaxWidth = Math.Max(region.Right - left, 80),
                Child = new TextBlock
                {
                    Text = text,
                    Foreground = Brushes.White,
                    FontSize = Math.Clamp(lineHeight * 0.8, 12, 28),
                    TextWrapping = TextWrapping.Wrap
                }
            };

            Canvas.SetLeft(label, left);
            Canvas.SetTop(label, top);
            TranslationCanvas.Children.Add(label);
        }

        #endregion

        #region Settings

        public void TogglePause()
        {
            if (_isChoosingRegion) return;

            _isPaused = !_isPaused;
            if (_isPaused)
            {
                _captureTimer.Stop();
            }
            else
            {
                ResetFrame();
                _captureTimer.Start();
            }

            SelectionBox.Stroke = _isPaused ? Brushes.Gray : Brushes.Red;
            ShowStatus(_isPaused ? "Paused" : "Resumed");
        }

        public void SetLanguage(int index)
        {
            var language = Languages.All[index];
            ApplyLanguage(language);
            _config.SourceLanguage = language.OcrTag;
            _config.Save();
            ShowStatus($"{language.Name} → {_config.TargetLanguage}");
        }

        private void ApplyLanguage(SourceLanguage language)
        {
            if (!_ocr.SetLanguage(language))
            {
                App.Notify($"{language.Name} OCR not installed",
                    $"Add {language.Name} in Settings › Time & language › Language & region, including its optical character recognition feature.");
            }

            _translator.SetLanguages(language.DeepLCode, _config.TargetLanguage);
            ResetFrame();
        }

        public void RefreshApiKey()
        {
            _translator.SetApiKey(_config.GetApiKey());
            ResetFrame();
        }

        public void CycleOpacity()
        {
            double opacity = _overlayBrush.Opacity - 0.25;
            if (opacity < 0.25) opacity = 1.0;

            // Every overlay shares this brush, so existing labels update too
            _overlayBrush.Opacity = opacity;
            _config.OverlayOpacity = opacity;
            _config.Save();
            ShowStatus($"Overlay opacity {opacity:P0}");
        }

        private void ShowStatus(string message)
        {
            if (_isChoosingRegion) return;

            StatusText.Text = message;
            StatusPanel.Visibility = Visibility.Visible;
            _statusTimer.Stop();
            _statusTimer.Start();
        }

        #endregion
    }
}
