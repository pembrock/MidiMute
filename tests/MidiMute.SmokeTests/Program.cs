using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using MidiMute;
using MidiMute.Models;

var repo = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
var failures = new List<string>();
var passes = new List<string>();

void Check(string name, bool condition, string? detail = null)
{
    if (condition) passes.Add(name);
    else failures.Add(detail == null ? name : $"{name}: {detail}");
}

void Throws<T>(string name, Action action) where T : Exception
{
    try { action(); failures.Add($"{name}: expected {typeof(T).Name}"); }
    catch (T) { passes.Add(name); }
    catch (Exception ex) { failures.Add($"{name}: expected {typeof(T).Name}, got {ex.GetType().Name}"); }
}

var temp = Path.Combine(Path.GetTempPath(), "MidiMute-smoke-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);
try
{
    var storage = new BindingStorage();
    var exportPath = Path.Combine(temp, "settings.json");
    var session = new AppSession
    {
        ProcessName = "Example",
        DisplayName = "Example App",
        Bindings = new List<MidiBinding>
        {
            new() { NoteNumber = 60, NoteName = "C4", Action = BindingAction.VolumeUp, VolumeStep = 7 }
        }
    };
    var master = new AppSession { ProcessName = "__master__", DisplayName = "System" };
    storage.Export(exportPath, new[] { session, master }, true, "Device A", new[] { "HiddenApp" }, Array.Empty<SavedAppProfile>(), AppThemeMode.Dark, AppLanguageMode.English);
    var roundTrip = storage.Import(exportPath);
    Check("settings round-trip", roundTrip.BypassEnabled && roundTrip.MidiDeviceName == "Device A" && roundTrip.Sessions.Single().Bindings.Single().VolumeStep == 7);
    Check("hidden app round-trip", roundTrip.HiddenProcessNames.SequenceEqual(new[] { "HiddenApp" }));
    Check("theme/language round-trip", roundTrip.AppThemeMode == AppThemeMode.Dark && roundTrip.AppLanguageMode == AppLanguageMode.English);
    Check("master excluded from app-profile cache", roundTrip.AppProfiles.All(profile => profile.ProcessName != "__master__"));

    var emptyExport = Path.Combine(temp, "empty-export.json");
    storage.Export(emptyExport, Array.Empty<AppSession>(), false, null, Array.Empty<string>(), Array.Empty<SavedAppProfile>(), AppThemeMode.Auto, AppLanguageMode.Auto);
    Check("empty app-generated export imports", storage.Import(emptyExport).Sessions.Count == 0);

    var emptyObject = Path.Combine(temp, "empty-object.json");
    File.WriteAllText(emptyObject, "{}");
    Throws<InvalidDataException>("empty object rejected", () => storage.Import(emptyObject));

    var nullLists = Path.Combine(temp, "null-lists.json");
    File.WriteAllText(nullLists, "{\"Sessions\":null,\"HiddenProcessNames\":null,\"AppProfiles\":null}");
    var normalizedNulls = storage.Import(nullLists);
    Check("null lists normalized", normalizedNulls.Sessions.Count == 0 && normalizedNulls.HiddenProcessNames.Count == 0 && normalizedNulls.AppProfiles.Count == 0);

    var hostile = Path.Combine(temp, "hostile-values.json");
    File.WriteAllText(hostile, "{\"Sessions\":[{\"ProcessName\":\"x\",\"Bindings\":[{\"NoteNumber\":999,\"NoteName\":null,\"Action\":1,\"VolumeStep\":999}]}],\"AppThemeMode\":999,\"AppLanguageMode\":999}");
    var normalized = storage.Import(hostile);
    var binding = normalized.Sessions.Single().Bindings.Single();
    Check("import note normalized", binding.NoteNumber == 127 && binding.NoteName == "");
    Check("import volume normalized", binding.VolumeStep == 25);
    Check("import enums normalized", normalized.AppThemeMode == AppThemeMode.Auto && normalized.AppLanguageMode == AppLanguageMode.Auto);

    XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
    HashSet<string> ResourceKeys(string file) => XDocument.Load(file).Root!.Elements().Select(e => (string?)e.Attribute(x + "Key")).Where(key => !string.IsNullOrWhiteSpace(key)).Cast<string>().ToHashSet();
    var en = ResourceKeys(Path.Combine(repo, "MidiMute", "Localization", "Strings.en.xaml"));
    var ru = ResourceKeys(Path.Combine(repo, "MidiMute", "Localization", "Strings.ru.xaml"));
    Check("localization key parity", en.SetEquals(ru), $"EN-only={string.Join(',', en.Except(ru))}; RU-only={string.Join(',', ru.Except(en))}");

    var sourceFiles = Directory.EnumerateFiles(Path.Combine(repo, "MidiMute"), "*.*", SearchOption.AllDirectories).Where(f => f.EndsWith(".cs") || f.EndsWith(".xaml"));
    var referenced = new HashSet<string>();
    foreach (var file in sourceFiles)
    {
        var text = File.ReadAllText(file);
        foreach (Match m in Regex.Matches(text, "(?:LocalizationManager\\.(?:Text|Format)\\(\\\"|DynamicResource\\s+)([A-Za-z0-9_.]+)"))
            referenced.Add(m.Groups[1].Value);
    }
    var themeKeys = Directory.EnumerateFiles(Path.Combine(repo, "MidiMute", "Themes"), "*.xaml").SelectMany(ResourceKeys).ToHashSet();
    var missing = referenced.Where(key => !en.Contains(key) && !themeKeys.Contains(key)).OrderBy(key => key).ToList();
    Check("all referenced resources exist", missing.Count == 0, string.Join(',', missing));

    var appSession = new AppSession { ProcessName = "__master__" };
    Check("master cannot hide", !appSession.CanHide);
    appSession.ProcessName = "example";
    Check("normal app can hide", appSession.CanHide);

    var autoStart = typeof(AutoStartManager);
    var extract = autoStart.GetMethod("ExtractExecutablePath", BindingFlags.NonPublic | BindingFlags.Static)!;
    var create = autoStart.GetMethod("CreateStartupCommand", BindingFlags.NonPublic | BindingFlags.Static)!;
    Check("autostart quoted path parsing", (string?)extract.Invoke(null, new object[] { "\"C:\\Program Files\\MidiMute.exe\" --tray" }) == "C:\\Program Files\\MidiMute.exe");
    Check("autostart command includes tray", (string?)create.Invoke(null, new object[] { "C:\\Program Files\\MidiMute.exe" }) == "\"C:\\Program Files\\MidiMute.exe\" --tray");

    var mainSource = File.ReadAllText(Path.Combine(repo, "MidiMute", "MainWindow.xaml.cs"));
    var traySource = File.ReadAllText(Path.Combine(repo, "MidiMute", "TrayMenuViewModel.cs"));
    var notePressBody = mainSource[mainSource.IndexOf("private void OnNotePressed", StringComparison.Ordinal)..mainSource.IndexOf("private void OnNoteReleased", StringComparison.Ordinal)];
    Check("hidden apps do not suppress bindings", !notePressBody.Contains("if (session.IsHidden)"));
    Check("tray exit uses explicit exit path", traySource.Contains("ExitApplication()") && mainSource.Contains("if (!_allowClose)"));
    Check("bypass feedback implemented", mainSource.Contains("Status.Bypassed") && en.Contains("Status.Bypassed") && ru.Contains("Status.Bypassed"));
    var appSource = File.ReadAllText(Path.Combine(repo, "MidiMute", "App.xaml.cs"));
    Check("tray bypass-off uses system menu text", appSource.Contains("SystemColors.MenuTextBrush"));

    var lifecycleCompleted = false;
    var closeToTrayWorked = false;
    Exception? lifecycleError = null;
    var lifecycleThread = new Thread(() =>
    {
        try
        {
            var app = new MidiMute.App();
            app.InitializeComponent();
            app.Startup += (_, _) => app.Dispatcher.BeginInvoke(() =>
            {
                var window = MidiMute.App.MainWin!;
                window.Close();
                closeToTrayWorked = !window.IsVisible;
                window.ExitApplication();
            });
            app.Run();
            lifecycleCompleted = true;
        }
        catch (Exception ex)
        {
            lifecycleError = ex;
        }
    });
    lifecycleThread.SetApartmentState(ApartmentState.STA);
    lifecycleThread.Start();
    var lifecycleJoined = lifecycleThread.Join(TimeSpan.FromSeconds(15));
    Check("window close hides to tray", lifecycleJoined && closeToTrayWorked, lifecycleError?.ToString());
    Check("explicit tray exit terminates app loop", lifecycleJoined && lifecycleCompleted, lifecycleError?.ToString());
}
finally
{
    Directory.Delete(temp, recursive: true);
}

foreach (var pass in passes) Console.WriteLine($"PASS {pass}");
foreach (var failure in failures) Console.WriteLine($"FAIL {failure}");
Console.WriteLine($"SUMMARY {passes.Count} passed, {failures.Count} failed");
return failures.Count == 0 ? 0 : 1;
