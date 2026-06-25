using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using MidiMute.Models;
using MidiMute.Services;

namespace MidiMute
{
    public partial class MainWindow : Window
    {
        private readonly AudioService _audio = new();
        private readonly AudioDeviceRestartService _audioDeviceRestart = new();
        private readonly MidiService _midi = new();
        private readonly DispatcherTimer _audioSessionRefreshTimer = new();
        private readonly ObservableCollection<AppSession> _sessions = new();
        private AppSession? _selected;
        private readonly BindingStorage _storage = new();
        private readonly Dictionary<int, DateTime> _lastMuteToggleByNote = new();
        private readonly Dictionary<(int NoteNumber, string ProcessName), bool> _heldMuteStates = new();
        private readonly Dictionary<(int NoteNumber, string ProcessName), float> _heldVolumeStates = new();
        private readonly Dictionary<AppSession, DispatcherTimer> _sessionHighlightTimers = new();
        private readonly Dictionary<MidiBinding, DispatcherTimer> _bindingHighlightTimers = new();
        private readonly Dictionary<string, SavedAppProfile> _appProfiles = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _hiddenProcessNames = new(StringComparer.OrdinalIgnoreCase);
        private bool _bypassEnabled = false;
        private bool _editingAppList;
        private AppThemeMode _themeMode = AppThemeMode.Auto;
        private AppLanguageMode _languageMode = AppLanguageMode.Auto;
        private bool _updatingMidiDevices;
        private bool _updatingVolumeDisplay;
        private bool _refreshingAudioSessions;
        private bool _allowClose;
        private readonly object _volumeApplyLock = new();
        private bool _volumeApplyWorkerRunning;
        private string? _pendingVolumeProcessName;
        private float _pendingVolumeValue;
        private string _audioSessionSnapshot = "";
        private string? _selectedMidiDeviceName;
        private string? _selectedRestartAudioDeviceInstanceId;
        private bool _updatingRestartAudioDevices;
        private bool _restartingAudioDevice;
        private static readonly TimeSpan MuteToggleDebounce = TimeSpan.FromMilliseconds(200);
        private static readonly TimeSpan AudioSessionRefreshInterval = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan MidiHighlightDuration = TimeSpan.FromMilliseconds(900);
        public bool BypassEnabled => _bypassEnabled;
        public bool MidiConnected => _midi.IsConnected;
        public string MidiDeviceName => _midi.DeviceName;

        public MainWindow()
        {
            var startupSettings = _storage.Load();
            _themeMode = startupSettings.AppThemeMode;
            ThemeManager.SetMode(_themeMode);
            _languageMode = startupSettings.AppLanguageMode;
            LocalizationManager.SetMode(_languageMode);

            InitializeComponent();
            AutoStartManager.RemoveStaleEntry();
            UpdateAutoStartMenuItem();
            AppListBox.ItemsSource = _sessions;
            LoadSessions(startupSettings);
            RefreshRestartAudioDeviceList(startupSettings.RestartAudioDeviceInstanceId);
            ConnectMidi();
            StartAudioSessionAutoRefresh();
            //MessageBox.Show(_audio.GetAllDevicesDebugInfo());
            StateChanged += (s, e) =>
            {
                if (WindowState == WindowState.Minimized)
                {
                    Hide();
                }
            };
        }

        private void LoadSessions(SavedData? importedSettings = null, List<AppSession>? activeSessions = null)
        {
            var selectedProcessName = _selected?.ProcessName;
            var currentBindings = _sessions
                .Where(_ => importedSettings == null)
                .Select(s => new { s.ProcessName, s.Bindings, s.ExecutablePath })
                .ToList();

            var saved = importedSettings ?? _storage.Load();
            _themeMode = saved.AppThemeMode;
            ThemeManager.SetMode(_themeMode);
            UpdateThemeMenuItems();
            _languageMode = saved.AppLanguageMode;
            LocalizationManager.SetMode(_languageMode);
            UpdateLanguageMenuItems();

            var current = activeSessions ?? _audio.GetActiveSessions();
            UpdateMasterSessionDisplayName(current);
            _audioSessionSnapshot = CreateAudioSessionSnapshot(current);
            LoadAppProfiles(saved);
            _hiddenProcessNames.Clear();
            foreach (var processName in saved.HiddenProcessNames.Where(name => name != "__master__"))
                _hiddenProcessNames.Add(processName);
            _selectedMidiDeviceName = saved.MidiDeviceName;
            _selectedRestartAudioDeviceInstanceId = saved.RestartAudioDeviceInstanceId;

            foreach (var session in current)
            {
                UpdateMasterSessionDisplayName(session);
                ApplySessionListState(session);
                RememberAppProfile(session);

                var currentMatch = currentBindings.FirstOrDefault(s => s.ProcessName == session.ProcessName);
                if (currentMatch != null)
                {
                    session.Bindings = currentMatch.Bindings;
                    if (string.IsNullOrWhiteSpace(session.ExecutablePath))
                        session.ExecutablePath = currentMatch.ExecutablePath;
                    continue;
                }

                var match = saved.Sessions.FirstOrDefault(s => s.ProcessName == session.ProcessName);
                var profile = GetAppProfile(session.ProcessName);
                if (match != null)
                {
                    session.Bindings = match.Bindings;
                    if (string.IsNullOrWhiteSpace(session.ExecutablePath))
                        session.ExecutablePath = match.ExecutablePath ?? profile?.ExecutablePath;
                    session.Icon ??= AudioService.GetIconFromPath(session.ExecutablePath);
                }
                else if (profile != null)
                {
                    if (string.IsNullOrWhiteSpace(session.ExecutablePath))
                        session.ExecutablePath = profile.ExecutablePath;
                    session.Icon ??= AudioService.GetIconFromPath(session.ExecutablePath);
                }
            }

            var inactiveSavedSessions = saved.Sessions
                .Where(savedSession => current.All(session => session.ProcessName != savedSession.ProcessName))
                .Select(savedSession =>
                {
                    var profile = GetAppProfile(savedSession.ProcessName);
                    var executablePath = savedSession.ExecutablePath ?? profile?.ExecutablePath;

                    return new AppSession
                    {
                        ProcessName = savedSession.ProcessName,
                        DisplayName = !string.IsNullOrWhiteSpace(savedSession.DisplayName)
                            ? savedSession.DisplayName
                            : profile?.DisplayName ?? savedSession.ProcessName,
                        ExecutablePath = executablePath,
                        Bindings = savedSession.Bindings,
                        Icon = AudioService.GetIconFromPath(executablePath),
                        IsAvailable = false
                    };
                })
                .Select(session =>
                {
                    ApplySessionListState(session);
                    return session;
                });

            _sessions.Clear();
            foreach (var s in current)
                _sessions.Add(s);
            foreach (var s in inactiveSavedSessions)
                _sessions.Add(s);

            SetBypass(saved.BypassEnabled, saveState: false);

            UpdateTotalBindings();
            ApplySessionFilter();
            RestoreSelection(selectedProcessName);
            SaveState();
        }

