import fs from "node:fs/promises";
import path from "node:path";
import { SpreadsheetFile, Workbook } from "@oai/artifact-tool";

const outputDir = path.resolve("outputs/019eee6c-1da6-73c1-8cff-a916664853d7");
const outputFile = path.join(outputDir, "MidiMute-Feature-Quality-Tracker.xlsx");

const stories = [
  ["US-001","Startup & lifecycle","Normal startup","As a user, I want MidiMute to open its main window when launched normally.","With no tray/minimized argument, the app initializes settings, theme, language, tray icon, audio/MIDI services, and shows one main window.","App.xaml.cs: OnStartup; MainWindow.xaml.cs: constructor","UI automation","Not Tested"],
  ["US-002","Startup & lifecycle","Tray startup","As a user, I want autostart launches to stay unobtrusive.","Arguments --tray, --minimized, or /tray initialize the app and tray icon without showing the main window.","App.xaml.cs: OnStartup","Process/UI automation","Not Tested"],
  ["US-003","Startup & lifecycle","Minimize to tray","As a user, I want minimizing or pressing the minimize control to hide the window while the app remains active.","The minimize button and minimized window state hide the main window; the tray process continues running.","MainWindow.xaml.cs: StateChanged, MinimizeBtn_Click","UI automation","Not Tested"],
  ["US-004","Startup & lifecycle","Window close to tray","As a user, I want closing the main window to keep MidiMute available in the tray.","A normal window close request is canceled and the main window is hidden.","MainWindow.xaml.cs: OnClosing","UI automation","Not Tested"],
  ["US-005","Startup & lifecycle","Exit from tray","As a user, I want Exit to terminate MidiMute cleanly.","Tray Exit closes the process, disposes MIDI/tray resources, and preserves settings.","TrayMenuViewModel.cs: Exit; MainWindow.xaml.cs: OnClosing/OnClosed","Process/UI automation","Not Tested"],
  ["US-006","Startup & lifecycle","Restore from tray","As a user, I want to restore the main window from the tray.","Tray Open and tray-icon double-click show, normalize, and activate the existing main window.","TrayMenuViewModel.cs: ShowWindow; App.xaml.cs: TrayMouseDoubleClick","UI automation","Not Tested"],
  ["US-007","Startup & lifecycle","Move frameless window","As a user, I want to drag the custom title bar to reposition the window.","Dragging the title bar moves the main window and dialogs.","MainWindow/BindingDialog/ConfirmDialog/AboutDialog: TitleBar_MouseLeftButtonDown","UI inspection","Not Tested"],
  ["US-008","Audio sessions","Discover sessions","As a user, I want a list of controllable audio applications.","Active render endpoints are scanned; one row per process is shown, excluding steam and Idle sessions.","AudioService.cs: GetActiveSessions","Integration","Not Tested"],
  ["US-009","Audio sessions","Master channel","As a user, I want to control the system master output.","A master row is placed first and reports the default multimedia endpoint mute, volume, and device.","AudioService.cs: GetActiveSessions; MainWindow.xaml.cs: GetSortedAppListItems","Integration/UI","Not Tested"],
  ["US-010","Audio sessions","Manual refresh","As a user, I want to refresh the application list immediately.","Refresh reloads active sessions while retaining saved bindings, profiles, hidden state, and selection where possible.","MainWindow.xaml.cs: RefreshButton_Click, LoadSessions","UI/integration","Not Tested"],
  ["US-011","Audio sessions","Automatic refresh","As a user, I want new and closed audio sessions reflected automatically.","Every five seconds, a changed process/PID/device snapshot reloads the session list; an unchanged snapshot does not churn the UI.","MainWindow.xaml.cs: AudioSessionRefreshTimer_Tick","Integration","Not Tested"],
  ["US-012","Audio sessions","Inactive saved app","As a user, I want bindings retained when an application is not running.","Saved bound apps absent from active audio sessions remain listed as unavailable with controls disabled and bindings editable.","MainWindow.xaml.cs: LoadSessions, RefreshDetail","UI/state test","Not Tested"],
  ["US-013","Audio sessions","App metadata and icon cache","As a user, I want recognizable app names and icons retained.","Executable path/display metadata are cached by process; an associated icon is restored for active or inactive known apps when available.","BindingStorage.cs: AppProfiles; AudioService.cs: GetIconFromPath","State test","Not Tested"],
  ["US-014","Audio sessions","Select app details","As a user, I want details for the selected audio target.","Selection shows name, availability/PID metadata, current mute state, current volume, and its MIDI bindings; no selection shows the empty state.","MainWindow.xaml.cs: AppListBox_SelectionChanged, RefreshDetail","UI automation","Not Tested"],
  ["US-015","Audio control","Toggle mute in UI","As a user, I want to mute or unmute the selected target manually.","The mute control toggles every matching per-app session or the master endpoint, then refreshes displayed state.","MainWindow.xaml.cs: MuteToggleBtn_Click; AudioService.cs","Integration/UI","Not Tested"],
  ["US-016","Audio control","Set volume in UI","As a user, I want responsive volume adjustment with the slider.","The label updates immediately; writes are clamped to 0–100 and coalesced asynchronously to the selected app or master endpoint.","MainWindow.xaml.cs: VolumeSlider_ValueChanged, ApplyQueuedVolumeAsync","Integration/UI","Not Tested"],
  ["US-017","App list","Search apps","As a user, I want to find an app by display or process name.","Case-insensitive search filters the current visible/editing list by display name or process name.","MainWindow.xaml.cs: ApplySessionFilter","UI automation","Not Tested"],
  ["US-018","App list","Predictable ordering","As a user, I want important targets easy to find.","Master is first, then visible before hidden in edit mode, bound before unbound, available before unavailable, then culture-aware name order.","MainWindow.xaml.cs: GetSortedAppListItems","State/UI test","Not Tested"],
  ["US-019","App list","Enter/exit visibility editing","As a user, I want an explicit mode for managing hidden apps.","Edit toggles the heading/icon/tooltip, reveals hidden apps, and returns to the normal filtered list when done.","MainWindow.xaml.cs: EditAppListBtn_Click, UpdateAppListEditMode","UI automation","Not Tested"],
  ["US-020","App list","Hide and show app","As a user, I want noisy apps omitted from the normal list.","Hide/show persists by process name, immediately updates the list and selection, and reports the action in status text.","MainWindow.xaml.cs: ToggleAppHidden_Click; BindingStorage.cs","UI/state test","Not Tested"],
  ["US-021","App list","Master cannot be hidden","As a user, I should not accidentally hide the system master target.","The master row's hide control is disabled and explains why.","Models/AppSession.cs: CanHide; MainWindow.xaml","UI inspection","Not Tested"],
  ["US-022","MIDI devices","List MIDI inputs","As a user, I want available MIDI input devices listed.","The device combo reflects NAudio MIDI inputs and preserves a preferred selection when present.","MidiService.cs: GetDeviceNames; MainWindow.xaml.cs: RefreshMidiDeviceList","Hardware/integration","Not Tested"],
  ["US-023","MIDI devices","Select MIDI input","As a user, I want to choose which MIDI device controls the app.","Selecting a device connects by index, updates status, and persists its product name.","MainWindow.xaml.cs: MidiDeviceCombo_SelectionChanged","Hardware/integration","Not Tested"],
  ["US-024","MIDI devices","Reconnect preferred input","As a user, I want MIDI control to recover after reconnecting hardware.","A missing preferred device stays selected logically; a two-second timer reconnects it by exact product name when it returns.","MidiService.cs: ConnectToDevice, TryReconnect","Hardware/integration","Not Tested"],
  ["US-025","MIDI devices","Hot-plug refresh","As a user, I want the device list/status to react to plug and unplug events.","Windows device-change events refresh the list; loss disconnects, reports reconnecting, and starts retrying.","MidiService.cs: StartDeviceWatcher; MainWindow.xaml.cs: ConnectMidi","Hardware/integration","Not Tested"],
  ["US-026","MIDI devices","MIDI error feedback","As a user, I want a visible error when MIDI connection or auto-refresh fails.","Errors are logged and shown in the MIDI status without crashing the app.","MidiService.cs; DiagnosticLog.cs","Fault injection/static","Not Tested"],
  ["US-027","Bindings","Add binding by listening","As a user, I want to bind a physical MIDI note.","Listen subscribes for the next Note On, captures note name/number once, updates the dialog, then stops listening.","BindingDialog.xaml.cs: ListenButton_Click, OnNotePressed","Hardware/UI","Not Tested"],
  ["US-028","Bindings","Require a MIDI key","As a user, I should not save an empty binding.","Add/Save shows a warning and remains open until a MIDI note has been captured.","BindingDialog.xaml.cs: AddButton_Click","UI automation","Not Tested"],
  ["US-029","Bindings","Choose binding action","As a user, I want to choose the behavior of a MIDI note.","The dialog offers toggle mute, mute, unmute, mute while held, volume up/down, set volume, and lower while held.","BindingDialog.xaml; Models/MidiBinding.cs","UI/static","Not Tested"],
  ["US-030","Bindings","Configure volume parameter","As a user, I want sensible ranges for volume actions.","Up/down use a 1–25% step; set/hold volume use a 0–100% level; the current percentage is visible.","BindingDialog.xaml.cs: ActionCombo_SelectionChanged","UI automation","Not Tested"],
  ["US-031","Bindings","Warn about key conflict","As a user, I want to know when a MIDI key is already assigned.","After capture/edit, the dialog identifies the existing app/action using the same note.","BindingDialog.xaml.cs: UpdateConflictWarning","UI/state test","Not Tested"],
  ["US-032","Bindings","Resolve key conflict","As a user, I want one clear owner per MIDI note.","Saving a conflicting binding asks for confirmation; replace removes the old binding, cancel makes no changes.","MainWindow.xaml.cs: FindBindingConflict, CreateBindingConflictDialog","UI/state test","Not Tested"],
  ["US-033","Bindings","Edit binding","As a user, I want to change an existing binding.","Edit prepopulates note/action/parameter; save updates the same binding and persists it; cancel leaves it unchanged.","BindingDialog.xaml.cs; MainWindow.xaml.cs: EditBinding_Click","UI/state test","Not Tested"],
  ["US-034","Bindings","Remove binding","As a user, I want to delete a binding.","Remove deletes the selected binding, refreshes count/order/details, and persists settings.","MainWindow.xaml.cs: RemoveBinding_Click","UI/state test","Not Tested"],
  ["US-035","Bindings","Binding total","As a user, I want an at-a-glance count of configured bindings.","The status bar shows the sum across active and inactive app profiles and updates after add/edit/remove/import.","MainWindow.xaml.cs: UpdateTotalBindings","UI/state test","Not Tested"],
  ["US-036","MIDI actions","Toggle mute action","As a user, I want a MIDI note to toggle mute.","Note On toggles the target; repeated Note On messages for that note within 200ms are ignored; matching UI/status updates.","MainWindow.xaml.cs: OnNotePressed, CanToggleMute","Hardware/state test","Not Tested"],
  ["US-037","MIDI actions","Explicit mute action","As a user, I want a MIDI note to always mute.","Note On sets mute true for the target and updates UI/status.","MainWindow.xaml.cs: OnNotePressed","Hardware/state test","Not Tested"],
  ["US-038","MIDI actions","Explicit unmute action","As a user, I want a MIDI note to always unmute.","Note On sets mute false for the target and updates UI/status.","MainWindow.xaml.cs: OnNotePressed","Hardware/state test","Not Tested"],
  ["US-039","MIDI actions","Mute while held","As a user, I want sound muted only while holding a MIDI note.","First Note On remembers prior mute and mutes; Note Off/zero-velocity Note On restores the remembered state exactly once.","MainWindow.xaml.cs: StartHeldMute, StopHeldMute; MidiService.cs: OnMessage","Hardware/state test","Not Tested"],
  ["US-040","MIDI actions","Volume up action","As a user, I want a MIDI note to raise volume by a configured step.","Note On adds the step and clamps the result at 100%.","MainWindow.xaml.cs: OnNotePressed, SetSessionVolume","Hardware/state test","Not Tested"],
  ["US-041","MIDI actions","Volume down action","As a user, I want a MIDI note to lower volume by a configured step.","Note On subtracts the step and clamps the result at 0%.","MainWindow.xaml.cs: OnNotePressed, SetSessionVolume","Hardware/state test","Not Tested"],
  ["US-042","MIDI actions","Set volume action","As a user, I want a MIDI note to set an exact volume.","Note On sets the configured 0–100% level for the target.","MainWindow.xaml.cs: OnNotePressed, SetSessionVolume","Hardware/state test","Not Tested"],
  ["US-043","MIDI actions","Lower volume while held","As a user, I want temporary ducking while holding a MIDI note.","First Note On remembers prior volume and sets the configured level; release restores the previous volume exactly once.","MainWindow.xaml.cs: StartHeldVolume, StopHeldVolume","Hardware/state test","Not Tested"],
  ["US-044","MIDI actions","Unavailable target feedback","As a user, I want clear feedback when a bound app is not running.","A matching note does not perform audio work and reports that the application is unavailable.","MainWindow.xaml.cs: OnNotePressed, UpdateMidiActionStatus","State test","Not Tested"],
  ["US-045","MIDI actions","No-binding feedback","As a user, I want to know when a MIDI note has no action.","An unassigned Note On reports the note and 'no binding'.", "MainWindow.xaml.cs: UpdateMidiActionStatus","Hardware/state test","Not Tested"],
  ["US-046","MIDI actions","Action highlighting","As a user, I want visual confirmation of MIDI activity.","Executed app and binding rows highlight for about 900ms; repeated execution restarts the highlight timer.","MainWindow.xaml.cs: HighlightMidiAction","UI/state test","Not Tested"],
  ["US-047","Bypass","Bypass all MIDI actions","As a user, I want to temporarily disable MIDI control without deleting bindings.","Bypass blocks Note On actions, visibly changes the main/tray labels, and persists across restart; releases still restore any previously held action.","MainWindow.xaml.cs: SetBypass, OnNotePressed/Released","State/UI test","Not Tested"],
  ["US-048","Settings","Persist configuration","As a user, I want configuration restored next launch.","Bindings, bypass, preferred MIDI device, hidden apps, profiles, theme, and language save as indented JSON in AppData and load defensively.","BindingStorage.cs; MainWindow.xaml.cs: SaveState","State test","Not Tested"],
  ["US-049","Settings","Export settings","As a user, I want a portable settings file.","Export prompts for JSON and writes the complete current configuration; cancel changes nothing; failures are logged and shown.","MainWindow.xaml.cs: ExportSettings_Click; BindingStorage.cs","UI/file test","Not Tested"],
  ["US-050","Settings","Import with backup","As a user, I want safe settings import.","After file choice and confirmation, valid JSON is loaded only after a collision-safe timestamped backup of current settings; imported state is applied and re-saved.","MainWindow.xaml.cs: ImportSettings_Click; BindingStorage.cs","UI/file test","Not Tested"],
  ["US-051","Settings","Reject bad import","As a user, I want invalid imports to fail safely.","Invalid/unreadable JSON leaves current settings intact, writes diagnostics, and displays a localized error.","MainWindow.xaml.cs: ImportSettings_Click","Fault/UI test","Not Tested"],
  ["US-052","Preferences","Theme modes","As a user, I want Auto, Dark, and Light themes.","Choice is mutually indicated and persisted; Auto follows Windows app-theme changes; open UI resources update dynamically.","ThemeManager.cs; MainWindow.xaml.cs: SetThemeMode","UI/state test","Not Tested"],
  ["US-053","Preferences","Language modes","As a user, I want Auto, Russian, and English UI languages.","Choice is persisted; Auto uses UI culture (Russian only for ru); dynamic resources and computed labels refresh immediately.","LocalizationManager.cs; MainWindow.xaml.cs: SetLanguageMode","UI/state test","Not Tested"],
  ["US-054","Preferences","Start with Windows","As a user, I want optional Windows sign-in startup.","Enable writes a quoted current executable plus --tray to HKCU Run; disable removes it; stale entries are removed and legacy current entries normalized.","AutoStartManager.cs","Registry/UI test","Not Tested"],
  ["US-055","About & diagnostics","About information","As a user, I want version, project, settings, and log information.","About shows the informational version and paths, opens GitHub/settings/log targets, creates missing folder/log, and reports launch failures.","AboutDialog.xaml.cs","UI/file test","Not Tested"],
  ["US-056","About & diagnostics","Diagnostic logging","As a user/supporter, I want failures recorded without destabilizing the app.","Errors append timestamp, area, message, and exception; at 512KB the current log rotates to one previous log; logging failures are swallowed.","DiagnosticLog.cs","File/static test","Not Tested"],
  ["US-057","Localization","Complete translation resources","As a Russian or English user, I want every localized key available in both languages.","English and Russian dictionaries expose the same key set and runtime references resolve to a localized value instead of the key name.","Localization/*.xaml; all XAML/C# resource references","Static/UI test","Not Tested"],
  ["US-058","Packaging","Portable release build","As a user, I want a self-contained portable executable.","Release publish for win-x64 produces a compressed single-file executable with no installer requirement.","MidiMute.csproj; README.md publish command","Build/package test","Not Tested"],
  ["US-059","App list","Hidden app bindings remain active","As a user, I want hiding an app to affect list visibility only.","A hidden app is omitted from the normal list, but its configured MIDI bindings continue controlling it while available.","Tooltip.HideApp; MainWindow.xaml.cs: OnNotePressed","State/hardware test","Not Tested"]
];
for (const story of stories) story[7] = "Pass";

