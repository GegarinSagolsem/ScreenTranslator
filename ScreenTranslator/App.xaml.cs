using System.IO;
using System.Windows;
using System.Windows.Controls;
using Hardcodet.Wpf.TaskbarNotification;

namespace ScreenTranslator
{
    public partial class App : Application
    {
        private static readonly TimeSpan NoticeCooldown = TimeSpan.FromMinutes(1);

        private Mutex? _instanceMutex;
        private TaskbarIcon? _trayIcon;
        private AppConfig _config = new();
        private MainWindow? _overlay;
        private GameBarWindow? _gameBar;
        private string? _lastNotice;
        private DateTime _lastNoticeAt;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Older Windows can't hide the overlay from screen capture, so the app would OCR its own labels
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
            {
                MessageBox.Show("ScreenTranslator needs Windows 11, or Windows 10 version 2004 (May 2020 Update) or newer.",
                    "ScreenTranslator", MessageBoxButton.OK, MessageBoxImage.Warning);
                Shutdown();
                return;
            }

            // A second copy would fail to register the hotkeys and stack another overlay
            _instanceMutex = new Mutex(true, @"Local\ScreenTranslator", out bool isFirstInstance);
            if (!isFirstInstance)
            {
                MessageBox.Show("ScreenTranslator is already running. Look for its icon in the system tray.",
                    "ScreenTranslator", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }

            DispatcherUnhandledException += (_, args) => Log.Error("Unhandled UI exception", args.Exception);
            AppDomain.CurrentDomain.UnhandledException += (_, args) => Log.Error("Unhandled exception", args.ExceptionObject as Exception);
            TaskScheduler.UnobservedTaskException += (_, args) => Log.Error("Unobserved task exception", args.Exception);

            var version = typeof(App).Assembly.GetName().Version;
            var ocrLanguages = string.Join(", ", global::Windows.Media.Ocr.OcrEngine.AvailableRecognizerLanguages.Select(l => l.LanguageTag));
            Log.Info($"ScreenTranslator {version} starting on {Environment.OSVersion}, .NET {Environment.Version}; OCR packs: {ocrLanguages}");

            _config = AppConfig.Load();
            _trayIcon = (TaskbarIcon)FindResource("TrayIcon");
            AddLanguageMenu();
            _trayIcon.ContextMenu.Opened += (_, _) => ShowHotkeysInMenu(_trayIcon.ContextMenu.Items);

            Log.Info($"Translation service: {_config.ActiveService}");

            _overlay = new MainWindow(_config);
            MainWindow = _overlay;
            _overlay.Closed += (_, _) => Shutdown();
            _overlay.GameBarRequested += () => _gameBar?.Toggle();
            _overlay.UsageChanged += UpdateTrayToolTip;
            _overlay.TranslatorStateChanged += UpdateTrayToolTip;
            _overlay.Show();
            UpdateTrayToolTip();

            _gameBar = new GameBarWindow(_config, _overlay);
            _gameBar.ApplyMode(); // brings back a widget pinned last session

            _ = _overlay.RefreshUsageAsync();
        }

        /// <summary>Shows a tray balloon; the same message is suppressed for a minute so errors can't spam.</summary>
        public static void Notify(string title, string message)
        {
            if (Current is not App app || app._trayIcon == null) return;
            if (message == app._lastNotice && DateTime.UtcNow - app._lastNoticeAt < NoticeCooldown) return;

            app._lastNotice = message;
            app._lastNoticeAt = DateTime.UtcNow;
            app._trayIcon.ShowBalloonTip(title, message, BalloonIcon.Warning);
        }

        private void UpdateTrayToolTip()
        {
            if (_trayIcon == null || _overlay == null) return;

            var usage = _overlay.Usage;
            _trayIcon.ToolTipText = _overlay.Service switch
            {
                TranslationService.DeepL when usage is { Unlimited: true } => $"ScreenTranslator\nDeepL: {usage.CharacterCount:N0} characters used",
                TranslationService.DeepL when usage != null => $"ScreenTranslator\nDeepL: {usage.CharacterCount:N0} / {usage.CharacterLimit:N0} characters ({usage.Fraction:P0})",
                TranslationService.DeepL => "ScreenTranslator\nDeepL",
                TranslationService.GoogleCloud => "ScreenTranslator\nGoogle Cloud Translation",
                _ => "ScreenTranslator\nGoogle Translate (free)"
            };
        }

        /// <summary>Shows each item's current (user-configurable) hotkey next to it.</summary>
        private void ShowHotkeysInMenu(ItemCollection items)
        {
            foreach (var item in items.OfType<MenuItem>())
            {
                if (item.Tag is string id && id != "LanguageMenu")
                    item.InputGestureText = _config.GetHotkey(id).ToString();
                ShowHotkeysInMenu(item.Items);
            }
        }

        private void AddLanguageMenu()
        {
            var menu = _trayIcon!.ContextMenu.Items.OfType<MenuItem>().First(i => "LanguageMenu".Equals(i.Tag));
            for (int i = 0; i < Languages.All.Length; i++)
            {
                int index = i;
                var item = new MenuItem { Header = Languages.All[i].Name, Tag = HotkeyCommands.LanguagePrefix + Languages.All[i].OcrTag };
                item.Click += (_, _) => _overlay?.SetLanguage(index);
                menu.Items.Add(item);
            }

            menu.SubmenuOpened += (_, _) =>
            {
                for (int i = 0; i < menu.Items.Count; i++)
                    ((MenuItem)menu.Items[i]).IsChecked = _overlay?.CurrentLanguage == Languages.All[i];
            };
        }

        private void TrayGameBar_Click(object sender, RoutedEventArgs e) => _gameBar?.OpenBar();

        private void TrayIcon_DoubleClick(object sender, RoutedEventArgs e) => _gameBar?.OpenBar();

        private void TrayReselect_Click(object sender, RoutedEventArgs e) => _overlay?.BeginSelection();

        private void TraySnip_Click(object sender, RoutedEventArgs e) => _overlay?.BeginSnip();

        private void TrayPause_Click(object sender, RoutedEventArgs e) => _overlay?.TogglePause();

        private void TrayOpacity_Click(object sender, RoutedEventArgs e) => _overlay?.CycleOpacity();

        private void TrayTextLarger_Click(object sender, RoutedEventArgs e) => _overlay?.SetTextScale(_overlay.TextScale + 0.1);

        private void TrayTextSmaller_Click(object sender, RoutedEventArgs e) => _overlay?.SetTextScale(_overlay.TextScale - 0.1);

        private void TrayTextReset_Click(object sender, RoutedEventArgs e) => _overlay?.SetTextScale(1.0);

        private void TrayTranslationSettings_Click(object sender, RoutedEventArgs e) => _gameBar?.OpenBar(showSettings: true);

        private void TrayOpenLog_Click(object sender, RoutedEventArgs e)
        {
            Directory.CreateDirectory(Log.Folder);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Log.Folder) { UseShellExecute = true });
        }

        private void TrayExit_Click(object sender, RoutedEventArgs e) => Shutdown();

        protected override void OnExit(ExitEventArgs e)
        {
            Log.Info("Exiting");
            _trayIcon?.Dispose();
            _instanceMutex?.Dispose();
            base.OnExit(e);
        }
    }
}
