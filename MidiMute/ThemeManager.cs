using Microsoft.Win32;
using System.Windows;

namespace MidiMute
{
    public enum AppThemeMode
    {
        Auto,
        Dark,
        Light
    }

    public static class ThemeManager
    {
        private const string DarkThemePath = "Themes/DarkTheme.xaml";
        private const string LightThemePath = "Themes/LightTheme.xaml";

        private static bool _isWatchingSystemTheme;

        public static AppThemeMode Mode { get; private set; } = AppThemeMode.Auto;

        public static void SetMode(AppThemeMode mode)
        {
            EnsureSystemThemeWatcher();

            Mode = mode;
            ApplyTheme(GetEffectiveTheme(mode));
        }

        private static void EnsureSystemThemeWatcher()
        {
            if (_isWatchingSystemTheme)
                return;

            SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
            _isWatchingSystemTheme = true;
        }

        private static void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (Mode != AppThemeMode.Auto)
                return;

            if (e.Category is not (UserPreferenceCategory.Color or UserPreferenceCategory.General or UserPreferenceCategory.VisualStyle))
                return;

            var application = Application.Current;
            if (application == null)
                return;

            application.Dispatcher.Invoke(() => ApplyTheme(GetEffectiveTheme(Mode)));
        }

        private static AppThemeMode GetEffectiveTheme(AppThemeMode mode)
        {
            if (mode != AppThemeMode.Auto)
                return mode;

            return SystemUsesLightAppsTheme() ? AppThemeMode.Light : AppThemeMode.Dark;
        }

        private static bool SystemUsesLightAppsTheme()
        {
            try
            {
                var value = Registry.GetValue(
                    @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                    "AppsUseLightTheme",
                    0);

                return value is int intValue && intValue > 0;
            }
            catch
            {
                return false;
            }
        }

        private static void ApplyTheme(AppThemeMode effectiveTheme)
        {
            var themePath = effectiveTheme == AppThemeMode.Light ? LightThemePath : DarkThemePath;
            var source = new Uri($"pack://application:,,,/MidiMute;component/{themePath}", UriKind.Absolute);

            var dictionaries = Application.Current.Resources.MergedDictionaries;
            var existingTheme = dictionaries.FirstOrDefault(dictionary =>
                dictionary.Source?.OriginalString.EndsWith(DarkThemePath, StringComparison.OrdinalIgnoreCase) == true ||
                dictionary.Source?.OriginalString.EndsWith(LightThemePath, StringComparison.OrdinalIgnoreCase) == true);

            var themeDictionary = new ResourceDictionary { Source = source };

            if (existingTheme == null)
            {
                dictionaries.Insert(0, themeDictionary);
                return;
            }

            var index = dictionaries.IndexOf(existingTheme);
            dictionaries[index] = themeDictionary;
        }
    }
}
