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
        private string? _lastNotice;
        private DateTime _lastNoticeAt;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // A second copy would fail to register the hotkeys and stack another overlay
            _instanceMutex = new Mutex(true, @"Local\ScreenTranslator", out bool isFirstInstance);
            if (!isFirstInstance)
            {
                MessageBox.Show("ScreenTranslator is already running. Look for its icon in the system tray.",
                    "ScreenTranslator", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }

            _config = AppConfig.Load();
            _trayIcon = (TaskbarIcon)FindResource("TrayIcon");
            AddLanguageMenu();

            if (_config.GetApiKey().Length == 0)
                PromptForApiKey();

            _overlay = new MainWindow(_config);
            MainWindow = _overlay;
            _overlay.Closed += (_, _) => Shutdown();
            _overlay.Show();
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

        private void AddLanguageMenu()
        {
            var menu = new MenuItem { Header = "Source language" };
            for (int i = 0; i < Languages.All.Length; i++)
            {
                int index = i;
                var item = new MenuItem { Header = Languages.All[i].Name, InputGestureText = $"Ctrl+Shift+{i + 1}" };
                item.Click += (_, _) => _overlay?.SetLanguage(index);
                menu.Items.Add(item);
            }

            menu.SubmenuOpened += (_, _) =>
            {
                for (int i = 0; i < menu.Items.Count; i++)
                    ((MenuItem)menu.Items[i]).IsChecked = _overlay?.CurrentLanguage == Languages.All[i];
            };

            _trayIcon!.ContextMenu.Items.Insert(3, menu);
        }

        private void PromptForApiKey()
        {
            var dialog = new ApiKeyWindow(_config.DeepLApiKey);
            if (dialog.ShowDialog() != true) return;

            _config.DeepLApiKey = dialog.ApiKey;
            _config.Save();
            _overlay?.RefreshApiKey();
        }

        private void TrayReselect_Click(object sender, RoutedEventArgs e) => _overlay?.BeginSelection();

        private void TrayPause_Click(object sender, RoutedEventArgs e) => _overlay?.TogglePause();

        private void TrayOpacity_Click(object sender, RoutedEventArgs e) => _overlay?.CycleOpacity();

        private void TrayApiKey_Click(object sender, RoutedEventArgs e) => PromptForApiKey();

        private void TrayExit_Click(object sender, RoutedEventArgs e) => Shutdown();

        protected override void OnExit(ExitEventArgs e)
        {
            _trayIcon?.Dispose();
            _instanceMutex?.Dispose();
            base.OnExit(e);
        }
    }
}
