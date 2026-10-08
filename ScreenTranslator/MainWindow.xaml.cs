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
        private readonly DispatcherTimer _peekTimer = new() { Interval = TimeSpan.FromMilliseconds(40) };
        private readonly FrameGate _frameGate = new();
        private readonly Dictionary<int, Action> _hotkeys = new();
        private int _peekKey;
        private readonly SolidColorBrush _overlayBrush;
        private IntPtr _hwnd;

        private Rect _selectedRegion = Rect.Empty;
        private Rect _previousRegion = Rect.Empty;
        private Point _startPoint;
        private bool _isChoosingRegion = true;
        private bool _isDragging;
        private bool _isPaused;
        private bool _isProcessing;
        private bool _isSnipping;
        private bool _wasChoosingBeforeSnip;
        private bool _resumeAfterSnip;
        private string? _drawnSignature; // text + positions on screen; null forces a redraw

        // What's on screen, so a text size change can re-lay it out without another OCR pass
        private (IReadOnlyList<OcrBlock> Blocks, IReadOnlyList<string?> Translations, Rect Region, DpiScale Dpi)? _drawn;

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
            _peekTimer.Tick += PeekTimer_Tick;

            _translator.Configure(config.ActiveService, config.GetApiKey(config.ActiveService));
            _translator.SetGlossary(config.Glossary);
            ApplyLanguage(Languages.Find(config.SourceLanguage));

            SourceInitialized += MainWindow_SourceInitialized;
            Closed += MainWindow_Closed;
        }

        /// <summary>Raised when region, pause or language state changes, so the game bar can redraw.</summary>
        public event Action? TranslatorStateChanged;

        public event Action? GameBarRequested;

        public event Action? UsageChanged
        {
            add => _translator.UsageChanged += value;
            remove => _translator.UsageChanged -= value;
        }

        public SourceLanguage CurrentLanguage => _ocr.CurrentLanguage;
        public bool IsChoosingRegion => _isChoosingRegion;
        public bool IsPaused => _isPaused;
        public bool HasRegion => !_selectedRegion.IsEmpty;
        public double TextScale => _config.TextScale;
        public DeepLUsage? Usage => _translator.Usage;
        public TranslationService Service => _translator.Service;

        public Task RefreshUsageAsync() => _translator.RefreshUsageAsync();

        private void MainWindow_SourceInitialized(object? sender, EventArgs e)
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            int style = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE);
            NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE, style | NativeMethods.WS_EX_LAYERED);
            if (!NativeMethods.SetWindowDisplayAffinity(_hwnd, NativeMethods.WDA_EXCLUDEFROMCAPTURE))
                Log.Warn("Could not hide the overlay from screen capture; OCR may read the translations back");
            CoverAllMonitors();

            HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);

            var failed = RegisterHotkeys();
            if (failed.Count > 0)
                App.Notify("Hotkeys unavailable", $"Another app is already using {string.Join(", ", failed)}. Change them in the game bar › Settings.");
        }

        private void MainWindow_Closed(object? sender, EventArgs e)
        {
            _captureTimer.Stop();
            UnregisterHotkeys();
            _translator.Dispose();
        }

        /// <summary>
        /// Stretches the overlay over every monitor, so a region can be selected on any of them, and keeps
        /// the hint and status messages on the primary monitor.
        /// </summary>
        private void CoverAllMonitors()
        {
            var all = NativeMethods.VirtualScreen;
            var primary = NativeMethods.PrimaryScreen;
            NativeMethods.PlaceTopmost(_hwnd, all);

            var dpi = VisualTreeHelper.GetDpi(this);
            PrimaryArea.Margin = new Thickness((primary.X - all.X) / dpi.DpiScaleX, (primary.Y - all.Y) / dpi.DpiScaleY, 0, 0);
            PrimaryArea.Width = primary.Width / dpi.DpiScaleX;
            PrimaryArea.Height = primary.Height / dpi.DpiScaleY;
            Log.Info($"Overlay covers {all.Width}x{all.Height} at {all.X},{all.Y} (primary {primary.Width}x{primary.Height}, scale {dpi.DpiScaleX:P0})");
        }

        #region Hotkeys

        /// <summary>
        /// (Re)registers every configured hotkey. Returns the ones that couldn't be taken, usually
        /// because another app already uses that combination.
        /// </summary>
        public IReadOnlyList<string> RegisterHotkeys()
        {
            UnregisterHotkeys();
            var failed = new List<string>();

            int id = 1;
            foreach (var command in HotkeyCommands.All)
            {
                var hotkey = _config.GetHotkey(command.Id);
                if (!hotkey.IsNone)
                {
                    if (NativeMethods.RegisterHotKey(_hwnd, id, hotkey.Win32Modifiers, hotkey.VirtualKey))
                        _hotkeys[id] = ActionFor(command.Id);
                    else
                        failed.Add($"{hotkey} ({command.Label})");
                }
                id++;
            }

            _peekKey = (int)_config.GetHotkey(HotkeyCommands.Peek).VirtualKey;
            if (failed.Count > 0)
                Log.Warn($"Hotkeys already taken by other apps: {string.Join(", ", failed)}");
            return failed;
        }

        /// <summary>Frees every hotkey, e.g. while the user is typing a new combination in Settings.</summary>
        public void UnregisterHotkeys()
        {
            foreach (var id in _hotkeys.Keys)
                NativeMethods.UnregisterHotKey(_hwnd, id);
            _hotkeys.Clear();
        }

        private Action ActionFor(string commandId) => commandId switch
        {
            HotkeyCommands.GameBar => () => GameBarRequested?.Invoke(),
            HotkeyCommands.SelectRegion => BeginSelection,
            HotkeyCommands.Snip => BeginSnip,
            HotkeyCommands.PauseResume => TogglePause,
            HotkeyCommands.CycleOpacity => CycleOpacity,
            HotkeyCommands.Peek => BeginPeek,
            _ => () => SetLanguage(Array.FindIndex(Languages.All, l => HotkeyCommands.LanguagePrefix + l.OcrTag == commandId))
        };

        // RegisterHotKey only reports the key going down, so watch for the peek key coming back up
        private void BeginPeek()
        {
            if (_peekTimer.IsEnabled) return;

            TranslationCanvas.Visibility = Visibility.Hidden;
            _peekTimer.Start();
        }

        private void PeekTimer_Tick(object? sender, EventArgs e)
        {
            if ((NativeMethods.GetAsyncKeyState(_peekKey) & 0x8000) != 0) return; // still held

            _peekTimer.Stop();
            TranslationCanvas.Visibility = Visibility.Visible;
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            // Monitors were added, removed or rescaled. WPF also resizes the window on a DPI change,
            // so stretch it back over every monitor once WPF is done.
            if (msg is NativeMethods.WM_DISPLAYCHANGE or NativeMethods.WM_DPICHANGED)
                Dispatcher.BeginInvoke(CoverAllMonitors);

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
            _isSnipping = false;
            _captureTimer.Stop();
            _settingsVersion++;
            if (!_selectedRegion.IsEmpty)
                _previousRegion = _selectedRegion;

            TranslationCanvas.Children.Clear();
            _drawn = null;
            SelectionBox.Visibility = Visibility.Collapsed;
            HintText.Text = ChooseRegionHint;
            SetChoosingRegion(true);
            Activate();
            TranslatorStateChanged?.Invoke();
        }

        private string ChooseRegionHint => _previousRegion.IsEmpty
            ? "Drag to select the area to translate"
            : "Drag to select a new area  ·  Esc to keep the current one";

        /// <summary>
        /// One-shot: drag over some text to see its translation in a card. The translated region (if any)
        /// is left as it was and resumes afterwards.
        /// </summary>
        public void BeginSnip()
        {
            if (_isSnipping || _isDragging) return;

            _isSnipping = true;
            _wasChoosingBeforeSnip = _isChoosingRegion;
            _resumeAfterSnip = !_isChoosingRegion && HasRegion && !_isPaused;
            _captureTimer.Stop();
            SelectionBox.Visibility = Visibility.Collapsed;
            HintText.Text = "Drag over the text to translate once  ·  Esc to cancel";
            SetChoosingRegion(true);
            Activate();
        }

        /// <summary>Puts things back the way they were before the snip.</summary>
        private void EndSnip()
        {
            _isSnipping = false;
            if (_wasChoosingBeforeSnip)
            {
                HintText.Text = ChooseRegionHint;
                SelectionBox.Visibility = Visibility.Collapsed;
                return;
            }

            SetChoosingRegion(false);
            if (HasRegion) ShowSelectionBox(_selectedRegion);
            if (_resumeAfterSnip)
            {
                ResetFrame();
                _captureTimer.Start();
            }
        }

        private async Task SnipAsync(Rect region)
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            var pixels = ToScreenPixels(region, dpi);
            var card = new SnipResultWindow();
            card.ShowNear(new Rect(Left + region.X, Top + region.Y, region.Width, region.Height));

            while (_isProcessing) await Task.Delay(50); // one OCR at a time
            _isProcessing = true;
            try
            {
                var timer = Stopwatch.StartNew();
                bool mergeLines = _config.MergeLines, verticalText = _config.VerticalText;
                var blocks = await Task.Run(async () =>
                {
                    using var bitmap = ScreenCapture.CaptureRegion(pixels.X, pixels.Y, pixels.Width, pixels.Height);
                    return await _ocr.RecognizeAsync(bitmap, mergeLines, verticalText);
                });

                if (blocks.Count == 0)
                {
                    card.ShowMessage("No text found in that area.");
                    return;
                }

                var translations = await _translator.TranslateAsync(blocks.Select(b => b.Text).ToList());
                card.ShowResult(
                    string.Join("\n", blocks.Select(b => b.Text)),
                    string.Join("\n", translations.Where(t => !string.IsNullOrWhiteSpace(t))));
                Log.Info($"Snip: translated {blocks.Count} block(s) in {timer.ElapsedMilliseconds} ms");
            }
            catch (TranslationException ex)
            {
                Log.Warn($"Snip translation failed: {ex.Message}");
                card.ShowMessage(ex.Message);
            }
            catch (Exception ex)
            {
                Log.Error("Snip failed", ex);
                card.ShowMessage("Couldn't read that area.");
            }
            finally
            {
                _isProcessing = false;
            }
        }

        /// <summary>A region of the overlay (DIPs) in screen pixels. The overlay spans every monitor and may start left of / above the primary one.</summary>
        private System.Drawing.Rectangle ToScreenPixels(Rect region, DpiScale dpi)
        {
            NativeMethods.GetWindowRect(_hwnd, out var window);
            return new System.Drawing.Rectangle(
                window.Left + (int)Math.Round(region.X * dpi.DpiScaleX),
                window.Top + (int)Math.Round(region.Y * dpi.DpiScaleY),
                (int)Math.Round(region.Width * dpi.DpiScaleX),
                (int)Math.Round(region.Height * dpi.DpiScaleY));
        }

        private void SetChoosingRegion(bool choosing)
        {
            _isChoosingRegion = choosing;
            RootGrid.Background = choosing ? DimBrush : Brushes.Transparent;
            HintPanel.Visibility = choosing ? Visibility.Visible : Visibility.Collapsed;
            if (choosing) StatusPanel.Visibility = Visibility.Collapsed;

            // Click-through while translating, so the app underneath stays usable
            NativeMethods.SetClickThrough(_hwnd, !choosing);
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

            if (_isSnipping)
            {
                EndSnip();
                _ = SnipAsync(region);
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
            else if (_isSnipping)
            {
                EndSnip();
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
            Log.Info($"Region selected: {region.Width:F0}x{region.Height:F0} at {region.X:F0},{region.Y:F0}");
            _selectedRegion = region;
            ShowSelectionBox(region);
            SetChoosingRegion(false);

            _isPaused = false;
            SelectionBox.Stroke = Brushes.Red;
            ResetFrame();
            _captureTimer.Start();
            TranslatorStateChanged?.Invoke();
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
                Log.Warn($"Translation failed: {ex.Message}");
                App.Notify("Translation failed", ex.Message);
            }
            catch (Exception ex)
            {
                // e.g. CopyFromScreen fails while the UAC / lock screen is up
                Log.Error("Frame failed", ex);
            }
            finally
            {
                _isProcessing = false;
            }
        }

        private async Task ProcessFrameAsync()
        {
            var timer = Stopwatch.StartNew();
            int version = _settingsVersion;
            var region = _selectedRegion;
            var dpi = VisualTreeHelper.GetDpi(this);

            var pixels = ToScreenPixels(region, dpi);
            int x = pixels.X, y = pixels.Y, width = pixels.Width, height = pixels.Height;
            if (width <= 0 || height <= 0) return;

            bool mergeLines = _config.MergeLines, verticalText = _config.VerticalText;
            var blocks = await Task.Run(() => CaptureAndRecognizeAsync(x, y, width, height, mergeLines, verticalText));
            if (blocks == null) return; // unchanged, or still typing out

            if (version != _settingsVersion)
            {
                ResetFrame(); // settings changed mid-frame; redo it next tick
                return;
            }

            // Animated scenes get re-read every few seconds; skip the work if the text is the same
            var signature = string.Join("\n", blocks.Select(b => $"{b.Text}@{(int)b.Bounds.X / 8},{(int)b.Bounds.Y / 8}"));
            if (signature == _drawnSignature) return;

            if (blocks.Count == 0)
            {
                TranslationCanvas.Children.Clear(); // text is gone, so drop stale overlays
                _drawn = null;
                _drawnSignature = signature;
                return;
            }

            var translations = await _translator.TranslateAsync(blocks.Select(b => b.Text).ToList());
            if (version != _settingsVersion) return;

            // Swap all overlays at once so they don't flicker in line by line
            _drawn = (blocks, translations, region, dpi);
            RedrawLabels();
            Log.Info($"Translated {blocks.Count} block(s) in {timer.ElapsedMilliseconds} ms");
            _drawnSignature = signature;
        }

        private void RedrawLabels()
        {
            if (_drawn is not { } d) return;

            if (_config.SubtitleMode)
                TranslationLayout.DrawSubtitles(TranslationCanvas, d.Blocks, d.Translations, d.Region,
                    new Rect(0, 0, ActualWidth, ActualHeight), d.Dpi, _overlayBrush, _config.TextScale);
            else
                TranslationLayout.Draw(TranslationCanvas, d.Blocks, d.Translations, d.Region, d.Dpi, _overlayBrush, _config.TextScale);
        }

        private async Task<IReadOnlyList<OcrBlock>?> CaptureAndRecognizeAsync(int x, int y, int width, int height,
                                                                              bool mergeLines, bool verticalText)
        {
            using var bitmap = ScreenCapture.CaptureRegion(x, y, width, height);
            var pixels = ScreenCapture.BitmapToBytes(bitmap);

            if (!_frameGate.ShouldProcess(pixels, DateTime.UtcNow))
                return null;

            return await _ocr.RecognizeAsync(bitmap, mergeLines, verticalText);
        }

        private void ResetFrame()
        {
            _settingsVersion++;
            _frameGate.Reset();
            _drawnSignature = null;
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
            TranslatorStateChanged?.Invoke();
        }

        public void SetLanguage(int index)
        {
            var language = Languages.All[index];
            ApplyLanguage(language);
            _config.SourceLanguage = language.OcrTag;
            _config.Save();
            ShowStatus($"{language.Name} → {_config.TargetLanguage}");
            TranslatorStateChanged?.Invoke();
        }

        public void SetTargetLanguage(string code)
        {
            if (code == _config.TargetLanguage) return;

            _config.TargetLanguage = code;
            _config.Save();
            _translator.SetLanguages(_ocr.CurrentLanguage, code);
            ResetFrame();
            TranslatorStateChanged?.Invoke();
        }

        public void ApplyGlossary()
        {
            _translator.SetGlossary(_config.Glossary);
            ResetFrame();
        }

        public void SetSubtitleMode(bool subtitles)
        {
            _config.SubtitleMode = subtitles;
            _config.Save();
            Log.Info($"Subtitle mode: {subtitles}");
            RedrawLabels();
        }

        public void SetVerticalText(bool vertical)
        {
            _config.VerticalText = vertical;
            _config.Save();
            Log.Info($"Vertical text: {vertical}");
            ResetFrame();
        }

        public void SetMergeLines(bool merge)
        {
            _config.MergeLines = merge;
            _config.Save();
            ResetFrame();
        }

        public void SetCaptureInterval(int milliseconds)
        {
            _config.CaptureIntervalMs = milliseconds;
            _captureTimer.Interval = TimeSpan.FromMilliseconds(milliseconds);
        }

        private void ApplyLanguage(SourceLanguage language)
        {
            if (!_ocr.SetLanguage(language))
            {
                App.Notify($"{language.Name} OCR not installed",
                    $"Add {language.Name} in Settings › Time & language › Language & region, including its optical character recognition feature.");
            }

            _translator.SetLanguages(language, _config.TargetLanguage);
            ResetFrame();
        }

        public void SetTranslationService(TranslationService service)
        {
            if (service == _config.ActiveService) return;

            _config.Service = service;
            _config.Save();
            Log.Info($"Translation service: {service}");
            RefreshTranslationService();
            TranslatorStateChanged?.Invoke();
        }

        /// <summary>Re-applies the chosen service and its key, e.g. after the user saves a new key.</summary>
        public void RefreshTranslationService()
        {
            var service = _config.ActiveService;
            _translator.Configure(service, _config.GetApiKey(service));
            ResetFrame();
            _ = _translator.RefreshUsageAsync();
        }

        /// <summary>100 → 75 → 50 → 25 → 0 % (text only) → 100 %.</summary>
        public void CycleOpacity()
        {
            double current = _overlayBrush.Opacity;
            double opacity = current <= 0.001 ? 1.0 : Math.Max(0, current - 0.25);

            SetOpacity(opacity);
            _config.Save();
            ShowStatus($"Background opacity {opacity:P0}");
            TranslatorStateChanged?.Invoke();
        }

        /// <summary>
        /// Scales the translation text (1.0 = automatic size, matched to the original text). Labels on
        /// screen re-lay out straight away. <paramref name="save"/> is false for slider drags, which save on close.
        /// </summary>
        public void SetTextScale(double scale, bool save = true)
        {
            _config.TextScale = Math.Clamp(Math.Round(scale, 2), TranslationLayout.MinTextScale, TranslationLayout.MaxTextScale);
            RedrawLabels();
            if (!save) return;

            _config.Save();
            ShowStatus($"Text size {_config.TextScale:P0}");
            TranslatorStateChanged?.Invoke();
        }

        /// <summary>
        /// Fades only the dark background behind the translations; the text itself stays solid.
        /// Applies immediately; the caller decides when to save, so slider drags don't hit the disk.
        /// </summary>
        public void SetOpacity(double opacity)
        {
            // Every label shares this brush, so existing labels update too
            _overlayBrush.Opacity = opacity;
            _config.OverlayOpacity = opacity;
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
