using NAudio.Midi;
using System.Management;

namespace MidiMute.Services
{
    public class MidiService : IDisposable
    {
        private MidiIn? _midiIn;
        private System.Threading.Timer? _reconnectTimer;
        private ManagementEventWatcher? _deviceWatcher;
        private string? _preferredDeviceName;

        public event Action<int, string>? NotePressed;
        public event Action<int, string>? NoteReleased;
        public event Action? Connected;
        public event Action? Disconnected;
        public event Action? DevicesChanged;
        public event Action<string>? Error;

        public string DeviceName { get; private set; } = "";
        public bool IsConnected { get; private set; }

        public IReadOnlyList<string> GetDeviceNames()
        {
            var devices = new List<string>();

            for (int i = 0; i < MidiIn.NumberOfDevices; i++)
                devices.Add(MidiIn.DeviceInfo(i).ProductName);

            return devices;
        }

        public void Connect(int deviceIndex = 0)
        {
            if (MidiIn.NumberOfDevices == 0 || deviceIndex < 0 || deviceIndex >= MidiIn.NumberOfDevices)
            {
                IsConnected = false;
                DeviceName = "";
                StartReconnectTimer();
                return;
            }

            try
            {
                DeviceName = MidiIn.DeviceInfo(deviceIndex).ProductName;
                _preferredDeviceName = DeviceName;

                _midiIn?.Stop();
                _midiIn?.Dispose();

                _midiIn = new MidiIn(deviceIndex);
                _midiIn.MessageReceived += OnMessage;
                _midiIn.Start();

                IsConnected = true;
                Connected?.Invoke();
                StopReconnectTimer();
                StartDeviceWatcher();
            }
            catch (Exception ex)
            {
                IsConnected = false;
                DeviceName = "";
                DiagnosticLog.Error("MIDI", $"Failed to connect MIDI device at index {deviceIndex}.", ex);
                Error?.Invoke(LocalizationManager.Text("Midi.ConnectFailed"));
                StartReconnectTimer();
            }
        }

        public void ConnectToDevice(string? deviceName)
        {
            _preferredDeviceName = deviceName;

            if (string.IsNullOrWhiteSpace(deviceName))
            {
                Connect();
                return;
            }

            var deviceIndex = FindDeviceIndex(deviceName);
            if (deviceIndex >= 0)
            {
                Connect(deviceIndex);
                return;
            }

            Disconnect();
            StartReconnectTimer();
        }

        private void OnMessage(object? sender, MidiInMessageEventArgs e)
        {
            if (e.MidiEvent is not NoteEvent note)
                return;

            if (note.CommandCode == MidiCommandCode.NoteOff ||
                note.CommandCode == MidiCommandCode.NoteOn && note.Velocity == 0)
            {
                NoteReleased?.Invoke(note.NoteNumber, note.NoteName);
                return;
            }

            if (note is NoteOnEvent noteOn && noteOn.Velocity > 0)
                NotePressed?.Invoke(noteOn.NoteNumber, noteOn.NoteName);
        }

        private void StartDeviceWatcher()
        {
            try
            {
                _deviceWatcher?.Stop();
                _deviceWatcher?.Dispose();

                _deviceWatcher = new ManagementEventWatcher(
                    new WqlEventQuery("SELECT * FROM Win32_DeviceChangeEvent WHERE EventType = 2 OR EventType = 3"));

                _deviceWatcher.EventArrived += (s, e) =>
                {
                    System.Threading.Thread.Sleep(500);
                    DevicesChanged?.Invoke();

                    bool stillExists = false;
                    for (int i = 0; i < MidiIn.NumberOfDevices; i++)
                    {
                        if (MidiIn.DeviceInfo(i).ProductName == DeviceName)
                        {
                            stillExists = true;
                            break;
                        }
                    }

                    if (!stillExists)
                    {
                        Disconnect();
                        Disconnected?.Invoke();
                        StartReconnectTimer();
                    }
                };

                _deviceWatcher.Start();
            }
            catch (Exception ex)
            {
                DiagnosticLog.Error("MIDI", "Failed to start MIDI device watcher.", ex);
                Error?.Invoke(LocalizationManager.Text("Midi.AutoRefreshUnavailable"));
            }
        }

        private void StartReconnectTimer()
        {
            if (_reconnectTimer != null) return;
            _reconnectTimer = new System.Threading.Timer(_ => TryReconnect(), null,
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(2));
        }

        private void StopReconnectTimer()
        {
            _reconnectTimer?.Dispose();
            _reconnectTimer = null;
        }

        private void TryReconnect()
        {
            if (MidiIn.NumberOfDevices == 0) return;

            if (!string.IsNullOrWhiteSpace(_preferredDeviceName))
            {
                var deviceIndex = FindDeviceIndex(_preferredDeviceName);
                if (deviceIndex >= 0)
                    Connect(deviceIndex);

                return;
            }

            Connect(0);
        }

        private static int FindDeviceIndex(string deviceName)
        {
            for (int i = 0; i < MidiIn.NumberOfDevices; i++)
            {
                if (MidiIn.DeviceInfo(i).ProductName == deviceName)
                    return i;
            }

            return -1;
        }

        private void Disconnect()
        {
            IsConnected = false;
            DeviceName = "";

            try { _midiIn?.Stop(); } catch { }
            try { _midiIn?.Dispose(); } catch { }
            _midiIn = null;
        }

        public void Dispose()
        {
            StopReconnectTimer();
            _deviceWatcher?.Stop();
            _deviceWatcher?.Dispose();
            try { _midiIn?.Stop(); } catch { }
            try { _midiIn?.Dispose(); } catch { }
        }
    }
}