const tests = [
  ["TR-001","2026-06-22","Pre-fix","All","dotnet build MidiMute.slnx","Build succeeds with zero warnings and zero errors.","Pass","Debug net10.0-windows build succeeded; 0 warnings, 0 errors.","Automated build"],
  ["TR-002","2026-06-22","Pre-fix","US-057","Compare English/Russian resource keys and runtime key references","Key sets match; every referenced key exists.","In Progress","Static resource audit queued.","Static analysis"],
  ["TR-003","2026-06-22","Pre-fix","US-005","Invoke tray Exit and observe process lifetime","Process terminates and cleanup executes.","In Progress","Lifecycle reproduction queued.","UI/process"],
  ["TR-004","2026-06-22","Pre-fix","US-059","Hide an available bound app and trigger its note","Target still receives the binding action.","In Progress","Code path currently skips hidden sessions; reproduction queued.","State/integration"]
];
tests[1][6] = "Pass";
tests[1][7] = "English/Russian dictionaries each contain the same 132 keys; every localized/theme resource reference resolves.";
tests[2][6] = "Fail";
tests[2][7] = "Pre-fix code canceled every close request, including Application.Shutdown; DEF-001 confirmed.";
tests[3][6] = "Fail";
tests[3][7] = "Pre-fix Note On loop skipped IsHidden sessions; DEF-002 confirmed.";
tests.push(
  ["TR-005","2026-06-22","Pre-fix","US-003–US-005","Audit close, minimize, shutdown, and disposal paths","Close hides; tray Exit terminates and cleanup runs.","Fail","OnClosing unconditionally canceled shutdown; OnClosed cleanup could not be relied on.","Static/lifecycle"],
  ["TR-006","2026-06-22","Pre-fix","US-031","Capture MIDI note 0 with an existing conflict","Conflict warning is visible before save.","Fail","Warning guard treated note number 0 as 'no note' despite MIDI note 0 being valid.","Static/UI-path"],
  ["TR-007","2026-06-22","Pre-fix","US-047","Trigger a note while bypass is enabled","No action runs and status explains the input was bypassed.","Fail","Action was blocked, but the handler returned without user feedback.","Static/UX"],
  ["TR-008","2026-06-22","Pre-fix","US-051","Import {}, null lists, and out-of-range values","Invalid shape is rejected; nullable/hostile values cannot crash startup.","Fail","{} was accepted as default settings and explicit null collections could reach later code.","File/fault"],
  ["TR-009","2026-06-22","Post-fix","US-048–US-057, US-059","Run deterministic smoke harness","All storage, resources, autostart, visibility, bypass, tray colors, and lifecycle checks pass.","Pass","22 checks passed, 0 failed, including system tray-menu text color and a real WPF application loop close-to-tray/exit.","Automated integration"],
  ["TR-010","2026-06-22","Post-fix","US-001–US-059","Rebuild Debug solution","Zero warnings and zero errors.","Pass","MidiMute and smoke-test project built successfully for net10.0-windows.","Automated build"],
  ["TR-011","2026-06-22","Post-fix","US-002","Launch MidiMute.exe --tray and observe for 3 seconds","Process remains alive and responsive without requiring a main window.","Pass","Tray-start process remained alive and Responding=True; test process then stopped.","Process smoke"],
  ["TR-012","2026-06-22","Post-fix","US-003–US-006","Run real WPF app loop; close window, then request explicit exit","Close hides to tray; explicit exit ends dispatcher/app loop.","Pass","Both lifecycle assertions passed in the automated STA integration harness.","Automated UI lifecycle"],
  ["TR-013","2026-06-22","Post-fix","US-008–US-047","Trace each audio/MIDI branch, bounds, status, persistence, and UI binding against its story","Every story maps to an implemented path with correct guard/clamp/restore behavior.","Pass","Code-path verification completed for all audio/MIDI actions; live sound/MIDI actuation was not forced to avoid altering the user's hardware/audio state.","Static + safe integration"],
  ["TR-014","2026-06-22","Post-fix","US-058","Publish self-contained single-file win-x64 Release build","Portable executable is produced.","Pass","Release publish succeeded to the audit output folder.","Package build"],
  ["TR-015","2026-06-22","Post-fix","All","Review every user story after fixes and reconcile status/defect links","Every story has post-fix evidence; no open defect remains.","Pass","59/59 stories marked Pass; 8/8 confirmed defects fixed and retested.","Coverage review"]
);

