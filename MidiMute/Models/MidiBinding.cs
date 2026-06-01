namespace MidiMute.Models
{
    public enum BindingAction
    {
        MuteToggle = 0,
        VolumeUp = 1,
        VolumeDown = 2,
        Mute = 3,
        Unmute = 4,
        SetVolume = 5,
        HoldMute = 6,
        HoldVolume = 7
    }

    public class MidiBinding
    {
        public int NoteNumber { get; set; }
        public string NoteName { get; set; } = "";
        public BindingAction Action { get; set; }
        public int VolumeStep { get; set; } = 10; // на сколько % менять громкость
        public bool IsMidiActive { get; set; }
    }
}
