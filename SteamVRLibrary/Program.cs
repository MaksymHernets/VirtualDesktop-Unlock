using System.Diagnostics;

namespace SteamVRLibrary;

internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        if (args.SequenceEqual(new[] { "--restart-streamer" })) return StreamerControl.RestartInstalledStreamer();
        ApplicationConfiguration.Initialize();
        if (args.Contains("--self-test")) return SelfTests.Run();
        Localizer.Initialize();
        if (args.Length == 2 && args[0] == "--render-preview")
        {
            using var form = new LibraryForm(preview: true);
            form.LoadPreview();
            form.ShowInTaskbar = false;
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-20000, -20000);
            form.Show();
            Application.DoEvents();
            // DrawToBitmap renders the app's own controls without operating any other window.
            using var bitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
            bitmap.Save(args[1]);
            return 0;
        }
        using var mutex = new Mutex(true, SingleInstance.MutexName, out var first);
        if (!first)
        {
            if (!SingleInstance.ActivateExisting())
                LocalizedMessageBox.Show("The existing library window did not respond. Close that instance in Task Manager and try again.", "Virtual Desktop Unlock", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return 0;
        }
        using var mainForm = new LibraryForm();
        StartupLog("Form constructed on desktop " + NativeWindowActivation.DesktopName);
        // Ensure activation requests can be marshalled to the UI thread before the message loop starts.
        _ = mainForm.Handle;
        StartupLog("Form handle created");
        using var activation = new SingleInstance(mainForm);
        StartupLog("Activation listener started");
        EventHandler? showOnIdle = null;
        showOnIdle = (_, _) =>
        {
            Application.Idle -= showOnIdle;
            mainForm.RestoreAndActivate();
            StartupLog($"Restored: visible={mainForm.Visible}; bounds={mainForm.Bounds}; state={mainForm.WindowState}");
        };
        Application.Idle += showOnIdle;
        mainForm.Shown += (_, _) => StartupLog($"Shown: visible={mainForm.Visible}; bounds={mainForm.Bounds}");
        StartupLog("Starting message loop");
        Application.Run(mainForm);
        StartupLog("Message loop exited");
        return 0;
    }

    private static void StartupLog(string text)
    {
        try { File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "startup.log"), $"{DateTimeOffset.Now:O} [{Environment.ProcessId}] {text}{Environment.NewLine}"); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

public sealed class LibraryForm : Form
{
    private static readonly Color Background = Color.FromArgb(14, 20, 31);
    private static readonly Color Card = Color.FromArgb(23, 32, 47);
    private static readonly Color Muted = Color.FromArgb(159, 176, 199);
    private static readonly Color Accent = Color.FromArgb(108, 218, 188);
    private readonly bool preview;
    private readonly TraySettings traySettings;
    private readonly BitratePreferences bitratePreferences;
    private readonly Button bitrate = MakeButton("Remove bitrate limits", primary: true);
    private readonly Button bitrateProfiles = MakeButton("Bitrate profiles…");
    private readonly Label bitrateStatus = TextLabel("Stock bitrate limits", Muted, 9);
    private readonly NotifyIcon trayIcon = new();
    private readonly StatusIcons statusIcons = new();
    internal ConnectionIcon CurrentIconState { get; private set; } = ConnectionIcon.Unloaded;
    private readonly ContextMenuStrip trayMenu = new();
    private bool exiting;
    private readonly LibraryStore store;
    private readonly SharedOverrides sharedOverrides;
    private readonly ListView steam = MakeList();
    private readonly ListView vr = MakeList();
    private readonly TextBox search = new() { PlaceholderText = "Search games or Steam app ID…", BorderStyle = BorderStyle.FixedSingle, Dock = DockStyle.Fill };
    private readonly Label steamCount = TextLabel("Loading installed games…", Muted, 9);
    private readonly Label vrCount = TextLabel("Saved on this PC", Muted, 9);
    private readonly Label status = TextLabel("Loading…", Muted, 9);
    private readonly Label connectionStatus = TextLabel("", Accent, 10);
    private readonly LinkLabel restart = new() { Text = "Restart Streamer and reconnect to apply custom additions", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, LinkColor = Accent, ActiveLinkColor = Color.White, VisitedLinkColor = Accent, Visible = false, AccessibleName = "Restart Streamer" };
    private readonly IStreamerControl streamerControl;
    private readonly System.Windows.Forms.Timer restartTimer = new() { Interval = 3000 };
    private uint[]? syncedIds;
    private readonly Button add = MakeButton("Add to VR  →", primary: true);
    private readonly Button remove = MakeButton("←  Remove");
    private readonly Button launch = MakeButton("Launch selected");
    private readonly Button undo = MakeButton("Undo move");
    private readonly Button refresh = MakeButton("Refresh");
    private readonly Button details = MakeButton("Scan details");
    private readonly ToolTip tip = new() { AutoPopDelay = 10000 };
    private ColumnStyle transferColumn = null!;
    private ScanResult scan = new([], [], []);
    private LibraryState state = new();
    private LibraryState? previous;
    private bool updating;
    private bool ready;
    private bool busy;

