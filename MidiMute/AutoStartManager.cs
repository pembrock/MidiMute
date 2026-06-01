using Microsoft.Win32;
using System.Diagnostics;
using System.IO;

namespace MidiMute
{
    public static class AutoStartManager
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "MidiMute";
        public const string StartupArgument = "--tray";

        public static bool IsEnabledForCurrentExecutable()
        {
            var configuredPath = GetConfiguredExecutablePath();
            if (string.IsNullOrWhiteSpace(configuredPath))
                return false;

            var currentPath = GetCurrentExecutablePath();
            return string.Equals(configuredPath, currentPath, StringComparison.OrdinalIgnoreCase);
        }

        public static void Enable()
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            key?.SetValue(ValueName, CreateStartupCommand(GetCurrentExecutablePath()), RegistryValueKind.String);
        }

        public static void Disable()
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }

        public static void RemoveStaleEntry()
        {
            var configuredPath = GetConfiguredExecutablePath();
            if (string.IsNullOrWhiteSpace(configuredPath))
                return;

            if (File.Exists(configuredPath) &&
                string.Equals(configuredPath, GetCurrentExecutablePath(), StringComparison.OrdinalIgnoreCase))
                return;

            Disable();
        }

        public static void NormalizeCurrentEntry()
        {
            var configuredPath = GetConfiguredExecutablePath();
            if (string.IsNullOrWhiteSpace(configuredPath))
                return;

            if (!string.Equals(configuredPath, GetCurrentExecutablePath(), StringComparison.OrdinalIgnoreCase))
                return;

            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            key?.SetValue(ValueName, CreateStartupCommand(configuredPath), RegistryValueKind.String);
        }

        private static string? GetConfiguredExecutablePath()
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            var value = key?.GetValue(ValueName) as string;
            return string.IsNullOrWhiteSpace(value) ? null : ExtractExecutablePath(value);
        }

        private static string GetCurrentExecutablePath()
        {
            return Environment.ProcessPath
                ?? Process.GetCurrentProcess().MainModule?.FileName
                ?? AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static string ExtractExecutablePath(string value)
        {
            var trimmed = value.Trim();
            if (trimmed.Length == 0)
                return trimmed;

            if (trimmed[0] == '"')
            {
                var closingQuoteIndex = trimmed.IndexOf('"', 1);
                return closingQuoteIndex > 1
                    ? trimmed.Substring(1, closingQuoteIndex - 1)
                    : trimmed.Trim('"');
            }

            var exeIndex = trimmed.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            return exeIndex >= 0
                ? trimmed.Substring(0, exeIndex + 4)
                : trimmed;
        }

        private static string Quote(string path)
        {
            return $"\"{path}\"";
        }

        private static string CreateStartupCommand(string path)
        {
            return $"{Quote(path)} {StartupArgument}";
        }
    }
}
