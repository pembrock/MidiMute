using System.Windows;

namespace MidiMute
{
    public partial class ConfirmDialog : Window
    {
        public ConfirmDialog(string title, string message, string? confirmText = null)
        {
            InitializeComponent();
            Title = title;
            TitleBarLabel.Text = title;
            TitleLabel.Text = title;
            MessageLabel.Text = message;
            ConfirmBtn.Content = confirmText ?? LocalizationManager.Text("Common.Confirm");
        }

        private void ConfirmButton_Click(object sender, RoutedEventArgs e)
            => DialogResult = true;

        private void CancelButton_Click(object sender, RoutedEventArgs e)
            => DialogResult = false;

        private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            DragMove();
        }
    }
}
