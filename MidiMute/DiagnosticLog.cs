using System.Diagnostics;
using System.IO;

namespace MidiMute
{
    internal static class DiagnosticLog
    {
        private const long MaxFileSizeBytes = 512 * 1024;

        public static readonly string FilePath = Path.Combine(
            BindingStorage.SettingsDirectory,
            "diagnostic.log");
        private static readonly string PreviousFilePath = Path.Combine(
            BindingStorage.SettingsDirectory,
            "diagnostic.previous.log");

        public static void Error(string area, string message, Exception exception)
        {
            var entry =
                $"{DateTimeOffset.Now:yyyy-MM-ddTHH:mm:ss.fffzzz} [ERROR] [{area}] {message}" +
                $"{Environment.NewLine}{exception}";

            Debug.WriteLine(entry);

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                RotateIfNeeded();
                File.AppendAllText(FilePath, entry + Environment.NewLine + Environment.NewLine);
            }
            catch
            {
                // Diagnostics must never break the app.
            }
        }

        private static void RotateIfNeeded()
        {
            if (!File.Exists(FilePath) || new FileInfo(FilePath).Length < MaxFileSizeBytes)
                return;

            File.Move(FilePath, PreviousFilePath, overwrite: true);
        }
    }
}