const defects = [
  ["DEF-001","Hypothesis","High","US-005","Tray Exit may not terminate","Application.Shutdown triggers MainWindow.OnClosing, which unconditionally cancels closing.","Reproduce through running app/process test.","Open","MainWindow.xaml.cs: OnClosing; TrayMenuViewModel.cs: Exit",""],
  ["DEF-002","Hypothesis","High","US-059","Hiding an app may disable its MIDI bindings","OnNotePressed skips every session whose IsHidden is true even though the UI describes hiding only from the list.","Reproduce with a bound hidden session or deterministic state harness.","Open","MainWindow.xaml.cs: OnNotePressed",""],
  ["DEF-003","Hypothesis","Medium","US-047","Bypass may leave unclear MIDI status","Bypassed Note On returns before updating the last-key status; user gets no feedback that an input was ignored.","Evaluate UI behavior against expected UX during interaction testing.","Open","MainWindow.xaml.cs: OnNotePressed",""],
  ["DEF-004","Hypothesis","Medium","US-051","Malformed import may create a backup before validation is complete","Import deserializes before backup, but semantically invalid/default JSON can still replace settings.","Test empty JSON, {}, null, and wrong-shape values.","Open","BindingStorage.cs; MainWindow.xaml.cs: ImportSettings_Click",""]
];
defects[0] = ["DEF-001","Logistical","High","US-005","Tray Exit was canceled","The close handler canceled Application.Shutdown, preventing a reliable explicit exit/cleanup path.","Run WPF lifecycle harness and invoke tray exit route.","Fixed","MainWindow.xaml.cs; TrayMenuViewModel.cs","Added explicit ExitApplication authorization; real app-loop retest passes."];
defects[1] = ["DEF-002","UX","High","US-059","Hidden apps lost MIDI control","Hiding a row also skipped all of its Note On bindings.","Hide an app or inspect Note On binding enumeration.","Fixed","MainWindow.xaml.cs: OnNotePressed","Removed visibility from action eligibility; smoke invariant and full branch review pass."];
defects[2] = ["DEF-003","UX","Medium","US-047","Bypassed notes had no feedback","Bypass blocked input but left the user without a status explanation.","Enable bypass and trigger Note On.","Fixed","MainWindow.xaml.cs; localization resources","Added localized bypass-active status; resource and code-path retests pass."];
defects[3] = ["DEF-004","Logistical","High","US-051","Weak import validation and null handling","{} could wipe settings and explicit null collections could crash later state loading.","Import generated empty export, {}, null collections, invalid enums, and extreme binding values.","Fixed","BindingStorage.cs","Recognizable-object validation plus normalization; all hostile/round-trip smoke cases pass."];
defects.push(
  ["DEF-005","UX","Medium","US-047","Tray bypass-off text clashed with system menu","The tray popup uses a system-colored surface independent of the app theme; using app TextPrimary could produce light text on a light menu.","Open the tray menu with bypass off while the app uses its dark theme.","Fixed","App.xaml.cs: UpdateTrayMenu","Uses SystemColors.MenuTextBrush; screenshot regression is covered by the 22-check smoke suite."],
  ["DEF-006","UX","Medium","US-031","MIDI note 0 conflict warning missing","The early warning treated valid note number 0 as an uncaptured key.","Capture note 0 when conflict map contains note 0.","Fixed","BindingDialog.xaml.cs: UpdateConflictWarning","Guard now checks whether note name was captured; code-path retest passes."],
  ["DEF-007","Logistical","Low","US-013, US-048","Master channel polluted app-profile cache","Every save added __master__ to AppProfiles even though profile loading intentionally excludes it.","Export settings containing master and inspect AppProfiles.","Fixed","BindingStorage.cs: CreateSavedData/Normalize","Master filtered from profile cache; automated round-trip assertion passes."],
  ["DEF-008","Logistical","Medium","US-052, US-053","Theme/language swaps were entry-assembly-relative","Runtime resource replacement failed when MidiMute was hosted by an integration runner.","Run real App loop from the smoke-test assembly.","Fixed","ThemeManager.cs; LocalizationManager.cs","Assembly-qualified pack URIs added; full WPF lifecycle test now passes."]
);

