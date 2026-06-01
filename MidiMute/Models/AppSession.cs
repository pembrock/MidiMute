using System.Windows.Media.Imaging;

namespace MidiMute.Models
{
    public class AppSession
    {
        public string ProcessName { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public int Pid { get; set; }
        public bool IsMuted { get; set; }
        public float Volume { get; set; }
        public List<MidiBinding> Bindings { get; set; } = new();
        public string DeviceName { get; set; } = "";
        public string? ExecutablePath { get; set; }
        public BitmapSource? Icon { get; set; }
        public bool IsAvailable { get; set; } = true;
        public bool IsHidden { get; set; }
        public bool CanHide => ProcessName != "__master__";
        public bool IsListEditing { get; set; }
        public bool IsMidiActive { get; set; }
    }
}
