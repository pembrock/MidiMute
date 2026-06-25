using System.Windows;
using System.Windows.Controls;
using MidiMute.Models;
using MidiMute.Services;

namespace MidiMute
{
    public partial class BindingDialog : Window
    {
        private readonly MidiService _midi;
        private readonly IReadOnlyDictionary<int, BindingConflictInfo> _conflictsByNote;
        private bool _listening;
        private int _noteNumber;
        private string _noteName = "";

        public MidiBinding? ResultBinding { get; private set; }

        public BindingDialog(MidiService midi, IEnumerable<BindingConflictInfo>? conflicts = null)
        {
            InitializeComponent();
            _midi = midi;
            _conflictsByNote = (conflicts ?? Enumerable.Empty<BindingConflictInfo>())
                .GroupBy(conflict => conflict.NoteNumber)
                .ToDictionary(group => group.Key, group => group.First());
        }

        public BindingDialog(MidiService midi, MidiBinding binding, IEnumerable<BindingConflictInfo>? conflicts = null) : this(midi, conflicts)
        {
            _noteNumber = binding.NoteNumber;
            _noteName = binding.NoteName;

            Title = LocalizationManager.Text("Dialog.BindingEditTitle");
            TitleBarLabel.Text = LocalizationManager.Text("Dialog.BindingEditTitle");
            DialogTitleLabel.Text = LocalizationManager.Text("Dialog.BindingEditHeader");
            SaveButton.Content = LocalizationManager.Text("Dialog.Save");
            KeyLabel.Text = $"{binding.NoteName}  (#{binding.NoteNumber})";
            KeyLabel.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(109, 99, 255));
            UpdateConflictWarning();

            SelectAction(binding.Action);
            StepSlider.Value = binding.VolumeStep;
        }

        private void ListenButton_Click(object sender, RoutedEventArgs e)
        {
            StopListening();
            _listening = true;
            KeyLabel.Text = LocalizationManager.Text("Dialog.WaitingForKey");
            KeyLabel.Foreground = (System.Windows.Media.Brush)FindResource("TextPrimary");
            _midi.NotePressed += OnNotePressed;
        }

        private void OnNotePressed(int noteNumber, string noteName)
        {
            if (!_listening) return;
            StopListening();

            _noteNumber = noteNumber;
            _noteName = noteName;

            Dispatcher.Invoke(() =>
            {
                KeyLabel.Text = $"{noteName}  (#{noteNumber})";
                KeyLabel.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(109, 99, 255));
                UpdateConflictWarning();
            });
        }

        private void ActionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ActionCombo.SelectedItem is ComboBoxItem item)
            {
                bool isVolumeStep = item.Tag?.ToString() is "VolumeUp" or "VolumeDown";
                bool isSetVolume = item.Tag?.ToString() is "SetVolume" or "HoldVolume";
                if (VolumeStepPanel != null)
                    VolumeStepPanel.Visibility = isVolumeStep || isSetVolume
                        ? Visibility.Visible
                        : Visibility.Collapsed;

                if (StepSlider != null)
                {
                    StepSlider.Minimum = isSetVolume ? 0 : 1;
                    StepSlider.Maximum = isSetVolume ? 100 : 25;
                    StepSlider.Value = isSetVolume
                        ? Math.Max(StepSlider.Value, 50)
                        : Math.Clamp(StepSlider.Value, 1, 25);
                }

                if (VolumeValueTitle != null)
                    VolumeValueTitle.Text = isSetVolume
                        ? LocalizationManager.Text("Dialog.VolumeLevelTitle")
                        : LocalizationManager.Text("Dialog.VolumeStepTitle");
            }
        }

        private void StepSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (StepLabel != null)
                StepLabel.Text = $"{(int)e.NewValue}%";
        }

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            if (_noteNumber == 0 && string.IsNullOrEmpty(_noteName))
            {
                MessageBox.Show(
                    LocalizationManager.Text("Dialog.KeyRequired"),
                    LocalizationManager.Text("Dialog.Error"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var action = BindingAction.MuteToggle;
            if (ActionCombo.SelectedItem is ComboBoxItem item)
            {
                action = item.Tag?.ToString() switch
                {
                    "Mute" => BindingAction.Mute,
                    "Unmute" => BindingAction.Unmute,
                    "HoldMute" => BindingAction.HoldMute,
                    "HoldVolume" => BindingAction.HoldVolume,
                    "VolumeUp" => BindingAction.VolumeUp,
                    "VolumeDown" => BindingAction.VolumeDown,
                    "SetVolume" => BindingAction.SetVolume,
                    "RestartAudioDevice" => BindingAction.RestartAudioDevice,
                    _ => BindingAction.MuteToggle
                };
            }

            ResultBinding = new MidiBinding
            {
                NoteNumber = _noteNumber,
                NoteName = _noteName,
                Action = action,
                VolumeStep = (int)StepSlider.Value
            };

            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            DragMove();
        }

        private void StopListening()
        {
            _listening = false;
            _midi.NotePressed -= OnNotePressed;
        }

        protected override void OnClosed(EventArgs e)
        {
            StopListening();
            base.OnClosed(e);
        }

        private void SelectAction(BindingAction action)
        {
            var tag = action switch
            {
                BindingAction.Mute => "Mute",
                BindingAction.Unmute => "Unmute",
                BindingAction.HoldMute => "HoldMute",
                BindingAction.HoldVolume => "HoldVolume",
                BindingAction.VolumeUp => "VolumeUp",
                BindingAction.VolumeDown => "VolumeDown",
                BindingAction.SetVolume => "SetVolume",
                BindingAction.RestartAudioDevice => "RestartAudioDevice",
                _ => "MuteToggle"
            };

            ActionCombo.SelectedItem = ActionCombo.Items
                .OfType<ComboBoxItem>()
                .First(item => item.Tag?.ToString() == tag);
        }

        private void UpdateConflictWarning()
        {
            if (string.IsNullOrEmpty(_noteName) || !_conflictsByNote.TryGetValue(_noteNumber, out var conflict))
            {
                ConflictWarningPanel.Visibility = Visibility.Collapsed;
                ConflictWarningLabel.Text = "";
                return;
            }

            ConflictWarningLabel.Text = LocalizationManager.Format(
                "Conflict.InUseWarningFormat",
                conflict.AppDisplayName,
                conflict.ActionDescription);
            ConflictWarningPanel.Visibility = Visibility.Visible;
        }
    }

    public sealed record BindingConflictInfo(
        int NoteNumber,
        string AppDisplayName,
        string ActionDescription);
}