        private void LoadAppProfiles(SavedData saved)
        {
            _appProfiles.Clear();

            foreach (var profile in saved.AppProfiles)
                RememberAppProfile(profile.ProcessName, profile.DisplayName, profile.ExecutablePath);

            foreach (var session in saved.Sessions)
                RememberAppProfile(session.ProcessName, session.DisplayName, session.ExecutablePath);
        }

        private SavedAppProfile? GetAppProfile(string processName)
        {
            return _appProfiles.TryGetValue(processName, out var profile)
                ? profile
                : null;
        }

        private void RememberAppProfile(AppSession session)
        {
            RememberAppProfile(session.ProcessName, session.DisplayName, session.ExecutablePath);
        }

        private void RememberAppProfile(string processName, string displayName, string? executablePath)
        {
            if (string.IsNullOrWhiteSpace(processName) || IsMasterSession(processName))
                return;

            if (!_appProfiles.TryGetValue(processName, out var profile))
            {
                _appProfiles[processName] = new SavedAppProfile
                {
                    ProcessName = processName,
                    DisplayName = displayName,
                    ExecutablePath = executablePath
                };
                return;
            }

            if (!string.IsNullOrWhiteSpace(displayName))
                profile.DisplayName = displayName;

            if (!string.IsNullOrWhiteSpace(executablePath))
                profile.ExecutablePath = executablePath;
        }

        private void ConnectMidi()
        {
            _midi.Connected += () => Dispatcher.Invoke(() =>
            {
                RefreshMidiDeviceList(_midi.DeviceName);
                MidiStatusLabel.Text = $"MIDI: {_midi.DeviceName}";
                MidiStatusDot.Fill = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(29, 158, 117));
                App.UpdateTrayMenu(true, _midi.DeviceName, _bypassEnabled);
            });

            _midi.Disconnected += () => Dispatcher.Invoke(() =>
            {
                RefreshMidiDeviceList(_selectedMidiDeviceName);
                MidiStatusLabel.Text = LocalizationManager.Text("Main.MidiReconnecting");
                MidiStatusDot.Fill = System.Windows.Media.Brushes.Gray;
                App.UpdateTrayMenu(false, "", _bypassEnabled);
            });

            _midi.DevicesChanged += () => Dispatcher.Invoke(() =>
            {
                RefreshMidiDeviceList(_midi.IsConnected ? _midi.DeviceName : _selectedMidiDeviceName);
            });

            _midi.Error += message => Dispatcher.BeginInvoke(() =>
            {
                MidiStatusLabel.Text = $"MIDI: {message}";
                MidiStatusDot.Fill = System.Windows.Media.Brushes.Gray;
            });

            _midi.NotePressed += OnNotePressed;
            _midi.NoteReleased += OnNoteReleased;
            RefreshMidiDeviceList(_selectedMidiDeviceName);
            _midi.ConnectToDevice(_selectedMidiDeviceName);

            MidiStatusLabel.Text = _midi.IsConnected ? $"MIDI: {_midi.DeviceName}" : LocalizationManager.Text("Main.MidiNotFound");
            if (!_midi.IsConnected)
                MidiStatusDot.Fill = System.Windows.Media.Brushes.Gray;
            App.UpdateTrayMenu(_midi.IsConnected, _midi.DeviceName, _bypassEnabled);
        }

        private void StartAudioSessionAutoRefresh()
        {
            _audioSessionRefreshTimer.Interval = AudioSessionRefreshInterval;
            _audioSessionRefreshTimer.Tick += AudioSessionRefreshTimer_Tick;
            _audioSessionRefreshTimer.Start();
        }

        private void AudioSessionRefreshTimer_Tick(object? sender, EventArgs e)
        {
            if (_refreshingAudioSessions)
                return;

            _refreshingAudioSessions = true;
            try
            {
                var activeSessions = _audio.GetActiveSessions();
                var nextSnapshot = CreateAudioSessionSnapshot(activeSessions);
                if (nextSnapshot == _audioSessionSnapshot)
                    return;

                LoadSessions(activeSessions: activeSessions);
            }
            finally
            {
                _refreshingAudioSessions = false;
            }
        }

        private static string CreateAudioSessionSnapshot(IEnumerable<AppSession> sessions)
        {
            return string.Join(
                "|",
                sessions
                    .Where(session => session.IsAvailable)
                    .Select(session => $"{session.ProcessName}:{session.Pid}:{session.DeviceName}")
                    .OrderBy(session => session, StringComparer.OrdinalIgnoreCase));
        }

        private void RefreshMidiDeviceList(string? selectedDeviceName = null)
        {
            var devices = _midi.GetDeviceNames();
            selectedDeviceName ??= MidiDeviceCombo.SelectedItem as string;
            var hasPreferredDevice = !string.IsNullOrWhiteSpace(selectedDeviceName);

            _updatingMidiDevices = true;
            MidiDeviceCombo.ItemsSource = devices;

            var selectedIndex = selectedDeviceName == null
                ? -1
                : devices.ToList().FindIndex(name => name == selectedDeviceName);

            MidiDeviceCombo.SelectedIndex = selectedIndex >= 0
                ? selectedIndex
                : !hasPreferredDevice && devices.Count > 0 ? 0 : -1;
            _updatingMidiDevices = false;
        }

