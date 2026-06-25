namespace MidiMute.Models
{
    public sealed class AudioDeviceInfo
    {
        public string Name { get; set; } = "";
        public string InstanceId { get; set; } = "";
        public string DeviceClass { get; set; } = "";

        public string DisplayName
        {
            get
            {
                var name = string.IsNullOrWhiteSpace(Name) ? InstanceId : Name;
                return string.IsNullOrWhiteSpace(DeviceClass)
                    ? name
                    : $"{name} ({DeviceClass})";
            }
        }
    }
}
