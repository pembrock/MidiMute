using Hardcodet.Wpf.TaskbarNotification;
using System.Windows;
using System.Windows.Controls;

namespace MidiMute
{
    public partial class App : Application
    {
        public static TaskbarIcon? TrayIcon { get; private set; }
        public static MainWindow? MainWin { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var startInTray = e.Args.Any(arg =>
                string.Equals(arg, AutoStartManager.StartupArgument, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "--minimized", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "/tray", StringComparison.OrdinalIgnoreCase));

            var startupSettings = new BindingStorage().Load();
            ThemeManager.SetMode(startupSettings.AppThemeMode);
            LocalizationManager.SetMode(startupSettings.AppLanguageMode);
            AutoStartManager.NormalizeCurrentEntry();

            TrayIcon = (TaskbarIcon)FindResource("TrayIcon");
            TrayIcon.TrayMouseDoubleClick += (s, e) =>
            {
                MainWin?.Show();
                MainWin?.Activate();
                if (MainWin != null)
                    MainWin.WindowState = WindowState.Normal;
            };
            TrayIcon.ContextMenu.Opened += (s, e) =>
            {
                if (MainWin == null) return;
                UpdateTrayMenu(MainWin.MidiConnected, MainWin.MidiDeviceName, MainWin.BypassEnabled);
            };
            MainWin = new MainWindow();
            if (!startInTray)
                MainWin.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            TrayIcon?.Dispose();
            base.OnExit(e);
        }

        public static void UpdateTrayMenu(bool midiConnected, string midiDevice, bool bypass)
        {
            if (TrayIcon?.ContextMenu == null) return;

            var items = TrayIcon.ContextMenu.Items;

            if (items[0] is MenuItem midiItem)
            {
                midiItem.Header = midiConnected
                    ? LocalizationManager.Format("Tray.MidiConnectedFormat", midiDevice)
                    : LocalizationManager.Text("Main.MidiNotFound");
                midiItem.Foreground = midiConnected
                    ? new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(29, 158, 117))
                    : new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(226, 75, 74));
            }

            if (items[4] is MenuItem bypassItem)
            {
                bypassItem.Header = bypass
                    ? LocalizationManager.Text("Tray.BypassOn")
                    : LocalizationManager.Text("Tray.BypassOff");
                bypassItem.Foreground = bypass
                    ? new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(226, 160, 74))
                    : System.Windows.SystemColors.MenuTextBrush;
            }
        }
    }
}
