using System.Globalization;
using System.Windows;
using System.Windows.Data;
using MidiMute.Models;

namespace MidiMute
{
    public class CountToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is int count && count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class BindingActionTitleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not MidiBinding binding)
                return "";

            return binding.Action switch
            {
                BindingAction.MuteToggle => LocalizationManager.Text("Action.MuteToggle"),
                BindingAction.Mute => LocalizationManager.Text("Action.Mute"),
                BindingAction.Unmute => LocalizationManager.Text("Action.Unmute"),
                BindingAction.HoldMute => LocalizationManager.Text("Action.HoldMute"),
                BindingAction.HoldVolume => LocalizationManager.Text("Action.HoldVolume"),
                BindingAction.VolumeUp => LocalizationManager.Text("Action.VolumeUp"),
                BindingAction.VolumeDown => LocalizationManager.Text("Action.VolumeDown"),
                BindingAction.SetVolume => LocalizationManager.Text("Action.SetVolume"),
                BindingAction.RestartAudioDevice => LocalizationManager.Text("Action.RestartAudioDevice"),
                _ => binding.Action.ToString()
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class BindingActionDetailConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not MidiBinding binding)
                return "";

            return binding.Action switch
            {
                BindingAction.VolumeUp => LocalizationManager.Format("Action.StepPerPressFormat", binding.VolumeStep),
                BindingAction.VolumeDown => LocalizationManager.Format("Action.StepPerPressFormat", binding.VolumeStep),
                BindingAction.SetVolume => LocalizationManager.Format("Action.ToVolumeFormat", binding.VolumeStep),
                BindingAction.HoldVolume => LocalizationManager.Format("Action.HoldToVolumeFormat", binding.VolumeStep),
                BindingAction.RestartAudioDevice => LocalizationManager.Text("Action.RestartAudioDeviceDetail"),
                _ => ""
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