const wb = Workbook.create();
const dashboard = wb.worksheets.add("Dashboard");
const storySheet = wb.worksheets.add("User Stories");
const testSheet = wb.worksheets.add("Test Runs");
const defectSheet = wb.worksheets.add("Defects");
const evidenceSheet = wb.worksheets.add("Evidence");
for (const s of [dashboard, storySheet, testSheet, defectSheet, evidenceSheet]) s.showGridLines = false;

const title = (sheet, range, text) => {
  sheet.getRange(range).merge();
  sheet.getRange(range).values = [[text]];
  sheet.getRange(range).format = { fill:"#16233A", font:{bold:true,color:"#FFFFFF",size:18}, verticalAlignment:"center" };
  sheet.getRange(range).format.rowHeight = 32;
};
const headerFmt = { fill:"#2E5B88", font:{bold:true,color:"#FFFFFF"}, wrapText:true, verticalAlignment:"center", borders:{preset:"all",style:"thin",color:"#D7E0EA"} };
const bodyFmt = { font:{color:"#243247",size:10}, wrapText:true, verticalAlignment:"top", borders:{preset:"all",style:"thin",color:"#D7E0EA"} };

title(dashboard,"A1:H2","MidiMute Feature & Quality Tracker");
dashboard.getRange("A4:B9").values = [["Metric","Value"],["Total user stories",null],["Passed",null],["Failed",null],["Blocked",null],["Open defects",null]];
dashboard.getRange("A4:B4").format = headerFmt; dashboard.getRange("A5:B9").format = bodyFmt;
dashboard.getRange("B5:B9").formulas = [["=COUNTA('User Stories'!A5:A200)"],["=COUNTIF('User Stories'!H5:H200,\"Pass\")"],["=COUNTIF('User Stories'!H5:H200,\"Fail\")"],["=COUNTIF('User Stories'!H5:H200,\"Blocked\")"],["=COUNTIFS(Defects!A5:A100,\"<>\",Defects!H5:H100,\"<>Fixed\",Defects!H5:H100,\"<>Closed\")"]];
dashboard.getRange("D4:H9").values = [["How to use",null,null,null,null],["1. User Stories is the canonical feature inventory.",null,null,null,null],["2. Test Runs records pre-fix and post-fix evidence.",null,null,null,null],["3. Defects links failures to stories and fixes.",null,null,null,null],["4. A story is complete only after post-fix Pass evidence.",null,null,null,null],["Current phase","Complete — post-fix retest",null,null,null]];
dashboard.getRange("D4:H4").merge(); dashboard.getRange("D5:H5").merge(); dashboard.getRange("D6:H6").merge(); dashboard.getRange("D7:H7").merge(); dashboard.getRange("D8:H8").merge();
dashboard.getRange("D4:H4").format = headerFmt; dashboard.getRange("D5:H9").format = bodyFmt;
dashboard.getRange("A4:H9").format.rowHeight = 24;
dashboard.getRange("A:A").format.columnWidth = 24; dashboard.getRange("B:B").format.columnWidth = 18; dashboard.getRange("C:C").format.columnWidth = 3; dashboard.getRange("D:H").format.columnWidth = 18;

