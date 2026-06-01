using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Navigation;

namespace MidiMute
{
    public partial class AboutDialog : Window
    {
        public AboutDialog()
        {
            InitializeComponent();

            VersionLabel.Text = LocalizationManager.Format("About.VersionFormat", GetVersion());
            SettingsPathLabel.Text = BindingStorage.SettingsDirectory;
            LogPathLabel.Text = DiagnosticLog.FilePath;
        }

        private static string GetVersion()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var informationalVersion = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;

            return string.IsNullOrWhiteSpace(informationalVersion)
                ? assembly.GetName().Version?.ToString() ?? LocalizationManager.Text("Common.NotSet")
                : informationalVersion;
        }

        private static void OpenPath(string path)
        {
            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                DiagnosticLog.Error("Settings", $"Failed to open path '{path}'.", ex);
                MessageBox.Show(
                    LocalizationManager.Text("About.OpenPathFailed"),
                    "MidiMute",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void OpenSettingsFolder_Click(object sender, RoutedEventArgs e)
        {
            Directory.CreateDirectory(BindingStorage.SettingsDirectory);
            OpenPath(BindingStorage.SettingsDirectory);
        }

        private void OpenLog_Click(object sender, RoutedEventArgs e)
        {
            Directory.CreateDirectory(BindingStorage.SettingsDirectory);
            if (!File.Exists(DiagnosticLog.FilePath))
                File.WriteAllText(DiagnosticLog.FilePath, "");

            OpenPath(DiagnosticLog.FilePath);
        }

        private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void GitHubLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            OpenPath(e.Uri.AbsoluteUri);
            e.Handled = true;
        }
    }
}
