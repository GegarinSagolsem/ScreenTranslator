using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;

namespace ScreenTranslator
{
    public partial class ApiKeyWindow : Window
    {
        public string ApiKey => KeyBox.Password.Trim();

        public ApiKeyWindow(string currentKey)
        {
            InitializeComponent();
            KeyBox.Password = currentKey;
            Loaded += (_, _) => KeyBox.Focus();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (ApiKey.Length == 0)
            {
                ErrorText.Text = "Paste a key first, or press Cancel.";
                ErrorText.Visibility = Visibility.Visible;
                return;
            }

            DialogResult = true;
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }
    }
}