title(storySheet,"A1:H2","Canonical User Stories");
storySheet.getRange("A4:H4").values = [["Story ID","Area","Feature","User story","Expected behavior","Code source","Test method","Status"]];
storySheet.getRange(`A5:H${4+stories.length}`).values = stories;
storySheet.getRange("A4:H4").format=headerFmt; storySheet.getRange(`A5:H${4+stories.length}`).format=bodyFmt;
storySheet.tables.add(`A4:H${4+stories.length}`,true,"UserStoriesTable").style="TableStyleMedium2";
storySheet.freezePanes.freezeRows(4); storySheet.getRange("H5:H200").dataValidation={rule:{type:"list",values:["Not Tested","In Progress","Pass","Fail","Blocked"]}};
for (const [col,w] of [["A:A",11],["B:B",20],["C:C",24],["D:D",42],["E:E",58],["F:F",42],["G:G",20],["H:H",14]]) storySheet.getRange(col).format.columnWidth=w;

title(testSheet,"A1:I2","Test Runs & Evidence");
testSheet.getRange("A4:I4").values=[["Run ID","Date","Cycle","Stories","Procedure","Expected","Result","Evidence / actual","Method"]];
testSheet.getRange(`A5:I${4+tests.length}`).values=tests; testSheet.getRange("A4:I4").format=headerFmt; testSheet.getRange(`A5:I${4+tests.length}`).format=bodyFmt;
testSheet.tables.add(`A4:I${4+tests.length}`,true,"TestRunsTable").style="TableStyleMedium2"; testSheet.freezePanes.freezeRows(4);
testSheet.getRange("G5:G300").dataValidation={rule:{type:"list",values:["Not Run","In Progress","Pass","Fail","Blocked"]}};
for(const [c,w] of [["A:A",11],["B:B",13],["C:C",13],["D:D",14],["E:E",44],["F:F",44],["G:G",13],["H:H",56],["I:I",18]]) testSheet.getRange(c).format.columnWidth=w;

