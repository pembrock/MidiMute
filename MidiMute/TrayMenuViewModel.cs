using System.Windows;
using System.Windows.Input;

namespace MidiMute
{
    public class TrayMenuViewModel
    {
        public static TrayMenuViewModel Instance { get; } = new();

        public ICommand ShowCommand => new RelayCommand(ShowWindow);
        public ICommand BypassCommand => new RelayCommand(ToggleBypass);
        public ICommand ExitCommand => new RelayCommand(Exit);

        private void ShowWindow(object? _)
        {
            var win = App.MainWin;
            if (win == null) return;
            win.Show();
            win.WindowState = WindowState.Normal;
            win.Activate();
        }

        private void ToggleBypass(object? _)
        {
            App.MainWin?.ToggleBypass();
        }

        private void Exit(object? _)
        {
            Application.Current.Shutdown();
        }
    }

    public class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public RelayCommand(Action<object?> execute) => _execute = execute;
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => _execute(parameter);
    }
}
