using System.IO;
using System.Text.Json;
using MidiMute.Models;

namespace MidiMute
{
    public class BindingStorage
    {
        public static readonly string SettingsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MidiMute");

        public static readonly string FilePath = Path.Combine(SettingsDirectory, "bindings.json");

        public static readonly string BackupDirectory = Path.Combine(SettingsDirectory, "backups");

        public void Save(
            IEnumerable<AppSession> sessions,
            bool bypassEnabled,
            string? midiDeviceName,
            IEnumerable<string> hiddenProcessNames,
            IEnumerable<SavedAppProfile> appProfiles,
            AppThemeMode themeMode,
            AppLanguageMode languageMode)
        {
            Directory.CreateDirectory(SettingsDirectory);
            SaveToFile(FilePath, CreateSavedData(
                sessions,
                bypassEnabled,
                midiDeviceName,
                hiddenProcessNames,
                appProfiles,
                themeMode,
                languageMode));
        }

        public SavedData Load()
        {
            if (!File.Exists(FilePath)) return new();
            try
            {
                return LoadFromFile(FilePath);
            }
            catch (Exception ex)
            {
                DiagnosticLog.Error("Settings", "Failed to load saved bindings.", ex);
                return new();
            }
        }

        public void Export(
            string filePath,
            IEnumerable<AppSession> sessions,
            bool bypassEnabled,
            string? midiDeviceName,
            IEnumerable<string> hiddenProcessNames,
            IEnumerable<SavedAppProfile> appProfiles,
            AppThemeMode themeMode,
            AppLanguageMode languageMode)
        {
            SaveToFile(filePath, CreateSavedData(
                sessions,
                bypassEnabled,
                midiDeviceName,
                hiddenProcessNames,
                appProfiles,
                themeMode,
                languageMode));
        }

        public SavedData Import(string filePath)
        {
            return LoadFromFile(filePath);
        }

        public string? BackupCurrentSettings()
        {
            if (!File.Exists(FilePath))
                return null;

            Directory.CreateDirectory(BackupDirectory);

            var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var backupPath = Path.Combine(BackupDirectory, $"bindings-{timestamp}.json");
            var counter = 1;

            while (File.Exists(backupPath))
            {
                backupPath = Path.Combine(BackupDirectory, $"bindings-{timestamp}-{counter}.json");
                counter++;
            }

            File.Copy(FilePath, backupPath, overwrite: false);
            return backupPath;
        }

        private static SavedData CreateSavedData(
            IEnumerable<AppSession> sessions,
            bool bypassEnabled,
            string? midiDeviceName,
            IEnumerable<string> hiddenProcessNames,
            IEnumerable<SavedAppProfile> appProfiles,
            AppThemeMode themeMode,
            AppLanguageMode languageMode)
        {
            var profilesByProcess = appProfiles
                .Concat(sessions
                    .Where(s => !string.IsNullOrWhiteSpace(s.ProcessName))
                    .Select(s => new SavedAppProfile
                    {
                        ProcessName = s.ProcessName,
                        DisplayName = s.DisplayName,
                        ExecutablePath = s.ExecutablePath
                    }))
                .GroupBy(profile => profile.ProcessName, StringComparer.OrdinalIgnoreCase)
                .Select(group => group
                    .OrderByDescending(profile => !string.IsNullOrWhiteSpace(profile.ExecutablePath))
                    .ThenByDescending(profile => !string.IsNullOrWhiteSpace(profile.DisplayName))
                    .First())
                .OrderBy(profile => profile.ProcessName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new SavedData
            {
                Sessions = sessions
                    .Where(s => s.Bindings.Count > 0)
                    .Select(s => new SavedSession
                    {
                        ProcessName = s.ProcessName,
                        DisplayName = s.DisplayName,
                        ExecutablePath = s.ExecutablePath,
                        Bindings = s.Bindings
                    }).ToList(),
                BypassEnabled = bypassEnabled,
                MidiDeviceName = midiDeviceName,
                AppThemeMode = themeMode,
                AppLanguageMode = languageMode,
                HiddenProcessNames = hiddenProcessNames
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                AppProfiles = profilesByProcess
            };
        }

        private static void SaveToFile(string filePath, SavedData data)
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filePath, json);
        }

        private static SavedData LoadFromFile(string filePath)
        {
            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<SavedData>(json) ?? new();
        }
    }

    public class SavedSession
    {
        public string ProcessName { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string? ExecutablePath { get; set; }
        public List<MidiBinding> Bindings { get; set; } = new();
    }

    public class SavedAppProfile
    {
        public string ProcessName { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string? ExecutablePath { get; set; }
    }

    public class SavedData
    {
        public List<SavedSession> Sessions { get; set; } = new();
        public bool BypassEnabled { get; set; }
        public string? MidiDeviceName { get; set; }
        public AppThemeMode AppThemeMode { get; set; } = AppThemeMode.Auto;
        public AppLanguageMode AppLanguageMode { get; set; } = AppLanguageMode.Auto;
        public List<string> HiddenProcessNames { get; set; } = new();
        public List<SavedAppProfile> AppProfiles { get; set; } = new();
    }
}