    public LibraryForm(bool preview = false, string? libraryPath = null, IStreamerControl? streamerControl = null, TraySettings? traySettings = null)
    {
        this.preview = preview;
        this.streamerControl = streamerControl ?? new StreamerControl();
        store = new LibraryStore(libraryPath ?? Path.Combine(AppContext.BaseDirectory, "Data", "library.json"));
        this.traySettings = traySettings ?? new TraySettings(preview || libraryPath != null
            ? Path.Combine(Path.GetDirectoryName(store.Path)!, "Settings.json") : TraySettings.DefaultPath);
        bitratePreferences = new BitratePreferences(preview || libraryPath != null
            ? Path.Combine(Path.GetDirectoryName(store.Path)!, "BitrateOverrides.xml") : VirtualDesktop.Unlock.BitrateConfiguration.DefaultPath);
        bitratePreferences.Changed += BitrateChanged;
        Localizer.Changed += LanguageChanged;
        sharedOverrides = new SharedOverrides(preview || libraryPath != null
            ? Path.Combine(Path.GetDirectoryName(store.Path)!, "GameOverrides.xml") : SharedOverrides.DefaultPath);
        Text = "Virtual Desktop Unlock — v0.1.7";
        Icon = statusIcons[ConnectionIcon.Unloaded];
        trayIcon.Icon = Icon;
        trayIcon.Text = "Virtual Desktop Unlock";
        trayMenu.Items.Add("Open", null, (_, _) => RestoreAndActivate());
        trayMenu.Items.Add("Settings", null, (_, _) => OpenSettings());
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("Exit", null, (_, _) => ExitApplication());
        trayIcon.ContextMenuStrip = trayMenu;
        trayIcon.DoubleClick += (_, _) => RestoreAndActivate();
        this.traySettings.Changed += TraySettingChanged;
        trayIcon.Visible = !preview && this.traySettings.Enabled;
        BackColor = Background;
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10);
        ClientSize = new Size(1160, 754);
        MinimumSize = new Size(990, 664);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(28, 22, 28, 18), ColumnCount = 1, RowCount = 9, BackColor = Background };
        foreach (var height in new[] { 50, 30, 54, 48 }) root.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        Controls.Add(root);

        var titleRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4 };
        titleRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var title = TextLabel("Virtual Desktop Unlock", Color.White, 24, FontStyle.Bold);
        title.AutoSize = true;
        title.Dock = DockStyle.None;
        title.Margin = new Padding(0, 0, 10, 0);
        var heading = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        heading.Controls.Add(title);
        var settings = MakeButton("⚙");
        settings.Size = new Size(40, 40);
        settings.Margin = new Padding(0, 0, 0, 0);
        settings.Font = new Font("Segoe UI Symbol", 21);
        settings.AccessibleName = "Settings";
        Localizer.SetToolTip(tip, settings, "Settings");
        settings.Click += (_, _) => OpenSettings();
        heading.Controls.Add(settings);
        titleRow.Controls.Add(heading, 0, 0);
        bitrate.Dock = bitrateProfiles.Dock = refresh.Dock = DockStyle.Fill;
        foreach(var button in new[]{bitrate,bitrateProfiles,refresh}) { button.AutoSize = true; button.AutoSizeMode = AutoSizeMode.GrowAndShrink; button.Padding = new Padding(8,0,8,0); }
        bitrate.Margin = bitrateProfiles.Margin = new Padding(8, 3, 0, 8);
        refresh.Margin = new Padding(8, 3, 0, 8);
        titleRow.Controls.Add(bitrate, 1, 0);
        titleRow.Controls.Add(bitrateProfiles, 2, 0);
        titleRow.Controls.Add(refresh, 3, 0);
        root.Controls.Add(titleRow, 0, 0);
        root.Controls.Add(TextLabel("Choose the games you want to play in VR.", Muted, 11), 0, 1);

        connectionStatus.Padding = new Padding(14, 0, 8, 0);
        connectionStatus.Margin = new Padding(0, 5, 0, 7);
        connectionStatus.AccessibleName = "Virtual Desktop status";
        Localizer.SetToolTip(tip, connectionStatus, "Streamer and library status. Use the restart link to apply pending changes.");
        root.Controls.Add(connectionStatus, 0, 2);
        search.BackColor = Card;
        search.ForeColor = Color.White;
        search.Font = new Font("Segoe UI", 11);
        search.Margin = new Padding(0, 4, 0, 10);
        search.AccessibleName = "Search games";
        var searchRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
        searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62));
        searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        searchRow.Controls.Add(TextLabel("Search", Muted, 10), 0, 0);
        searchRow.Controls.Add(search, 1, 0);
        root.Controls.Add(searchRow, 0, 3);

        var lists = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Margin = Padding.Empty };
        lists.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        lists.ColumnStyles.Add(transferColumn = new ColumnStyle(SizeType.Absolute, 154));
        lists.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        lists.Controls.Add(MakeCard("Steam games", "Not in your VR library", steamCount, steam), 0, 0);
        lists.Controls.Add(MakeCard("VD cache games", "Selected for the headset Games panel", vrCount, vr), 2, 0);
        var center = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(12, 0, 12, 0) };
        center.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        center.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        center.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        center.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        add.Dock = remove.Dock = DockStyle.Fill;
        add.Margin = new Padding(0, 0, 0, 8);
        remove.Margin = new Padding(0, 0, 0, 8);
        center.Controls.Add(add, 0, 1);
        center.Controls.Add(remove, 0, 2);
        lists.Controls.Add(center, 1, 0);
        root.Controls.Add(lists, 0, 4);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 12, 0, 0), WrapContents = false, Margin = Padding.Empty };
        launch.Width = 154; undo.Width = 116; details.Width = 110;
        actions.Controls.AddRange([launch, undo, details]);
        bitrate.AccessibleName = "Toggle bitrate limits";
        bitrate.Click += (_, _) =>
        {
            try { bitratePreferences.Toggle(); }
            catch (Exception e) { LocalizedMessageBox.Show(this, e.Message, "Could not change bitrate limits", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        };
        bitrateProfiles.Click += (_, _) => { using var dialog = new BitrateProfilesForm(bitratePreferences); dialog.Icon = Icon; dialog.ShowDialog(this); };
        Localizer.SetToolTip(tip, bitrate, "Configure codec bitrate ceilings. Requires a compatible helper; headset behavior still needs testing.");
        root.Controls.Add(actions, 0, 5);
        status.AutoEllipsis = true;
        root.Controls.Add(status, 0, 6);
        root.Controls.Add(restart, 0, 7);
        root.Controls.Add(bitrateStatus, 0, 8);
        Localizer.SetToolTip(tip, restart, "Close and relaunch Streamer, then reconnect your headset. Windows may request administrator permission.");
        restart.LinkClicked += async (_, _) => await RestartStreamerAsync();
        restartTimer.Tick += (_, _) => UpdateRestartNotice();
        if (!preview) restartTimer.Start();
        steam.AccessibleName = "Installed Steam games not in your VR library";
        vr.AccessibleName = "VD cache games";
        Localizer.SetToolTip(tip, add, "Add selected games to the headset library. Restart Streamer to apply.");
        Localizer.SetToolTip(tip, remove, "Remove custom additions. Native VR games may still appear automatically.");
        Localizer.SetToolTip(tip, launch, "Launch through Steam. Connect Virtual Desktop first and install any required VR mod yourself.");
        search.TextChanged += (_, _) => Populate();
        steam.SelectedIndexChanged += (_, _) => SelectionChanged(steam, vr);
        vr.SelectedIndexChanged += (_, _) => SelectionChanged(vr, steam);
        add.Click += (_, _) => MoveGames(true);
        remove.Click += (_, _) => MoveGames(false);
        undo.Click += (_, _) => Undo();
        refresh.Click += async (_, _) => await ReloadAsync();
        launch.Click += (_, _) => LaunchSelected();
        steam.DoubleClick += (_, _) => MoveGames(true);
        vr.DoubleClick += (_, _) => MoveGames(false);
        details.Click += (_, _) => LocalizedMessageBox.Show(this,
            (scan.Warnings.Count == 0 ? Localizer.T("All available Steam folders were scanned successfully.") : string.Join("\n\n", scan.Warnings))
            + "\n\n" + Localizer.T("Selections") + ": " + store.Path + "\n\n" + Localizer.T("Shared overrides") + ": " + sharedOverrides.Path + "\n\n" + Localizer.T("Restart Streamer after changing selections. Native VR games may still appear without custom additions."),
            "Library details", MessageBoxButtons.OK, MessageBoxIcon.Information);
        if (!preview) Shown += async (_, _) => await ReloadAsync();
        Localizer.Apply(this); Localizer.ApplyMenu(trayMenu);
        AdjustLocalizedLayout();
        UpdateButtons();
    }

    private static Label TextLabel(string text, Color color, float size, FontStyle style = FontStyle.Regular) => new()
    { Text = text, ForeColor = color, Font = new Font("Segoe UI", size, style), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty };

    private static Button MakeButton(string text, bool primary = false)
    {
        var b = new ThemeButton { Text = text, Height = 34, Width = 128, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, BackColor = primary ? Accent : Card, ForeColor = primary ? Background : Color.White, Font = new Font("Segoe UI", 10, FontStyle.Bold), Margin = new Padding(8, 0, 0, 0), UseVisualStyleBackColor = false };
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(140, 236, 209) : Color.FromArgb(40, 54, 76);
        return b;
    }

    private static ListView MakeList()
    {
        var list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = true, BorderStyle = BorderStyle.None, BackColor = Card, ForeColor = Color.FromArgb(231, 237, 245), HeaderStyle = ColumnHeaderStyle.None, ShowItemToolTips = true, Font = new Font("Segoe UI", 10), Margin = Padding.Empty };
        list.Columns.Add("Game", 320);
        list.Columns.Add("Status", 90);
        list.Resize += (_, _) => { if (list.Columns.Count == 2) list.Columns[0].Width = Math.Max(180, list.ClientSize.Width - list.Columns[1].Width); };
        return list;
    }

    private static Control MakeCard(string heading, string subtitle, Label count, ListView list)
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Card, Padding = new Padding(16, 12, 16, 10), ColumnCount = 1, RowCount = 4, Margin = Padding.Empty };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        panel.Controls.Add(TextLabel(heading, Color.White, 16, FontStyle.Bold), 0, 0);
        panel.Controls.Add(TextLabel(subtitle, Muted, 9), 0, 1);
        panel.Controls.Add(list, 0, 2);
        panel.Controls.Add(count, 0, 3);
        return panel;
    }

    private async Task ReloadAsync()
    {
        busy = true; Localizer.SetText(status, "Scanning installed Steam games…"); UpdateButtons();
        try
        {
            var next = await Task.Run(() => SteamScanner.Scan());
            var saved = store.Load();
            if (saved == null)
            {
                // A saved empty library remains empty on refresh and next launch.
                saved = new LibraryState { Games = next.Games.Where(g => next.CachedVrIds.Contains(g.Id)).Select(g => g with { Origin = "Imported from Virtual Desktop cache" }).ToList() };
                store.Save(saved);
            }
            scan = next; state = saved; previous = null; ready = true;
            Populate();
            SyncOverrides("Loaded library");
        }
        catch (Exception e)
        { ready = false; Localizer.SetText(status, "Could not load the library. Your saved selections were left unchanged."); LocalizedMessageBox.Show(this, e.Message, "Could not load library", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { busy = false; UpdateButtons(); }
    }

    public void LoadPreview(ScanResult? previewScan = null)
    {
        scan = previewScan ?? SteamScanner.Scan();
        state = store.Load() ?? new LibraryState { Games = scan.Games.Where(g => scan.CachedVrIds.Contains(g.Id)).Select(g => g with { Origin = "Imported from Virtual Desktop cache" }).ToList() };
        ready = true;
        Populate();
        Localizer.SetText(status, "Saved automatically  ·  Connect Virtual Desktop before launching a VR game.");
        PerformLayout();
    }

    private void Populate()
    {
        updating = true;
        try
        {
            var selectedSteam = steam.SelectedItems.Cast<ListViewItem>().Select(x => ((Game)x.Tag!).Id).ToHashSet();
            var selectedVr = vr.SelectedItems.Cast<ListViewItem>().Select(x => ((Game)x.Tag!).Id).ToHashSet();
            var ids = state.Games.Select(g => g.Id).ToHashSet();
            var installed = scan.Games.ToDictionary(g => g.Id);
            var left = scan.Games.Where(g => !ids.Contains(g.Id)).ToList();
            var right = state.Games.Select(g => installed.TryGetValue(g.Id, out var current) ? current with { Origin = g.Origin } : g).OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            bool Match(Game g) => g.Name.Contains(search.Text.Trim(), StringComparison.CurrentCultureIgnoreCase) || g.Id.Contains(search.Text.Trim());
            Fill(steam, left.Where(Match), selectedSteam);
            Fill(vr, right.Where(Match), selectedVr);
            Localizer.SetText(steamCount,"{0} of {1} games",steam.Items.Count,left.Count);
            Localizer.SetText(vrCount,"{0} of {1} games",vr.Items.Count,right.Count);
        }
        finally { updating = false; UpdateButtons(); }
    }

    private static void Fill(ListView list, IEnumerable<Game> games, HashSet<string> selected)
    {
        list.BeginUpdate();
        try
        {
            list.Items.Clear();
            foreach (var game in games)
            {
                var item = new ListViewItem(game.Name) { Tag = game, ToolTipText = Localizer.T("Steam app {0}",game.Id)+$"\n{game.InstallDirectory}\n{Localizer.T(game.Origin)}", Selected = selected.Contains(game.Id) };
                item.SubItems.Add(Localizer.T(game.Installed ? "Installed" : "Not installed"));
                list.Items.Add(item);
            }
        }
        finally { list.EndUpdate(); }
    }

    private void SelectionChanged(ListView source, ListView other)
    {
        if (updating) return;
        if (source.SelectedItems.Count > 0)
        { updating = true; foreach (var item in other.SelectedItems.Cast<ListViewItem>().ToArray()) item.Selected = false; updating = false; }
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        UpdateRestartNotice();
        restart.Enabled = ready && !busy;
        bitrate.Enabled = bitrateProfiles.Enabled = !busy;
        add.Enabled = ready && !busy && steam.SelectedItems.Count > 0;
        remove.Enabled = ready && !busy && vr.SelectedItems.Count > 0;
        launch.Enabled = ready && !busy && steam.SelectedItems.Count + vr.SelectedItems.Count == 1;
        undo.Enabled = ready && !busy && previous != null;
        refresh.Enabled = !busy;
        search.Enabled = ready && !busy;
    }

    private void MoveGames(bool toVr)
    {
        if (!ready || busy) return;
        var selected = (toVr ? steam : vr).SelectedItems.Cast<ListViewItem>().Select(x => (Game)x.Tag!).ToList();
        if (selected.Count == 0) return;
        var ids = selected.Select(g => g.Id).ToHashSet();
        var next = new LibraryState { Games = toVr
            ? state.Games.Concat(selected.Where(g => !state.Games.Any(x => x.Id == g.Id)).Select(g => g with { Origin = "Added by you" })).ToList()
            : state.Games.Where(g => !ids.Contains(g.Id)).ToList() };
        try
        {
            store.Save(next); previous = state; state = next; Populate();
            SyncOverrides(toVr ? "Saved additions: {0}" : "Saved removals: {0}",selected.Count);
        }
        catch (Exception e) { LocalizedMessageBox.Show(this, e.Message, "Could not save — move cancelled", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void Undo()
    {
        if (previous == null || busy) return;
        try { store.Save(previous); state = previous; previous = null; Populate(); SyncOverrides("Last move undone"); }
        catch (Exception e) { LocalizedMessageBox.Show(this, e.Message, "Could not undo", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void SyncOverrides(string action, params object[] args)
    {
        try
        {
            sharedOverrides.Save(state);
            syncedIds = state.Games.Select(g => uint.Parse(g.Id)).Distinct().Order().ToArray();
            Localizer.SetText(status,action,args);
            UpdateRestartNotice();
        }
        catch (Exception e)
        {
            Localizer.SetText(status, "Saved locally; Virtual Desktop sync failed. Refresh to retry.");
            LocalizedMessageBox.Show(this, e.Message, "Could not sync Virtual Desktop overrides", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void UpdateRestartNotice()
    {
        UpdateConnectionStatus();
        string previousBitrateText=bitrate.Text;
        Localizer.SetText(bitrate,bitratePreferences.Configuration.Enabled ? "Restore bitrate limits" : "Remove bitrate limits");
        if(previousBitrateText!=bitrate.Text)AdjustLocalizedLayout();
        bitrateStatus.Text = bitratePreferences.StatusText;
        if (!busy) restart.Visible = (syncedIds != null && !streamerControl.IsApplied(syncedIds)) || bitratePreferences.RequiresRestart;
    }

    internal void UpdateConnectionStatus()
    {
        bool running = streamerControl.IsRunning;
        var iconState = StatusIcons.Select(running, ready);
        if (iconState != CurrentIconState)
        {
            CurrentIconState = iconState;
            Icon = statusIcons[iconState];
            trayIcon.Icon = Icon;
        }
        string trayText = "Virtual Desktop Unlock — "+Localizer.T(iconState == ConnectionIcon.Unloaded ? "Library not loaded" : iconState == ConnectionIcon.Stopped ? "Streamer stopped" : "Ready");
        trayIcon.Text = trayText.Length > 63 ? trayText[..63] : trayText;
        Localizer.SetText(connectionStatus,"VIRTUAL DESKTOP   ·   Streamer: {0}   ·   Library: {1}",Localizer.T(running ? "Running" : "Not running"),Localizer.T(ready ? "Loaded" : "Not loaded"));
        connectionStatus.BackColor = !ready ? Color.FromArgb(83, 52, 18)
            : running ? Color.FromArgb(22, 48, 49) : Color.FromArgb(77, 28, 36);
        connectionStatus.ForeColor = !ready ? Color.FromArgb(255, 192, 105)
            : running ? Accent : Color.FromArgb(255, 151, 160);
    }

    internal async Task RestartStreamerAsync()
    {
        if (busy || syncedIds == null) return;
        busy = true; UpdateButtons(); Localizer.SetText(status, "Restarting Streamer…");
        try
        {
            await streamerControl.RestartAsync();
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while ((!streamerControl.IsApplied(syncedIds) || bitratePreferences.RequiresRestart) && DateTime.UtcNow < deadline) await Task.Delay(250);
            if (!streamerControl.IsApplied(syncedIds) || bitratePreferences.RequiresRestart) throw new IOException(Localizer.T("Streamer restarted, but the helper has not confirmed your changes. Check the integration installation."));
            Localizer.SetText(status, "Streamer restarted. Reconnect your headset and reopen Games.");
        }
        catch (Exception e)
        {
            Localizer.SetText(status, "Streamer restart was not completed. Click the restart message to retry.");
            if (!preview) LocalizedMessageBox.Show(this, e.Message, "Could not apply library changes", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally { busy = false; UpdateButtons(); UpdateRestartNotice(); }
    }

    private void LaunchSelected()
    {
        var selection = steam.SelectedItems.Cast<ListViewItem>().Concat(vr.SelectedItems.Cast<ListViewItem>()).ToList();
        if (selection.Count != 1 || selection[0].Tag is not Game game) return;
        try { Process.Start(new ProcessStartInfo($"steam://rungameid/{game.Id}") { UseShellExecute = true }); Localizer.SetText(status,"Launch requested through Steam  ·  {0}",game.Name); }
        catch (Exception e) { LocalizedMessageBox.Show(this, e.Message, "Could not launch Steam", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    public void RestoreAndActivate()
    {
        if (IsDisposed || Disposing) return;
        ShowInTaskbar = true;
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Show();
        // A process launcher can request SW_HIDE. Explicitly show the window again after startup.
        NativeWindowActivation.ShowWindow(Handle, WindowState == FormWindowState.Maximized ? 3 : 9);
        if (!Screen.AllScreens.Any(s => Rectangle.Intersect(s.WorkingArea, Bounds) is var overlap && overlap.Width >= 100 && overlap.Height >= 80))
        {
            var area = Screen.PrimaryScreen?.WorkingArea ?? SystemInformation.WorkingArea;
            Location = new Point(area.Left + Math.Max(0, (area.Width - Width) / 2), area.Top + Math.Max(0, (area.Height - Height) / 2));
        }
        if (WindowState == FormWindowState.Normal)
        {
            var area = Screen.FromRectangle(Bounds).WorkingArea;
            Location = new Point(Math.Clamp(Left, area.Left, Math.Max(area.Left, area.Right - Width)),
                Math.Clamp(Top, area.Top, Math.Max(area.Top, area.Bottom - Height)));
        }
        BringToFront();
        Activate();
        NativeWindowActivation.SetForegroundWindow(Handle);
    }

    private void OpenSettings()
    {
        RestoreAndActivate();
        using var dialog = new SettingsForm(new WindowsStartupRegistration(), traySettings: traySettings, bitratePreferences: bitratePreferences);
        dialog.Icon = Icon;
        dialog.ShowDialog(this);
    }

    private void TraySettingChanged(object? sender, EventArgs e)
    {
        if (!traySettings.Enabled && !Visible) RestoreAndActivate();
        trayIcon.Visible = !preview && traySettings.Enabled;
    }

    private void BitrateChanged(object? sender, EventArgs e) { UpdateButtons(); }

    private void LanguageChanged(object? sender, EventArgs e)
    {
        Localizer.Apply(this); Localizer.ApplyMenu(trayMenu);
        AdjustLocalizedLayout();
        if (ready) Populate();
        UpdateRestartNotice();
    }

    private void AdjustLocalizedLayout()
    {
        int WidthFor(Button b) => TextRenderer.MeasureText(b.Text,b.Font).Width+24;
        transferColumn.Width = Math.Max(154,Math.Max(WidthFor(add),WidthFor(remove))+24);
        launch.Width=Math.Max(154,WidthFor(launch));undo.Width=Math.Max(116,WidthFor(undo));details.Width=Math.Max(110,WidthFor(details));
        int statusWidth=Math.Max(105,Math.Max(TextRenderer.MeasureText(Localizer.T("Installed"),steam.Font).Width,TextRenderer.MeasureText(Localizer.T("Not installed"),steam.Font).Width)+18);
        foreach(var list in new[]{steam,vr}) { list.Columns[1].Width=statusWidth;list.Columns[0].Width=Math.Max(180,list.ClientSize.Width-statusWidth); }
        MinimumSize=new Size(Math.Max(990,bitrate.PreferredSize.Width+bitrateProfiles.PreferredSize.Width+refresh.PreferredSize.Width+540),664);
    }

    internal void ExitApplication()
    {
        exiting = true;
        Close();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing && traySettings.Enabled && !exiting)
        {
            e.Cancel = true;
            Hide();
            ShowInTaskbar = false;
        }
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            traySettings.Changed -= TraySettingChanged;
            bitratePreferences.Changed -= BitrateChanged;
            Localizer.Changed -= LanguageChanged;
            trayIcon.Visible = false;
            trayIcon.Dispose(); trayMenu.Dispose(); restartTimer.Dispose(); tip.Dispose();
            statusIcons.Dispose();
        }
        base.Dispose(disposing);
    }
}

internal sealed class ThemeButton : Button
{
    protected override void OnPaint(PaintEventArgs e)
    {
        var hovered = Enabled && ClientRectangle.Contains(PointToClient(MousePosition));
        e.Graphics.Clear(!Enabled ? Color.FromArgb(28, 37, 52) : hovered ? FlatAppearance.MouseOverBackColor : BackColor);
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle,
            Enabled ? ForeColor : Color.FromArgb(137, 152, 174), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | (Localizer.IsRtl ? TextFormatFlags.RightToLeft : 0));
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -3, -3));
    }
}
