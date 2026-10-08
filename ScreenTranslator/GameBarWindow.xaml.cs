using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace ScreenTranslator
{
    /// <summary>
    /// Optional Xbox Game Bar–style control centre. Hidden until summoned (Ctrl+Shift+G or the tray);
    /// a pinned Translator widget stays on screen, click-through, after the bar closes.
    /// </summary>
    public partial class GameBarWindow : Window
    {
        private static readonly Brush ActiveBrush = new SolidColorBrush(Color.FromRgb(0x6C, 0xCB, 0x5F));
        private static readonly Brush WaitingBrush = new SolidColorBrush(Color.FromRgb(0xFC, 0xE1, 0x00));
        private static readonly Brush IdleBrush = new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E));

        private readonly AppConfig _config;
        private readonly MainWindow _overlay;
        private readonly ObservableCollection<GlossaryEntry> _glossary;
        private readonly List<TargetLanguage> _targets;
        private readonly (string Name, FrameworkElement Widget, ToggleButton Toggle)[] _widgets;
        private readonly DispatcherTimer _clockTimer = new() { Interval = TimeSpan.FromSeconds(10) };
        private IntPtr _hwnd;
        private IntPtr _previousForeground;
        private bool _isOpen;
        private bool _syncing = true; // true until constructed, so control events don't fire into a half-built window
        private FrameworkElement? _dragWidget;
        private Point _dragOffset;

        public GameBarWindow(AppConfig config, MainWindow overlay)
        {
            InitializeComponent();
            _config = config;
            _overlay = overlay;

            Width = SystemParameters.PrimaryScreenWidth;
            Height = SystemParameters.PrimaryScreenHeight;

            _widgets =
            [
                ("Translator", TranslatorWidget, TranslatorToggle),
                ("Glossary", GlossaryWidget, GlossaryToggle),
                ("Settings", SettingsWidget, SettingsToggle),
            ];

            SourceLanguageBox.ItemsSource = Languages.All;
            _targets = Languages.Targets.ToList();
            if (!_targets.Any(t => t.Code.Equals(config.TargetLanguage, StringComparison.OrdinalIgnoreCase)))
                _targets.Add(new TargetLanguage(config.TargetLanguage, config.TargetLanguage));
            TargetLanguageBox.ItemsSource = _targets;

            _glossary = new(config.Glossary.Select(e => new GlossaryEntry { Source = e.Source, Target = e.Target }));
            _glossary.CollectionChanged += (_, _) => UpdateGlossaryEmpty();
            GlossaryList.ItemsSource = _glossary;
            UpdateGlossaryEmpty();

            ApiKeyBox.Password = config.DeepLApiKey;
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DEEPL_API_KEY")))
                KeyStatus.Text = "The DEEPL_API_KEY environment variable is set and overrides this key.";

            RestoreLayout();
            RefreshState();
            RefreshUsage();
            _syncing = false;

            _clockTimer.Tick += (_, _) => UpdateClock();
            overlay.TranslatorStateChanged += RefreshState;
            overlay.UsageChanged += RefreshUsage;

            SourceInitialized += (_, _) =>
            {
                _hwnd = new WindowInteropHelper(this).Handle;
                // Like the overlay, never let the bar end up in the OCR'd screenshots
                NativeMethods.SetWindowDisplayAffinity(_hwnd, NativeMethods.WDA_EXCLUDEFROMCAPTURE);
            };
        }

        #region Open / close / pin

        public void Toggle()
        {
            if (_isOpen) CloseBar();
            else OpenBar();
        }

        public void OpenBar()
        {
            if (_isOpen) return;

            _previousForeground = NativeMethods.GetForegroundWindow();
            _isOpen = true;
            ApplyMode();
            Activate();

            UpdateClock();
            _clockTimer.Start();
            RefreshState();
            _ = _overlay.RefreshUsageAsync();
        }

        public void CloseBar()
        {
            if (!_isOpen) return;

            _isOpen = false;
            _clockTimer.Stop();
            SaveGlossary();
            SaveLayout();
            ApplyMode();

            // Hand focus back to the game or app that was in front before the bar opened
            if (_previousForeground != IntPtr.Zero)
                NativeMethods.SetForegroundWindow(_previousForeground);
        }

        /// <summary>
        /// Open: backdrop, home bar and every toggled widget, interactive.
        /// Closed: only a pinned Translator widget, click-through — or nothing at all.
        /// </summary>
        public void ApplyMode()
        {
            bool showPinned = TranslatorPinToggle.IsChecked == true && TranslatorToggle.IsChecked == true;
            var chrome = _isOpen ? Visibility.Visible : Visibility.Collapsed;
            Backdrop.Visibility = chrome;
            HomeBar.Visibility = chrome;

            foreach (var (_, widget, toggle) in _widgets)
            {
                bool visible = toggle.IsChecked == true && (_isOpen || (widget == TranslatorWidget && showPinned));
                widget.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            }

            if (_isOpen || showPinned)
            {
                if (!IsVisible) Show();
                NativeMethods.SetClickThrough(_hwnd, !_isOpen);
                TranslatorWidget.Opacity = _isOpen ? 1.0 : 0.85;
            }
            else
            {
                Hide();
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                CloseBar();
                e.Handled = true;
            }
        }

        private void Window_Closing(object? sender, CancelEventArgs e)
        {
            // Alt+F4 just closes the bar; on app shutdown WPF ignores the cancel
            e.Cancel = true;
            CloseBar();
        }

        private void Backdrop_MouseDown(object sender, MouseButtonEventArgs e) => CloseBar();

        private void CloseBar_Click(object sender, RoutedEventArgs e) => CloseBar();

        private void WidgetToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (!_syncing) ApplyMode();
        }

        private void CloseWidget_Click(object sender, RoutedEventArgs e)
        {
            var widget = WidgetOf(sender);
            _widgets.First(w => w.Widget == widget).Toggle.IsChecked = false;
        }

        private void UpdateClock() => ClockText.Text = DateTime.Now.ToString("t");

        #endregion

        #region Widget layout

        private void Header_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!_isOpen) return;

            _dragWidget = WidgetOf(sender);
            _dragOffset = e.GetPosition(_dragWidget);
            ((UIElement)sender).CaptureMouse();
        }

        private void Header_MouseMove(object sender, MouseEventArgs e)
        {
            if (_dragWidget == null) return;

            var position = e.GetPosition(WidgetLayer);
            PlaceWidget(_dragWidget, position.X - _dragOffset.X, position.Y - _dragOffset.Y);
        }

        private void Header_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_dragWidget == null) return;

            _dragWidget = null;
            ((UIElement)sender).ReleaseMouseCapture();
        }

        /// <summary>Walks up from a control inside a widget to the widget itself.</summary>
        private FrameworkElement WidgetOf(object element)
        {
            var current = (FrameworkElement)element;
            while (current.Parent is FrameworkElement parent && parent != WidgetLayer)
                current = parent;
            return current;
        }

        private void PlaceWidget(FrameworkElement widget, double x, double y)
        {
            // Keep at least the header reachable, even after a resolution change
            Canvas.SetLeft(widget, Math.Clamp(x, 0, Math.Max(0, Width - 120)));
            Canvas.SetTop(widget, Math.Clamp(y, 0, Math.Max(0, Height - 48)));
        }

        private void RestoreLayout()
        {
            var defaults = new Dictionary<string, (double X, double Y, bool Visible)>
            {
                ["Translator"] = (32, 110, true),
                ["Glossary"] = ((Width - 420) / 2, 130, false),
                ["Settings"] = (Width - 32 - 360, 110, false),
            };

            foreach (var (name, widget, toggle) in _widgets)
            {
                if (_config.GameBarWidgets.TryGetValue(name, out var saved))
                {
                    PlaceWidget(widget, saved.X, saved.Y);
                    toggle.IsChecked = saved.Visible;
                }
                else
                {
                    var (x, y, visible) = defaults[name];
                    PlaceWidget(widget, x, y);
                    toggle.IsChecked = visible;
                }
            }

            TranslatorPinToggle.IsChecked =
                _config.GameBarWidgets.TryGetValue("Translator", out var translator) && translator.Pinned;
        }

        private void SaveLayout()
        {
            foreach (var (name, widget, toggle) in _widgets)
            {
                _config.GameBarWidgets[name] = new WidgetState
                {
                    X = Canvas.GetLeft(widget),
                    Y = Canvas.GetTop(widget),
                    Visible = toggle.IsChecked == true,
                    Pinned = widget == TranslatorWidget && TranslatorPinToggle.IsChecked == true
                };
            }
            _config.Save();
        }

        #endregion

        #region Translator widget

        private void RefreshState()
        {
            bool previous = _syncing;
            _syncing = true;
            try
            {
                var (status, brush) =
                    _overlay.IsChoosingRegion ? ("Choose a region to translate", WaitingBrush)
                    : !_overlay.HasRegion ? ("No region selected", IdleBrush)
                    : _overlay.IsPaused ? ("Paused", WaitingBrush)
                    : ("Translating", ActiveBrush);
                StatusText.Text = status;
                StatusDot.Fill = brush;

                PauseButton.IsEnabled = _overlay.HasRegion && !_overlay.IsChoosingRegion;
                PauseButton.Opacity = PauseButton.IsEnabled ? 1.0 : 0.4; // Fluent's disabled look is too subtle here
                PauseIcon.Text = _overlay.IsPaused ? "" : "";
                PauseLabel.Text = _overlay.IsPaused ? "Resume" : "Pause";

                SourceLanguageBox.SelectedItem = _overlay.CurrentLanguage;
                TargetLanguageBox.SelectedItem = _targets.FirstOrDefault(t =>
                    t.Code.Equals(_config.TargetLanguage, StringComparison.OrdinalIgnoreCase));

                OpacitySlider.Value = _config.OverlayOpacity;
                OpacityValue.Text = $"{_config.OverlayOpacity:P0}";
                IntervalSlider.Value = _config.CaptureIntervalMs;
                IntervalValue.Text = $"{_config.CaptureIntervalMs} ms";
                HotkeyCheck.IsChecked = _config.GameBarHotkeyEnabled;
            }
            finally
            {
                _syncing = previous;
            }
        }

        private void RefreshUsage()
        {
            var usage = _overlay.Usage;
            if (usage == null)
            {
                UsageBar.Value = 0;
                UsagePercent.Text = "";
                UsageText.Text = _config.GetApiKey().Length == 0
                    ? "Add your DeepL API key in Settings."
                    : "Checking usage…";
            }
            else if (usage.Unlimited)
            {
                UsageBar.Value = 0;
                UsagePercent.Text = "";
                UsageText.Text = $"{usage.CharacterCount:N0} characters used (no limit)";
            }
            else
            {
                UsageBar.Value = usage.Fraction;
                UsagePercent.Text = $"{usage.Fraction:P0}";
                UsageText.Text = $"{usage.CharacterCount:N0} of {usage.CharacterLimit:N0} characters";
            }
        }

        private void SelectRegion_Click(object sender, RoutedEventArgs e)
        {
            CloseBar();
            _overlay.BeginSelection();
        }

        private void Pause_Click(object sender, RoutedEventArgs e) => _overlay.TogglePause();

        private void SourceLanguage_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!_syncing && SourceLanguageBox.SelectedIndex >= 0)
                _overlay.SetLanguage(SourceLanguageBox.SelectedIndex);
        }

        private void TargetLanguage_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!_syncing && TargetLanguageBox.SelectedItem is TargetLanguage target)
                _overlay.SetTargetLanguage(target.Code);
        }

        private void Opacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_syncing) return;

            _overlay.SetOpacity(e.NewValue);
            OpacityValue.Text = $"{e.NewValue:P0}";
        }

        #endregion

        #region Glossary widget

        private void AddTerm_Click(object sender, RoutedEventArgs e)
        {
            _glossary.Add(new GlossaryEntry());
            GlossaryStatus.Text = "";
        }

        private void RemoveTerm_Click(object sender, RoutedEventArgs e)
        {
            if (((FrameworkElement)sender).DataContext is GlossaryEntry entry)
                _glossary.Remove(entry);
            GlossaryStatus.Text = "";
        }

        private void SaveGlossary_Click(object sender, RoutedEventArgs e)
        {
            int count = SaveGlossary();
            GlossaryStatus.Text = count == 1 ? "Saved 1 term" : $"Saved {count} terms";
        }

        /// <summary>Saves complete rows (blank halves are ignored) and applies them to the translator.</summary>
        private int SaveGlossary()
        {
            var entries = _glossary
                .Where(e => !string.IsNullOrWhiteSpace(e.Source) && !string.IsNullOrWhiteSpace(e.Target))
                .Select(e => new GlossaryEntry { Source = e.Source.Trim(), Target = e.Target.Trim() })
                .ToList();

            bool changed = entries.Count != _config.Glossary.Count || entries.Zip(_config.Glossary)
                .Any(p => p.First.Source != p.Second.Source || p.First.Target != p.Second.Target);
            if (changed)
            {
                _config.Glossary = entries;
                _config.Save();
                _overlay.ApplyGlossary();
            }

            return entries.Count;
        }

        private void UpdateGlossaryEmpty() =>
            GlossaryEmpty.Visibility = _glossary.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        #endregion

        #region Settings widget

        private void SaveKey_Click(object sender, RoutedEventArgs e)
        {
            _config.DeepLApiKey = ApiKeyBox.Password.Trim();
            _config.Save();
            _overlay.RefreshApiKey();
            KeyStatus.Text = _config.DeepLApiKey.Length == 0 ? "Key removed." : "Key saved.";
        }

        private void Interval_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_syncing) return;

            _overlay.SetCaptureInterval((int)e.NewValue);
            IntervalValue.Text = $"{(int)e.NewValue} ms";
        }

        private void HotkeyCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (_syncing) return;

            _config.GameBarHotkeyEnabled = HotkeyCheck.IsChecked == true;
            _config.Save();
            _overlay.UpdateGameBarHotkey();
        }

        #endregion
    }
}