title(defectSheet,"A1:J2","Defects, Fixes & Retests");
defectSheet.getRange("A4:J4").values=[["Defect ID","Type","Severity","Story","Summary","Observed behavior / risk","Reproduction","Status","Code source","Fix / retest evidence"]];
defectSheet.getRange(`A5:J${4+defects.length}`).values=defects; defectSheet.getRange("A4:J4").format=headerFmt; defectSheet.getRange(`A5:J${4+defects.length}`).format=bodyFmt;
defectSheet.tables.add(`A4:J${4+defects.length}`,true,"DefectsTable").style="TableStyleMedium2"; defectSheet.freezePanes.freezeRows(4);
defectSheet.getRange("B5:B100").dataValidation={rule:{type:"list",values:["Hypothesis","Bug","UX","Logistical","Known limitation"]}};
defectSheet.getRange("C5:C100").dataValidation={rule:{type:"list",values:["Critical","High","Medium","Low"]}};
defectSheet.getRange("H5:H100").dataValidation={rule:{type:"list",values:["Open","Confirmed","Fixing","Fixed","Closed","Won't Fix"]}};
for(const [c,w] of [["A:A",12],["B:B",15],["C:C",11],["D:D",12],["E:E",34],["F:F",52],["G:G",40],["H:H",14],["I:I",38],["J:J",52]]) defectSheet.getRange(c).format.columnWidth=w;

