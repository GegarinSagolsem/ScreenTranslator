using System.Windows;
using Hardcodet.Wpf.TaskbarNotification;

namespace ScreenTranslator
{
    public partial class App : Application
    {
        private TaskbarIcon? _trayIcon;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            _trayIcon = (TaskbarIcon)FindResource("TrayIcon");
        }

        private void TrayPause_Click(object sender, RoutedEventArgs e)
        {
            if (MainWindow is MainWindow mw)
                mw.TogglePauseFromTray();
        }

        private void TrayReselect_Click(object sender, RoutedEventArgs e)
        {
            if (MainWindow is MainWindow mw)
                mw.ReselectRegionFromTray();
        }

        private void TrayExit_Click(object sender, RoutedEventArgs e)
        {
            _trayIcon?.Dispose();
            Shutdown();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _trayIcon?.Dispose();
            base.OnExit(e);
        }
    }
}