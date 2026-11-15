using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MidiMute;
using MidiMute.Models;

internal static class OverlayChecks
{
    public static void Run(string repo, Action<string, bool, string?> check)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            AudioOverlay? overlay = null;
            app.Dispatcher.BeginInvoke(async () =>
            {
                try
                {
                    ThemeManager.SetMode(AppThemeMode.Dark);
                    LocalizationManager.SetMode(AppLanguageMode.Russian);
                    overlay = new AudioOverlay();
                    var foreground = GetForegroundWindow();
                    var session = new AppSession { DisplayName = "Discord", Volume = 42 };
                    overlay.Present(session, true);
                    await Task.Delay(180);
                    check("overlay does not steal focus", GetForegroundWindow() == foreground, null);
                    var handle = new WindowInteropHelper(overlay).Handle;
                    var style = GetWindowLong(handle, -20);
                    check("overlay is click-through and non-activating", (style & 0x080000A0) == 0x080000A0, null);
                    for (int i = 0; i < 100; i++) { session.Volume = i; overlay.Present(session, true); }
                    check("rapid volume updates reuse window", new WindowInteropHelper(overlay).Handle == handle && app.Windows.Count == 1, null);
                    check("latest volume wins", ((TextBlock)overlay.FindName("Percentage")).Text == "99%", null);
                    SavePreview(overlay, Path.Combine(repo, "artifacts", "overlay-dark.png"));
                    await Task.Delay(1100);
                    session.IsMuted = true;
                    overlay.Present(session, false);
                    await Task.Delay(600);
                    check("new event extends visibility", overlay.IsVisible, null);
                    check("mute state localized", ((TextBlock)overlay.FindName("Status")).Text == "Звук выключен", null);
                    ThemeManager.SetMode(AppThemeMode.Light);
                    SavePreview(overlay, Path.Combine(repo, "artifacts", "overlay-light.png"));
                    await Task.Delay(1300);
                    check("overlay hides after inactivity", !overlay.IsVisible, null);
                    session.IsMuted = false;
                    overlay.Present(session, false);
                    check("overlay reopens after dismissal", overlay.IsVisible && ((TextBlock)overlay.FindName("Status")).Text == "Звук включён", null);
                    await Task.Delay(1650);
                    overlay.Present(session, true);
                    await Task.Delay(220);
                    check("event during fade cancels dismissal", overlay.IsVisible && overlay.Opacity > 0.99, null);
                    overlay.Close();
                    overlay.Present(session, false);
                    check("late events after shutdown are ignored", !overlay.IsVisible, null);
                }
                catch (Exception ex) { failure = ex; }
                finally { app.Shutdown(); }
            });
            app.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        var joined = thread.Join(TimeSpan.FromSeconds(15));
        check("overlay lifecycle completes", joined && failure == null, failure?.ToString());
    }

    private static void SavePreview(AudioOverlay overlay, string path)
    {
        overlay.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)overlay.Width, (int)overlay.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(overlay);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern int GetWindowLong(nint window, int index);
}