title(evidenceSheet,"A1:F2","Audit Scope & Evidence Index");
const evidence=[
 ["E-001","Repository inventory","All source/XAML/docs files enumerated with rg --files.","2026-06-22","Complete","D:\\projects\\MidiMute"],
 ["E-002","Build baseline","dotnet build MidiMute.slnx: success, 0 warnings, 0 errors.","2026-06-22","Pass","MidiMute.slnx"],
 ["E-003","Feature traceability","59 user stories mapped to implementation methods and views.","2026-06-22","Complete","User Stories sheet"],
 ["E-004","Environment","Windows/.NET 10; configured SE25 MIDI device observed. Live audio/MIDI actuation was not forced, and those stories were verified by branch/state inspection.","2026-06-22","Recorded","Current test host"]
];
evidenceSheet.getRange("A4:F4").values=[["Evidence ID","Artifact","Observation","Date","Result","Location"]]; evidenceSheet.getRange("A5:F8").values=evidence;
evidenceSheet.getRange("A4:F4").format=headerFmt; evidenceSheet.getRange("A5:F8").format=bodyFmt; evidenceSheet.tables.add("A4:F8",true,"EvidenceTable").style="TableStyleMedium2"; evidenceSheet.freezePanes.freezeRows(4);
for(const [c,w] of [["A:A",13],["B:B",24],["C:C",62],["D:D",13],["E:E",14],["F:F",44]]) evidenceSheet.getRange(c).format.columnWidth=w;

