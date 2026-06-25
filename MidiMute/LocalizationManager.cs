using System.Globalization;
using System.Windows;

namespace MidiMute
{
    public enum AppLanguageMode
    {
        Auto,
        Russian,
        English
    }

    public static class LocalizationManager
    {
        private const string RussianStringsPath = "Localization/Strings.ru.xaml";
        private const string EnglishStringsPath = "Localization/Strings.en.xaml";

        public static AppLanguageMode Mode { get; private set; } = AppLanguageMode.Auto;

        public static void SetMode(AppLanguageMode mode)
        {
            Mode = mode;
            ApplyLanguage(GetEffectiveLanguage(mode));
        }

        public static string Text(string key)
        {
            return Application.Current.TryFindResource(key) as string ?? key;
        }

        public static string Format(string key, params object[] args)
        {
            return string.Format(CultureInfo.CurrentUICulture, Text(key), args);
        }

        private static AppLanguageMode GetEffectiveLanguage(AppLanguageMode mode)
        {
            if (mode != AppLanguageMode.Auto)
                return mode;

            return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("ru", StringComparison.OrdinalIgnoreCase)
                ? AppLanguageMode.Russian
                : AppLanguageMode.English;
        }

        private static void ApplyLanguage(AppLanguageMode effectiveLanguage)
        {
            var languagePath = effectiveLanguage == AppLanguageMode.Russian ? RussianStringsPath : EnglishStringsPath;
            var source = new Uri($"pack://application:,,,/MidiMute;component/{languagePath}", UriKind.Absolute);

            var dictionaries = Application.Current.Resources.MergedDictionaries;
            var existingLanguage = dictionaries.FirstOrDefault(dictionary =>
                dictionary.Source?.OriginalString.EndsWith(RussianStringsPath, StringComparison.OrdinalIgnoreCase) == true ||
                dictionary.Source?.OriginalString.EndsWith(EnglishStringsPath, StringComparison.OrdinalIgnoreCase) == true);

            var languageDictionary = new ResourceDictionary { Source = source };

            if (existingLanguage == null)
            {
                dictionaries.Insert(0, languageDictionary);
                return;
            }

            var index = dictionaries.IndexOf(existingLanguage);
            dictionaries[index] = languageDictionary;
        }
    }
}
