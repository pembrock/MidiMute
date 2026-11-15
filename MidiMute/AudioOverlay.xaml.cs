using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using MidiMute.Models;

namespace MidiMute;

// One reusable, non-interactive window. No audio queries or disk access on this path.
public partial class AudioOverlay : Window
{
    private readonly DispatcherTimer _dismiss = new() { Interval = TimeSpan.FromMilliseconds(1600) };
    private bool _fading;
    private bool _disposed;
    private nint _handle;

    public AudioOverlay()
    {
        InitializeComponent();
        _dismiss.Tick += Dismiss;
        SourceInitialized += (_, _) =>
        {
            _handle = new WindowInteropHelper(this).Handle;
            // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT
            SetWindowLong(_handle, -20, GetWindowLong(_handle, -20) | 0x08000000 | 0x80 | 0x20);
        };
        Closed += (_, _) => { _disposed = true; _dismiss.Stop(); };
    }

    public void Present(AppSession session, bool volumeAction)
    {
        if (_disposed) return;
        _dismiss.Stop();
        _fading = false;
        BeginAnimation(OpacityProperty, null);
        Opacity = 1;
        AppName.Text = session.DisplayName;
        AppIcon.Source = session.Icon;
        FallbackIcon.Visibility = session.Icon == null ? Visibility.Visible : Visibility.Collapsed;
        FallbackIcon.Text = session.IsMuted ? "\uE74F" : "\uE767";
        Status.Text = LocalizationManager.Text(session.IsMuted ? "Overlay.Muted" : volumeAction ? "Overlay.Volume" : "Overlay.Unmuted");
        Percentage.Text = $"{Math.Round(Math.Clamp(session.Volume, 0, 100))}%";
        Level.SetResourceReference(BackgroundProperty, session.IsMuted ? "MutedRed" : "AccentBrush");
        // Fixed inner width avoids measuring/layout on every MIDI event.
        Level.Width = 258 * Math.Clamp(session.Volume, 0, 100) / 100;
        if (!IsVisible)
        {
            Opacity = 0;
            Show();
            Position();
            if (SystemParameters.ClientAreaAnimation)
                BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(100)));
            else Opacity = 1;
        }
        _dismiss.Start();
    }

    private void Dismiss(object? sender, EventArgs e)
    {
        _dismiss.Stop();
        if (!SystemParameters.ClientAreaAnimation) { Hide(); return; }
        _fading = true;
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(160));
        fade.Completed += (_, _) =>
        {
            if (!_fading || _disposed) return;
            Hide();
            BeginAnimation(OpacityProperty, null);
        };
        BeginAnimation(OpacityProperty, fade);
    }

    private void Position()
    {
        GetCursorPos(out var cursor);
        var monitor = MonitorFromPoint(cursor, 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) return;
        // Set physical coordinates after Show so mixed-DPI monitors and negative origins work.
        SetWindowPos(_handle, new nint(-1), info.Work.Right - 340, info.Work.Bottom - 140, 0, 0, 0x0011);
        var dpi = GetDpiForWindow(_handle) / 96.0;
        var width = (int)Math.Round(Width * dpi);
        var height = (int)Math.Round(Height * dpi);
        var gap = (int)Math.Round(20 * dpi);
        SetWindowPos(_handle, new nint(-1), info.Work.Right - width - gap, info.Work.Bottom - height - gap, width, height, 0x0010);
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern nint MonitorFromPoint(NativePoint point, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll")] private static extern int GetWindowLong(nint window, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(nint window, int index, int value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
}