        private void MidiDeviceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_updatingMidiDevices || MidiDeviceCombo.SelectedIndex < 0)
                return;

            _selectedMidiDeviceName = MidiDeviceCombo.SelectedItem as string;
            _midi.Connect(MidiDeviceCombo.SelectedIndex);
            SaveState();
        }

        private void OnNotePressed(int noteNumber, string noteName)
        {
            if (_bypassEnabled)
            {
                Dispatcher.BeginInvoke(() =>
                {
                    LastKeyLabel.Text = LocalizationManager.Format(
                        "Status.NoteFormat",
                        noteName,
                        noteNumber,
                        LocalizationManager.Text("Status.Bypassed"));
                });
                return;
            }

            var muteToggleAllowed = CanToggleMute(noteNumber);

            Dispatcher.BeginInvoke(() =>
            {
                var matchingBindingFound = false;
                var unavailableBindingFound = false;
                var actionExecuted = false;
                var holdActionActive = false;

                // Проходим по всем сессиям и выполняем привязанные действия
            foreach (var session in _sessions)
                {
                    foreach (var binding in session.Bindings)
                    {
                        if (binding.NoteNumber != noteNumber) continue;

                        matchingBindingFound = true;
                        if (!session.IsAvailable && binding.Action != BindingAction.RestartAudioDevice)
                        {
                            unavailableBindingFound = true;
                            continue;
                        }

                        var bindingExecuted = false;

                        switch (binding.Action)
                        {
                            case BindingAction.MuteToggle:
                                if (!muteToggleAllowed) continue;

                                if (session.ProcessName == "__master__")
                                    _audio.ToggleMasterMute();
                                else
                                    _audio.ToggleMute(session.ProcessName);
                                session.IsMuted = session.ProcessName == "__master__"
                                    ? _audio.GetMasterMute()
                                    : _audio.GetMute(session.ProcessName) ?? false;
                                actionExecuted = true;
                                bindingExecuted = true;
                                break;

                            case BindingAction.Mute:
                                if (session.ProcessName == "__master__")
                                    _audio.SetMasterMute(true);
                                else
                                    _audio.SetMute(session.ProcessName, true);
                                session.IsMuted = true;
                                actionExecuted = true;
                                bindingExecuted = true;
                                break;

                            case BindingAction.Unmute:
                                if (session.ProcessName == "__master__")
                                    _audio.SetMasterMute(false);
                                else
                                    _audio.SetMute(session.ProcessName, false);
                                session.IsMuted = false;
                                actionExecuted = true;
                                bindingExecuted = true;
                                break;

                            case BindingAction.HoldMute:
                                var holdStarted = StartHeldMute(noteNumber, session);
                                actionExecuted |= holdStarted;
                                holdActionActive |= holdStarted || IsHeldMuteActive(noteNumber, session);
                                bindingExecuted = holdStarted;
                                break;

                            case BindingAction.HoldVolume:
                                var volumeHoldStarted = StartHeldVolume(noteNumber, binding.VolumeStep, session);
                                actionExecuted |= volumeHoldStarted;
                                holdActionActive |= volumeHoldStarted || IsHeldVolumeActive(noteNumber, session);
                                bindingExecuted = volumeHoldStarted;
                                break;

                            case BindingAction.VolumeUp:
                                SetSessionVolume(session, session.Volume + binding.VolumeStep);
                                actionExecuted = true;
                                bindingExecuted = true;
                                break;

                            case BindingAction.VolumeDown:
                                SetSessionVolume(session, session.Volume - binding.VolumeStep);
                                actionExecuted = true;
                                bindingExecuted = true;
                                break;

                            case BindingAction.SetVolume:
                                SetSessionVolume(session, binding.VolumeStep);
                                actionExecuted = true;
                                bindingExecuted = true;
                                break;

                            case BindingAction.RestartAudioDevice:
                                actionExecuted = TryRestartSelectedAudioDevice(noteName, noteNumber);
                                bindingExecuted = actionExecuted;
                                break;
                        }

                        if (bindingExecuted)
                            HighlightMidiAction(session, binding);

                        // Обновляем слайдер если это выбранное приложение
                        if (_selected?.ProcessName == session.ProcessName)
                        {
                            if (binding.Action is BindingAction.VolumeUp or BindingAction.VolumeDown
                                or BindingAction.SetVolume or BindingAction.HoldVolume)
                                UpdateVolumeDisplay(session.Volume);
                            else
                                RefreshDetail();
                        }
                    }
                }

                UpdateMidiActionStatus(
                    noteNumber,
                    noteName,
                    matchingBindingFound,
                    unavailableBindingFound,
                    actionExecuted,
                    holdActionActive);
                AppListBox.Items.Refresh();
            });
        }

        private void OnNoteReleased(int noteNumber, string noteName)
        {
            Dispatcher.BeginInvoke(() =>
            {
                var matchingBindingFound = false;
                var actionExecuted = false;

                foreach (var session in _sessions)
                {
                    foreach (var binding in session.Bindings)
                    {
                        if (binding.NoteNumber != noteNumber ||
                            binding.Action is not (BindingAction.HoldMute or BindingAction.HoldVolume))
                            continue;

                        matchingBindingFound = true;
                        actionExecuted |= binding.Action == BindingAction.HoldMute
                            ? StopHeldMute(noteNumber, session)
                            : StopHeldVolume(noteNumber, session);
                    }
                }

                if (matchingBindingFound)
                {
                    LastKeyLabel.Text = actionExecuted
                        ? LocalizationManager.Format("Status.HoldReleasedFormat", noteName, noteNumber)
                        : LocalizationManager.Format("Status.HoldInactiveFormat", noteName, noteNumber);
                    AppListBox.Items.Refresh();
                }
            });
        }

        private bool StartHeldMute(int noteNumber, AppSession session)
        {
            var key = (noteNumber, session.ProcessName);
            if (_heldMuteStates.ContainsKey(key))
                return false;

            var wasMuted = session.ProcessName == "__master__"
                ? _audio.GetMasterMute()
                : _audio.GetMute(session.ProcessName) ?? session.IsMuted;

            _heldMuteStates[key] = wasMuted;

            if (session.ProcessName == "__master__")
                _audio.SetMasterMute(true);
            else
                _audio.SetMute(session.ProcessName, true);

            session.IsMuted = true;
            return true;
        }

        private bool StopHeldMute(int noteNumber, AppSession session)
        {
            var key = (noteNumber, session.ProcessName);
            if (!_heldMuteStates.Remove(key, out var wasMuted))
                return false;

            if (session.ProcessName == "__master__")
                _audio.SetMasterMute(wasMuted);
            else
                _audio.SetMute(session.ProcessName, wasMuted);

            session.IsMuted = wasMuted;

            if (_selected?.ProcessName == session.ProcessName)
                RefreshDetail();

            return true;
        }

        private bool IsHeldMuteActive(int noteNumber, AppSession session)
            => _heldMuteStates.ContainsKey((noteNumber, session.ProcessName));

        private bool StartHeldVolume(int noteNumber, int targetVolume, AppSession session)
        {
            var key = (noteNumber, session.ProcessName);
            if (_heldVolumeStates.ContainsKey(key))
                return false;

            var previousVolume = session.ProcessName == "__master__"
                ? _audio.GetMasterVolume()
                : _audio.GetVolume(session.ProcessName) ?? session.Volume;

            _heldVolumeStates[key] = previousVolume;

            if (session.ProcessName == "__master__")
                _audio.SetMasterVolume(targetVolume);
            else
                _audio.SetVolume(session.ProcessName, targetVolume);

            session.Volume = Math.Clamp(targetVolume, 0, 100);
            return true;
        }

        private bool StopHeldVolume(int noteNumber, AppSession session)
        {
            var key = (noteNumber, session.ProcessName);
            if (!_heldVolumeStates.Remove(key, out var previousVolume))
                return false;

            if (session.ProcessName == "__master__")
                _audio.SetMasterVolume(previousVolume);
            else
                _audio.SetVolume(session.ProcessName, previousVolume);

            session.Volume = Math.Clamp(previousVolume, 0, 100);

            if (_selected?.ProcessName == session.ProcessName)
                UpdateVolumeDisplay(session.Volume);

            return true;
        }

        private void SetSessionVolume(AppSession session, float volume)
        {
            var nextVolume = Math.Clamp(volume, 0, 100);

            if (session.ProcessName == "__master__")
                _audio.SetMasterVolume(nextVolume);
            else
                _audio.SetVolume(session.ProcessName, nextVolume);

            session.Volume = nextVolume;
        }

        private bool IsHeldVolumeActive(int noteNumber, AppSession session)
            => _heldVolumeStates.ContainsKey((noteNumber, session.ProcessName));

        private void UpdateMidiActionStatus(
            int noteNumber,
            string noteName,
            bool matchingBindingFound,
            bool unavailableBindingFound,
            bool actionExecuted,
            bool heldMuteActive = false)
        {
            var status = actionExecuted
                ? heldMuteActive ? LocalizationManager.Text("Status.HoldActive") : LocalizationManager.Text("Status.ActionExecuted")
                : heldMuteActive
                ? LocalizationManager.Text("Status.HoldActive")
                : unavailableBindingFound
                ? LocalizationManager.Text("Status.AppUnavailable")
                : matchingBindingFound
                ? LocalizationManager.Text("Status.RepeatIgnored")
                : LocalizationManager.Text("Status.NoBinding");

            LastKeyLabel.Text = LocalizationManager.Format("Status.NoteFormat", noteName, noteNumber, status);
        }

        private bool CanToggleMute(int noteNumber)
        {
            var now = DateTime.UtcNow;

            if (_lastMuteToggleByNote.TryGetValue(noteNumber, out var lastToggle) &&
                now - lastToggle < MuteToggleDebounce)
            {
                return false;
            }

            _lastMuteToggleByNote[noteNumber] = now;
            return true;
        }

        private void HighlightMidiAction(AppSession session, MidiBinding binding)
        {
            session.IsMidiActive = true;
            binding.IsMidiActive = true;

            RestartHighlightTimer(
                _sessionHighlightTimers,
                session,
                () =>
                {
                    session.IsMidiActive = false;
                    AppListBox.Items.Refresh();
                });

            RestartHighlightTimer(
                _bindingHighlightTimers,
                binding,
                () =>
                {
                    binding.IsMidiActive = false;
                    BindingsListBox.Items.Refresh();
                });

            AppListBox.Items.Refresh();
            BindingsListBox.Items.Refresh();
        }

        private static void RestartHighlightTimer<TKey>(
            IDictionary<TKey, DispatcherTimer> timers,
            TKey key,
            Action elapsed)
            where TKey : notnull
        {
            if (timers.TryGetValue(key, out var existingTimer))
            {
                existingTimer.Stop();
                timers.Remove(key);
            }

            var timer = new DispatcherTimer { Interval = MidiHighlightDuration };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                timers.Remove(key);
                elapsed();
            };

            timers[key] = timer;
            timer.Start();
        }

        private void StopHighlightTimers()
        {
            foreach (var timer in _sessionHighlightTimers.Values.Concat(_bindingHighlightTimers.Values))
                timer.Stop();

            _sessionHighlightTimers.Clear();
            _bindingHighlightTimers.Clear();
        }

        private void AppListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selected = AppListBox.SelectedItem as AppSession;
            if (_selected == null)
            {
                ClearSelection();
                return;
            }

            EmptyState.Visibility = Visibility.Collapsed;
            AppDetail.Visibility = Visibility.Visible;

            RefreshDetail();
        }

        private void RefreshDetail()
        {
            if (_selected == null) return;

            DetailName.Text = _selected.DisplayName;

            bool isMaster = _selected.ProcessName == "__master__";

            DetailMeta.Text = isMaster
                ? LocalizationManager.Text("Main.MasterDescription")
                : !_selected.IsAvailable
                ? LocalizationManager.Text("Main.AppNotRunning")
                : LocalizationManager.Format("Main.AudioSessionActiveFormat", _selected.Pid);

            MuteToggleBtn.IsEnabled = _selected.IsAvailable;
            VolumePanel.IsEnabled = _selected.IsAvailable;

            if (_selected.IsAvailable)
            {
                float vol = isMaster
                    ? _audio.GetMasterVolume()
                    : _audio.GetVolume(_selected.ProcessName) ?? 0f;

                _selected.Volume = vol;
                UpdateVolumeDisplay(vol);

                bool muted = isMaster
                    ? _audio.GetMasterMute()
                    : _audio.GetMute(_selected.ProcessName) ?? false;

                _selected.IsMuted = muted;
                MuteToggleBtn.Content = muted
                    ? LocalizationManager.Text("Action.Unmute")
                    : LocalizationManager.Text("Main.Mute");
                MuteToggleBtn.Tag = muted ? "muted" : "";
            }
            else
            {
                VolumeLabel.Text = "";
                MuteToggleBtn.Content = LocalizationManager.Text("Main.Mute");
                MuteToggleBtn.Tag = "";
            }

            BindingsListBox.ItemsSource = null;
            BindingsListBox.ItemsSource = _selected.Bindings;
        }

        private void MuteToggleBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null) return;

            if (_selected.ProcessName == "__master__")
                _audio.ToggleMasterMute();
            else
                _audio.ToggleMute(_selected.ProcessName);

            RefreshDetail();
            var idx = AppListBox.SelectedIndex;
            AppListBox.Items.Refresh();
            AppListBox.SelectedIndex = idx;
        }

        private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_selected == null || VolumeLabel == null) return;
            int val = (int)e.NewValue;
            VolumeLabel.Text = $"{val}%";

            if (_updatingVolumeDisplay)
                return;

            _selected.Volume = val;

            QueueVolumeApply(_selected.ProcessName, val);
        }

        private void QueueVolumeApply(string processName, float volume)
        {
            lock (_volumeApplyLock)
            {
                _pendingVolumeProcessName = processName;
                _pendingVolumeValue = Math.Clamp(volume, 0, 100);

                if (_volumeApplyWorkerRunning)
                    return;

                _volumeApplyWorkerRunning = true;
            }

            _ = Task.Run(ApplyQueuedVolumeAsync);
        }

        private async Task ApplyQueuedVolumeAsync()
        {
            while (true)
            {
                string? processName;
                float volume;

                lock (_volumeApplyLock)
                {
                    processName = _pendingVolumeProcessName;
                    volume = _pendingVolumeValue;
                    _pendingVolumeProcessName = null;

                    if (string.IsNullOrWhiteSpace(processName))
                    {
                        _volumeApplyWorkerRunning = false;
                        return;
                    }
                }

                try
                {
                    if (processName == "__master__")
                        _audio.SetMasterVolume(volume);
                    else
                        _audio.SetVolume(processName, volume);
                }
                catch (Exception ex)
                {
                    DiagnosticLog.Error("Audio", "Failed to apply volume from slider.", ex);
                }

                await Task.Delay(16);
            }
        }

        private void UpdateVolumeDisplay(float volume)
        {
            if (VolumeSlider == null || VolumeLabel == null)
                return;

            var roundedVolume = (int)Math.Round(Math.Clamp(volume, 0, 100));
            _updatingVolumeDisplay = true;
            try
            {
                VolumeSlider.Value = roundedVolume;
                VolumeLabel.Text = $"{roundedVolume}%";
            }
            finally
            {
                _updatingVolumeDisplay = false;
            }
        }

        private void AddBinding_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null) return;

            var dialog = new BindingDialog(_midi, CreateBindingConflictInfos());
            dialog.Owner = this;
            if (dialog.ShowDialog() == true && dialog.ResultBinding != null)
            {
                var conflict = FindBindingConflict(dialog.ResultBinding.NoteNumber);

                if (conflict != null)
                {
                    var dialog2 = CreateBindingConflictDialog(dialog.ResultBinding, conflict.Value.Session, conflict.Value.Binding);
                    dialog2.Owner = this;

                    if (dialog2.ShowDialog() != true) return;
                    conflict.Value.Session.Bindings.Remove(conflict.Value.Binding);
                }

                _selected.Bindings.Add(dialog.ResultBinding);
                BindingsListBox.ItemsSource = null;
                BindingsListBox.ItemsSource = _selected.Bindings;
                UpdateTotalBindings();
                ApplySessionFilter();
                RestoreSelection(_selected.ProcessName);
                SaveState();
            }
        }

        private void RemoveBinding_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is MidiBinding binding && _selected != null)
            {
                _selected.Bindings.Remove(binding);
                RefreshDetail();
                UpdateTotalBindings();
                ApplySessionFilter();
                RestoreSelection(_selected.ProcessName);
                SaveState();
            }
        }

        private void EditBinding_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not MidiBinding binding || _selected == null)
                return;

            var dialog = new BindingDialog(_midi, binding, CreateBindingConflictInfos(binding)) { Owner = this };
            if (dialog.ShowDialog() != true || dialog.ResultBinding == null)
                return;

            var conflict = FindBindingConflict(dialog.ResultBinding.NoteNumber, binding);

            if (conflict != null)
            {
                var dialog2 = CreateBindingConflictDialog(dialog.ResultBinding, conflict.Value.Session, conflict.Value.Binding);
                dialog2.Owner = this;

                if (dialog2.ShowDialog() != true) return;
                conflict.Value.Session.Bindings.Remove(conflict.Value.Binding);
            }

            binding.NoteNumber = dialog.ResultBinding.NoteNumber;
            binding.NoteName = dialog.ResultBinding.NoteName;
            binding.Action = dialog.ResultBinding.Action;
            binding.VolumeStep = dialog.ResultBinding.VolumeStep;

            RefreshDetail();
            UpdateTotalBindings();
            ApplySessionFilter();
            RestoreSelection(_selected.ProcessName);
            SaveState();
        }

        private IEnumerable<BindingConflictInfo> CreateBindingConflictInfos(MidiBinding? excludedBinding = null)
        {
            return _sessions.SelectMany(session => session.Bindings
                .Where(binding => binding != excludedBinding)
                .Select(binding => new BindingConflictInfo(
                    binding.NoteNumber,
                    session.DisplayName,
                    DescribeBindingAction(binding))));
        }

        private (AppSession Session, MidiBinding Binding)? FindBindingConflict(
            int noteNumber,
            MidiBinding? excludedBinding = null)
        {
            foreach (var session in _sessions)
            {
                var binding = session.Bindings.FirstOrDefault(binding =>
                    binding != excludedBinding &&
                    binding.NoteNumber == noteNumber);

                if (binding != null)
                    return (session, binding);
            }

            return null;
        }

        private static ConfirmDialog CreateBindingConflictDialog(
            MidiBinding requestedBinding,
            AppSession conflictSession,
            MidiBinding conflictBinding)
        {
            var message =
                LocalizationManager.Format(
                    "Conflict.MessageFormat",
                    requestedBinding.NoteName,
                    requestedBinding.NoteNumber,
                    conflictSession.DisplayName,
                    DescribeBindingAction(conflictBinding),
                    DescribeBindingAction(requestedBinding));

            return new ConfirmDialog(
                LocalizationManager.Text("Conflict.Title"),
                message,
                LocalizationManager.Text("Conflict.Replace"));
        }

        private static string DescribeBindingAction(MidiBinding binding)
        {
            return binding.Action switch
            {
                BindingAction.MuteToggle => LocalizationManager.Text("Action.MuteToggle"),
                BindingAction.Mute => LocalizationManager.Text("Action.Mute"),
                BindingAction.Unmute => LocalizationManager.Text("Action.Unmute"),
                BindingAction.HoldMute => LocalizationManager.Text("Action.HoldMute"),
                BindingAction.VolumeUp => LocalizationManager.Format("Action.VolumeUpFormat", binding.VolumeStep),
                BindingAction.VolumeDown => LocalizationManager.Format("Action.VolumeDownFormat", binding.VolumeStep),
                BindingAction.SetVolume => LocalizationManager.Format("Action.SetVolumeFormat", binding.VolumeStep),
                BindingAction.HoldVolume => LocalizationManager.Format("Action.HoldVolumeFormat", binding.VolumeStep),
                BindingAction.RestartAudioDevice => LocalizationManager.Text("Action.RestartAudioDevice"),
                _ => binding.Action.ToString()
            };
        }

        private void RefreshRestartAudioDeviceList(string? selectedInstanceId = null)
        {
            IReadOnlyList<AudioDeviceInfo> devices;
            try
            {
                devices = _audioDeviceRestart.GetRestartableAudioDevices();
            }
            catch (Exception ex)
            {
                DiagnosticLog.Error("AudioDeviceRestart", "Failed to enumerate restartable audio devices.", ex);
                devices = Array.Empty<AudioDeviceInfo>();
            }

            selectedInstanceId ??= _selectedRestartAudioDeviceInstanceId;
            _updatingRestartAudioDevices = true;
            RestartAudioDeviceCombo.ItemsSource = devices;

            var selected = !string.IsNullOrWhiteSpace(selectedInstanceId)
                ? devices.FirstOrDefault(device => string.Equals(
                    device.InstanceId,
                    selectedInstanceId,
                    StringComparison.OrdinalIgnoreCase))
                : null;

            RestartAudioDeviceCombo.SelectedItem = selected;
            _selectedRestartAudioDeviceInstanceId = selected?.InstanceId ?? selectedInstanceId;
            _updatingRestartAudioDevices = false;
        }

        private void RestartAudioDeviceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_updatingRestartAudioDevices)
                return;

            _selectedRestartAudioDeviceInstanceId = RestartAudioDeviceCombo.SelectedItem is AudioDeviceInfo device
                ? device.InstanceId
                : null;
            SaveState();
        }

        private bool TryRestartSelectedAudioDevice(string noteName, int noteNumber)
        {
            if (_restartingAudioDevice)
            {
                LastKeyLabel.Text = LocalizationManager.Format(
                    "Status.NoteFormat",
                    noteName,
                    noteNumber,
                    LocalizationManager.Text("Status.AudioDeviceRestartAlreadyRunning"));
                return false;
            }

            if (string.IsNullOrWhiteSpace(_selectedRestartAudioDeviceInstanceId))
            {
                LastKeyLabel.Text = LocalizationManager.Format(
                    "Status.NoteFormat",
                    noteName,
                    noteNumber,
                    LocalizationManager.Text("Status.AudioDeviceRestartNotConfigured"));
                return false;
            }

            var deviceName = (RestartAudioDeviceCombo.SelectedItem as AudioDeviceInfo)?.DisplayName
                ?? LocalizationManager.Text("Common.NotSet");

            _restartingAudioDevice = true;
            LastKeyLabel.Text = LocalizationManager.Format("Status.AudioDeviceRestartStartingFormat", deviceName);

            _ = RestartSelectedAudioDeviceAsync(deviceName);
            return true;
        }

        private async Task RestartSelectedAudioDeviceAsync(string deviceName)
        {
            try
            {
                await _audioDeviceRestart.RestartDeviceAsync(_selectedRestartAudioDeviceInstanceId!);
                Dispatcher.Invoke(() =>
                {
                    LastKeyLabel.Text = LocalizationManager.Format("Status.AudioDeviceRestartDoneFormat", deviceName);
                    RefreshRestartAudioDeviceList(_selectedRestartAudioDeviceInstanceId);
                    LoadSessions();
                });
            }
            catch (Exception ex)
            {
                DiagnosticLog.Error("AudioDeviceRestart", "Failed to restart audio device.", ex);
                Dispatcher.Invoke(() =>
                {
                    LastKeyLabel.Text = LocalizationManager.Text("Status.AudioDeviceRestartFailed");
                });
            }
            finally
            {
                Dispatcher.Invoke(() => _restartingAudioDevice = false);
            }
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            LoadSessions();
        }

        private void SettingsMenuBtn_Click(object sender, RoutedEventArgs e)
        {
            AutoStartManager.RemoveStaleEntry();
            UpdateAutoStartMenuItem();
            SettingsMenuBtn.ContextMenu.PlacementTarget = SettingsMenuBtn;
            SettingsMenuBtn.ContextMenu.IsOpen = true;
        }

        private void AboutMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new AboutDialog { Owner = this };
            dialog.ShowDialog();
        }

        private void ThemeAutoMenuItem_Click(object sender, RoutedEventArgs e)
        {
            SetThemeMode(AppThemeMode.Auto);
        }

        private void ThemeDarkMenuItem_Click(object sender, RoutedEventArgs e)
        {
            SetThemeMode(AppThemeMode.Dark);
        }

        private void ThemeLightMenuItem_Click(object sender, RoutedEventArgs e)
        {
            SetThemeMode(AppThemeMode.Light);
        }

        private void LanguageAutoMenuItem_Click(object sender, RoutedEventArgs e)
        {
            SetLanguageMode(AppLanguageMode.Auto);
        }

        private void LanguageRussianMenuItem_Click(object sender, RoutedEventArgs e)
        {
            SetLanguageMode(AppLanguageMode.Russian);
        }

        private void LanguageEnglishMenuItem_Click(object sender, RoutedEventArgs e)
        {
            SetLanguageMode(AppLanguageMode.English);
        }

        private void AutoStartMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (AutoStartManager.IsEnabledForCurrentExecutable())
                AutoStartManager.Disable();
            else
                AutoStartManager.Enable();

            UpdateAutoStartMenuItem();
        }

        private void SetThemeMode(AppThemeMode themeMode)
        {
            _themeMode = themeMode;
            ThemeManager.SetMode(themeMode);
            UpdateThemeMenuItems();
            SaveState();
        }

        private void SetLanguageMode(AppLanguageMode languageMode)
        {
            _languageMode = languageMode;
            LocalizationManager.SetMode(languageMode);
            UpdateLanguageMenuItems();
            RefreshLocalizedText();
            SaveState();
        }

        private void UpdateThemeMenuItems()
        {
            ThemeAutoMenuItem.IsChecked = _themeMode == AppThemeMode.Auto;
            ThemeDarkMenuItem.IsChecked = _themeMode == AppThemeMode.Dark;
            ThemeLightMenuItem.IsChecked = _themeMode == AppThemeMode.Light;
        }

        private void UpdateLanguageMenuItems()
        {
            LanguageAutoMenuItem.IsChecked = _languageMode == AppLanguageMode.Auto;
            LanguageRussianMenuItem.IsChecked = _languageMode == AppLanguageMode.Russian;
            LanguageEnglishMenuItem.IsChecked = _languageMode == AppLanguageMode.English;
        }

        private void UpdateAutoStartMenuItem()
        {
            AutoStartMenuItem.IsChecked = AutoStartManager.IsEnabledForCurrentExecutable();
        }

        private void RefreshLocalizedText()
        {
            UpdateMasterSessionDisplayName(_sessions);
            UpdateAppListEditMode();
            SetBypass(_bypassEnabled, saveState: false);
            UpdateTotalBindings();

            if (_midi.IsConnected)
                MidiStatusLabel.Text = $"MIDI: {_midi.DeviceName}";
            else if (string.IsNullOrWhiteSpace(MidiStatusLabel.Text) ||
                     MidiStatusLabel.Text.Contains("MIDI:", StringComparison.OrdinalIgnoreCase))
                MidiStatusLabel.Text = LocalizationManager.Text("Main.MidiNotFound");

            AppListBox.Items.Refresh();
            BindingsListBox.Items.Refresh();
            if (_selected != null)
                RefreshDetail();
            App.UpdateTrayMenu(_midi.IsConnected, _midi.DeviceName, _bypassEnabled);
        }

        private static bool IsMasterSession(string processName)
            => processName == "__master__";

        private static void UpdateMasterSessionDisplayName(IEnumerable<AppSession> sessions)
        {
            foreach (var session in sessions)
                UpdateMasterSessionDisplayName(session);
        }

        private static void UpdateMasterSessionDisplayName(AppSession session)
        {
            if (IsMasterSession(session.ProcessName))
                session.DisplayName = LocalizationManager.Text("Main.MasterDisplayName");
        }

        private void ExportSettings_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Title = LocalizationManager.Text("Settings.ExportTitle"),
                Filter = LocalizationManager.Text("Settings.FileFilter"),
                FileName = "MidiMute-settings.json",
                DefaultExt = ".json",
                AddExtension = true
            };

            if (dialog.ShowDialog(this) != true)
                return;

            try
            {
                _storage.Export(
                    dialog.FileName,
                    _sessions,
                    _bypassEnabled,
                    _selectedMidiDeviceName,
                    _hiddenProcessNames,
                    _appProfiles.Values,
                    _themeMode,
                    _languageMode,
                    _selectedRestartAudioDeviceInstanceId);
                LastKeyLabel.Text = LocalizationManager.Text("Main.SettingsExported");
            }
            catch (Exception ex)
            {
                DiagnosticLog.Error("Settings", "Failed to export settings.", ex);
                MessageBox.Show(
                    this,
                    LocalizationManager.Text("Settings.ExportFailed"),
                    LocalizationManager.Text("Settings.ExportTitle"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ImportSettings_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = LocalizationManager.Text("Settings.ImportTitle"),
                Filter = LocalizationManager.Text("Settings.FileFilter"),
                DefaultExt = ".json",
                CheckFileExists = true
            };

            if (dialog.ShowDialog(this) != true)
                return;

            var confirmDialog = new ConfirmDialog(
                LocalizationManager.Text("Settings.ImportTitle"),
                LocalizationManager.Text("Settings.ImportConfirmMessage"),
                LocalizationManager.Text("Settings.ImportConfirmButton"))
            {
                Owner = this
            };

            if (confirmDialog.ShowDialog() != true)
                return;

            try
            {
                var importedSettings = _storage.Import(dialog.FileName);
                string? backupPath;

                try
                {
                    backupPath = _storage.BackupCurrentSettings();
                }
                catch (Exception ex)
                {
                    DiagnosticLog.Error("Settings", "Failed to back up settings before import.", ex);
                    MessageBox.Show(
                        this,
                        LocalizationManager.Text("Settings.BackupFailed"),
                        LocalizationManager.Text("Settings.ImportTitle"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }

                _heldMuteStates.Clear();
                _heldVolumeStates.Clear();
                LoadSessions(importedSettings);
                SaveState();
                RefreshMidiDeviceList(_selectedMidiDeviceName);
                RefreshRestartAudioDeviceList(_selectedRestartAudioDeviceInstanceId);
                _midi.ConnectToDevice(_selectedMidiDeviceName);
                LastKeyLabel.Text = string.IsNullOrWhiteSpace(backupPath)
                    ? LocalizationManager.Text("Main.SettingsImported")
                    : LocalizationManager.Format("Main.SettingsImportedWithBackupFormat", backupPath);
            }
            catch (Exception ex)
            {
                DiagnosticLog.Error("Settings", "Failed to import settings.", ex);
                MessageBox.Show(
                    this,
                    LocalizationManager.Text("Settings.ImportFailed"),
                    LocalizationManager.Text("Settings.ImportTitle"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplySessionFilter();
        }

        private void ApplySessionFilter()
        {
            var filter = SearchBox.Text.Trim();
            var source = GetSortedAppListItems();

            if (string.IsNullOrWhiteSpace(filter))
            {
                AppListBox.ItemsSource = source;
                return;
            }

            var filtered = source
                .Where(s => s.DisplayName.Contains(filter, StringComparison.CurrentCultureIgnoreCase) ||
                            s.ProcessName.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .ToList();

            AppListBox.ItemsSource = filtered;
        }

        private List<AppSession> GetSortedAppListItems()
        {
            return _sessions
                .Where(session => _editingAppList || !session.IsHidden)
                .OrderBy(session => IsMasterSession(session.ProcessName) ? 0 : 1)
                .ThenBy(session => _editingAppList && session.IsHidden ? 1 : 0)
                .ThenByDescending(session => session.Bindings.Count > 0)
                .ThenByDescending(session => session.IsAvailable)
                .ThenBy(session => session.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(session => session.ProcessName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private void EditAppListBtn_Click(object sender, RoutedEventArgs e)
        {
            _editingAppList = !_editingAppList;
            UpdateAppListEditMode();
        }

        private void ToggleAppHidden_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: AppSession session } || !session.CanHide)
                return;

            session.IsHidden = !session.IsHidden;
            if (session.IsHidden)
                _hiddenProcessNames.Add(session.ProcessName);
            else
                _hiddenProcessNames.Remove(session.ProcessName);

            SaveState();
            ApplySessionFilter();
            if (!_editingAppList && session.IsHidden)
                ClearSelection();
            else
                RestoreSelection(session.ProcessName);
            LastKeyLabel.Text = session.IsHidden
                ? LocalizationManager.Format("Main.AppHiddenFormat", session.DisplayName)
                : LocalizationManager.Format("Main.AppShownFormat", session.DisplayName);
        }

        private void UpdateAppListEditMode()
        {
            foreach (var session in _sessions)
                session.IsListEditing = _editingAppList;

            AppListTitle.Text = _editingAppList
                ? LocalizationManager.Text("Main.AppListEditingTitle")
                : LocalizationManager.Text("Main.AppListTitle");
            EditAppListBtn.Content = _editingAppList ? "\u2713" : "\u270E";
            EditAppListBtn.ToolTip = _editingAppList
                ? LocalizationManager.Text("Tooltip.DoneEditApps")
                : LocalizationManager.Text("Tooltip.EditApps");

            ApplySessionFilter();
            if (!_editingAppList && _selected?.IsHidden == true)
                ClearSelection();
            AppListBox.Items.Refresh();
        }

        private void ApplySessionListState(AppSession session)
        {
            session.IsHidden = session.CanHide && _hiddenProcessNames.Contains(session.ProcessName);
            session.IsListEditing = _editingAppList;
        }

        private void RestoreSelection(string? processName)
        {
            if (string.IsNullOrEmpty(processName))
            {
                ClearSelection();
                return;
            }

            var session = AppListBox.Items
                .OfType<AppSession>()
                .FirstOrDefault(s => s.ProcessName == processName);

            if (session == null)
            {
                ClearSelection();
                return;
            }

            AppListBox.SelectedItem = session;
        }

        private void ClearSelection()
        {
            _selected = null;
            EmptyState.Visibility = Visibility.Visible;
            AppDetail.Visibility = Visibility.Collapsed;
        }

        private void BypassBtn_Click(object sender, RoutedEventArgs e)
        {
            ToggleBypass();
        }

        public void ToggleBypass()
        {
            SetBypass(!_bypassEnabled);
        }

        private void SetBypass(bool enabled, bool saveState = true)
        {
            _bypassEnabled = enabled;
            BypassBtn.Content = _bypassEnabled
                ? LocalizationManager.Text("Main.BypassOn")
                : LocalizationManager.Text("Main.BypassOff");
            BypassBtn.Tag = _bypassEnabled ? "active" : "";
            App.UpdateTrayMenu(_midi.IsConnected, _midi.DeviceName, _bypassEnabled);

            if (saveState)
                SaveState();
        }

        private void UpdateTotalBindings()
        {
            int total = _sessions.Sum(s => s.Bindings.Count);
            if (TotalBindingsLabel != null)
                TotalBindingsLabel.Text = LocalizationManager.Format("Main.TotalBindingsFormat", total);
        }

        private void SaveState()
        {
            _storage.Save(
                _sessions,
                _bypassEnabled,
                _selectedMidiDeviceName,
                _hiddenProcessNames,
                _appProfiles.Values,
                _themeMode,
                _languageMode,
                _selectedRestartAudioDeviceInstanceId);
        }

        protected override void OnClosed(EventArgs e)
        {
            _audioSessionRefreshTimer.Stop();
            StopHighlightTimers();
            SaveState();
            _midi.Dispose();
            base.OnClosed(e);
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!_allowClose)
            {
                e.Cancel = true;
                Hide();
                return;
            }

            base.OnClosing(e);
        }

        public void ExitApplication()
        {
            _allowClose = true;
            Application.Current.Shutdown();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            DragMove();
        }

        private void MinimizeBtn_Click(object sender, RoutedEventArgs e)
        {
            Hide();
        }
    }
}

