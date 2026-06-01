using MidiMute.Models;
using NAudio.CoreAudioApi;
using System.Diagnostics;
using System.Drawing;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace MidiMute.Services
{
    public class AudioService
    {
        private readonly MMDeviceEnumerator _enumerator = new();

        public List<AppSession> GetActiveSessions()
        {
            var result = new List<AppSession>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var devices = _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);

            foreach (var device in devices)
            {
                var sessions = device.AudioSessionManager.Sessions;
                for (int i = 0; i < sessions.Count; i++)
                {
                    var session = sessions[i];
                    try
                    {
                        uint pid = session.GetProcessID;
                        if (pid == 0) continue;

                        var process = Process.GetProcessById((int)pid);

                        if (process.ProcessName == "steam" ||
                            process.ProcessName == "Idle") continue;

                        if (!seen.Add(process.ProcessName)) continue;

                        var executablePath = GetExecutablePath(process);

                        string displayName = device.FriendlyName.Contains("Elgato Virtual Audio")
                            ? device.FriendlyName.Replace("(Elgato Virtual Audio)", "").Trim()
                            : process.ProcessName;

                        result.Add(new AppSession
                        {
                            ProcessName = process.ProcessName,
                            DisplayName = displayName,
                            Pid = process.Id,
                            IsMuted = session.SimpleAudioVolume.Mute,
                            Volume = session.SimpleAudioVolume.Volume * 100f,
                            DeviceName = device.FriendlyName,
                            ExecutablePath = executablePath,
                            Icon = GetIconFromPath(executablePath)
                        });
                    }
                    catch { }
                }
            }

            // Добавляем системный мастер-канал
            try
            {
                var device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                result.Insert(0, new AppSession
                {
                    ProcessName = "__master__",
                    DisplayName = LocalizationManager.Text("Main.MasterDisplayName"),
                    Pid = 0,
                    IsMuted = device.AudioEndpointVolume.Mute,
                    Volume = device.AudioEndpointVolume.MasterVolumeLevelScalar * 100f,
                    DeviceName = device.FriendlyName,
                    Icon = null
                });
            }
            catch (Exception ex)
            {
                DiagnosticLog.Error("Audio", "Failed to read master audio endpoint.", ex);
            }

            return result;
        }

        private static string? GetExecutablePath(Process process)
        {
            try
            {
                return process.MainModule?.FileName;
            }
            catch
            {
                return null;
            }
        }

        public static BitmapSource? GetIconFromPath(string? path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
                    return null;

                var icon = Icon.ExtractAssociatedIcon(path);
                if (icon == null) return null;

                var bitmapSource = Imaging.CreateBitmapSourceFromHIcon(
                    icon.Handle,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                bitmapSource.Freeze();

                return bitmapSource;
            }
            catch { return null; }
        }

        public void ToggleMute(string processName)
        {
            var sessions = FindSessions(processName);
            if (sessions.Count == 0) return;

            var mute = sessions.Any(session => !session.SimpleAudioVolume.Mute);
            foreach (var session in sessions)
                session.SimpleAudioVolume.Mute = mute;
        }

        public void ChangeVolume(string processName, float deltaPercent)
        {
            foreach (var session in FindSessions(processName))
            {
                float current = session.SimpleAudioVolume.Volume;
                float next = Math.Clamp(current + deltaPercent / 100f, 0f, 1f);
                session.SimpleAudioVolume.Volume = next;
            }
        }

        public bool? GetMute(string processName)
        {
            var sessions = FindSessions(processName);
            return sessions.Count == 0 ? null : sessions.All(session => session.SimpleAudioVolume.Mute);
        }

        public float? GetVolume(string processName)
        {
            var sessions = FindSessions(processName);
            return sessions.Count == 0
                ? null
                : sessions.Average(session => session.SimpleAudioVolume.Volume) * 100f;
        }

        public void SetMute(string processName, bool mute)
        {
            foreach (var session in FindSessions(processName))
                session.SimpleAudioVolume.Mute = mute;
        }

        public void SetVolume(string processName, float percent)
        {
            foreach (var session in FindSessions(processName))
                session.SimpleAudioVolume.Volume = Math.Clamp(percent / 100f, 0f, 1f);
        }

        private List<AudioSessionControl> FindSessions(string processName)
        {
            var result = new List<AudioSessionControl>();
            var devices = _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);

            foreach (var device in devices)
            {
                var sessions = device.AudioSessionManager.Sessions;
                for (int i = 0; i < sessions.Count; i++)
                {
                    try
                    {
                        var process = Process.GetProcessById((int)sessions[i].GetProcessID);
                        if (string.Equals(process.ProcessName, processName, StringComparison.OrdinalIgnoreCase))
                            result.Add(sessions[i]);
                    }
                    catch { }
                }
            }

            return result;
        }

        public bool GetMasterMute()
        {
            var device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            return device.AudioEndpointVolume.Mute;
        }

        public void ToggleMasterMute()
        {
            var device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            device.AudioEndpointVolume.Mute = !device.AudioEndpointVolume.Mute;
        }

        public void SetMasterMute(bool mute)
        {
            var device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            device.AudioEndpointVolume.Mute = mute;
        }

        public float GetMasterVolume()
        {
            var device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            return device.AudioEndpointVolume.MasterVolumeLevelScalar * 100f;
        }

        public void SetMasterVolume(float percent)
        {
            var device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            device.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(percent / 100f, 0f, 1f);
        }

        public void ChangeMasterVolume(float deltaPercent)
        {
            float current = GetMasterVolume();
            SetMasterVolume(current + deltaPercent);
        }
    }
}