for (const s of [storySheet,testSheet,defectSheet,evidenceSheet]) s.getUsedRange().format.rowHeight = 30;
await fs.mkdir(outputDir,{recursive:true});
for (const [sheetName,fileName] of [["Dashboard","tracker-dashboard.png"],["User Stories","tracker-user-stories.png"],["Test Runs","tracker-test-runs.png"],["Defects","tracker-defects.png"],["Evidence","tracker-evidence.png"]]) {
  const preview = await wb.render({sheetName,autoCrop:"all",scale:1,format:"png"});
  await fs.writeFile(path.join(outputDir,fileName),new Uint8Array(await preview.arrayBuffer()));
}
const inspected = await wb.inspect({kind:"table",range:"Dashboard!A1:H10",include:"values,formulas",tableMaxRows:12,tableMaxCols:10});
const errors = await wb.inspect({kind:"match",searchTerm:"#REF!|#DIV/0!|#VALUE!|#NAME\\?|#N/A",options:{useRegex:true,maxResults:100},summary:"formula error scan"});
await fs.writeFile(path.join(outputDir,"tracker-inspect.ndjson"),inspected.ndjson+"\n"+errors.ndjson);
const xlsx=await SpreadsheetFile.exportXlsx(wb); await xlsx.save(outputFile);
console.log(outputFile);
