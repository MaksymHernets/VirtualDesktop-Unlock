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
                MessageBox.Show("The existing library window did not respond. Close that instance in Task Manager and try again.", "Virtual Desktop Unlock", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
    private ScanResult scan = new([], [], []);
    private LibraryState state = new();
    private LibraryState? previous;
    private bool updating;
    private bool ready;
    private bool busy;

    public LibraryForm(bool preview = false, string? libraryPath = null, IStreamerControl? streamerControl = null)
    {
        this.preview = preview;
        this.streamerControl = streamerControl ?? new StreamerControl();
        store = new LibraryStore(libraryPath ?? Path.Combine(AppContext.BaseDirectory, "Data", "library.json"));
        sharedOverrides = new SharedOverrides(preview || libraryPath != null
            ? Path.Combine(Path.GetDirectoryName(store.Path)!, "GameOverrides.xml") : SharedOverrides.DefaultPath);
        Text = "Virtual Desktop Unlock — v0.1.2";
        Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Application;
        BackColor = Background;
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10);
        ClientSize = new Size(1160, 730);
        MinimumSize = new Size(990, 640);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(28, 22, 28, 18), ColumnCount = 1, RowCount = 8, BackColor = Background };
        foreach (var height in new[] { 50, 30, 54, 48 }) root.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        Controls.Add(root);

        var titleRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        titleRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
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
        tip.SetToolTip(settings, "Settings");
        settings.Click += (_, _) =>
        {
            using var dialog = new SettingsForm(new WindowsStartupRegistration());
            dialog.Icon = Icon;
            dialog.ShowDialog(this);
        };
        heading.Controls.Add(settings);
        titleRow.Controls.Add(heading, 0, 0);
        refresh.Dock = DockStyle.Fill;
        refresh.Margin = new Padding(0, 3, 0, 8);
        titleRow.Controls.Add(refresh, 1, 0);
        root.Controls.Add(titleRow, 0, 0);
        root.Controls.Add(TextLabel("Choose the games you want to play in VR.", Muted, 11), 0, 1);

        connectionStatus.Padding = new Padding(14, 0, 8, 0);
        connectionStatus.Margin = new Padding(0, 5, 0, 7);
        connectionStatus.AccessibleName = "Virtual Desktop status";
        tip.SetToolTip(connectionStatus, "Shows whether Streamer is running and this app has loaded your library. Pending changes are applied using the restart link below.");
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
        lists.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 154));
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
        root.Controls.Add(actions, 0, 5);
        status.AutoEllipsis = true;
        root.Controls.Add(status, 0, 6);
        root.Controls.Add(restart, 0, 7);
        tip.SetToolTip(restart, "Close and relaunch Virtual Desktop Streamer. Windows may request administrator permission. Then reconnect your headset.");
        restart.LinkClicked += async (_, _) => await RestartStreamerAsync();
        restartTimer.Tick += (_, _) => UpdateRestartNotice();
        if (!preview) restartTimer.Start();
        steam.AccessibleName = "Installed Steam games not in your VR library";
        vr.AccessibleName = "VD cache games";
        tip.SetToolTip(add, "Save selected games and export their Steam IDs to the Virtual Desktop startup helper. Restart Streamer to apply.");
        tip.SetToolTip(remove, "Remove custom additions for the next Streamer restart. Native VR games may still appear automatically in Virtual Desktop.");
        tip.SetToolTip(launch, "Launch the selected game through Steam. Connect Virtual Desktop first; a VR mod must already be installed for flat-screen games.");
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
        details.Click += (_, _) => MessageBox.Show(this,
            (scan.Warnings.Count == 0 ? "All available Steam folders were scanned successfully." : string.Join("\n\n", scan.Warnings))
            + "\n\nSelections: " + store.Path + "\n\nShared overrides: " + sharedOverrides.Path + "\n\nRequires the 0.1.1 startup helper. Restart Streamer after changes. Native VR games are detected automatically and cannot be hidden by removing a custom override.",
            "Library details", MessageBoxButtons.OK, MessageBoxIcon.Information);
        if (!preview) Shown += async (_, _) => await ReloadAsync();
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
        list.Resize += (_, _) => { if (list.Columns.Count == 2) list.Columns[0].Width = Math.Max(180, list.ClientSize.Width - 105); };
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
        busy = true; status.Text = "Scanning installed Steam games…"; UpdateButtons();
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
        { ready = false; status.Text = "Could not load the library. Your saved selections were left unchanged."; MessageBox.Show(this, e.Message, "Could not load library", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { busy = false; UpdateButtons(); }
    }

    public void LoadPreview(ScanResult? previewScan = null)
    {
        scan = previewScan ?? SteamScanner.Scan();
        state = store.Load() ?? new LibraryState { Games = scan.Games.Where(g => scan.CachedVrIds.Contains(g.Id)).Select(g => g with { Origin = "Imported from Virtual Desktop cache" }).ToList() };
        ready = true;
        Populate();
        status.Text = "Saved automatically  ·  Connect Virtual Desktop before launching a VR game.";
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
            steamCount.Text = $"{steam.Items.Count} of {left.Count} games";
            vrCount.Text = $"{vr.Items.Count} of {right.Count} games";
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
                var item = new ListViewItem(game.Name) { Tag = game, ToolTipText = $"Steam app {game.Id}\n{game.InstallDirectory}\n{game.Origin}", Selected = selected.Contains(game.Id) };
                item.SubItems.Add(game.Installed ? "Installed" : "Not installed");
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
            SyncOverrides($"Saved {selected.Count} game{(selected.Count == 1 ? "" : "s")} {(toVr ? "addition" : "removal")}");
        }
        catch (Exception e) { MessageBox.Show(this, e.Message, "Could not save — move cancelled", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void Undo()
    {
        if (previous == null || busy) return;
        try { store.Save(previous); state = previous; previous = null; Populate(); SyncOverrides("Last move undone"); }
        catch (Exception e) { MessageBox.Show(this, e.Message, "Could not undo", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void SyncOverrides(string action)
    {
        try
        {
            sharedOverrides.Save(state);
            syncedIds = state.Games.Select(g => uint.Parse(g.Id)).Distinct().Order().ToArray();
            status.Text = action;
            UpdateRestartNotice();
        }
        catch (Exception e)
        {
            status.Text = "Saved locally; Virtual Desktop sync failed. Refresh to retry.";
            MessageBox.Show(this, e.Message, "Could not sync Virtual Desktop overrides", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void UpdateRestartNotice()
    {
        UpdateConnectionStatus();
        if (!busy) restart.Visible = syncedIds != null && !streamerControl.IsApplied(syncedIds);
    }

    internal void UpdateConnectionStatus()
    {
        bool running = streamerControl.IsRunning;
        connectionStatus.Text = $"VIRTUAL DESKTOP   ·   Streamer: {(running ? "Running" : "Not running")}   ·   Library: {(ready ? "Loaded" : "Not loaded")}";
        connectionStatus.BackColor = !ready ? Color.FromArgb(83, 52, 18)
            : running ? Color.FromArgb(22, 48, 49) : Color.FromArgb(77, 28, 36);
        connectionStatus.ForeColor = !ready ? Color.FromArgb(255, 192, 105)
            : running ? Accent : Color.FromArgb(255, 151, 160);
    }

    internal async Task RestartStreamerAsync()
    {
        if (busy || syncedIds == null) return;
        busy = true; UpdateButtons(); status.Text = "Restarting Streamer…";
        try
        {
            await streamerControl.RestartAsync();
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!streamerControl.IsApplied(syncedIds) && DateTime.UtcNow < deadline) await Task.Delay(250);
            if (!streamerControl.IsApplied(syncedIds)) throw new IOException("Streamer restarted, but the helper has not confirmed your selection. Try again or check the integration installation.");
            status.Text = "Streamer restarted. Reconnect your headset and reopen Games.";
        }
        catch (Exception e)
        {
            status.Text = "Streamer restart was not completed. Click the restart message to retry.";
            if (!preview) MessageBox.Show(this, e.Message, "Could not apply library changes", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally { busy = false; UpdateButtons(); UpdateRestartNotice(); }
    }

    private void LaunchSelected()
    {
        var selection = steam.SelectedItems.Cast<ListViewItem>().Concat(vr.SelectedItems.Cast<ListViewItem>()).ToList();
        if (selection.Count != 1 || selection[0].Tag is not Game game) return;
        try { Process.Start(new ProcessStartInfo($"steam://rungameid/{game.Id}") { UseShellExecute = true }); status.Text = $"Launch requested through Steam  ·  {game.Name}"; }
        catch (Exception e) { MessageBox.Show(this, e.Message, "Could not launch Steam", MessageBoxButtons.OK, MessageBoxIcon.Error); }
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

    protected override void Dispose(bool disposing)
    { if (disposing) { restartTimer.Dispose(); tip.Dispose(); } base.Dispose(disposing); }
}

internal sealed class ThemeButton : Button
{
    protected override void OnPaint(PaintEventArgs e)
    {
        var hovered = Enabled && ClientRectangle.Contains(PointToClient(MousePosition));
        e.Graphics.Clear(!Enabled ? Color.FromArgb(28, 37, 52) : hovered ? FlatAppearance.MouseOverBackColor : BackColor);
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle,
            Enabled ? ForeColor : Color.FromArgb(137, 152, 174), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -3, -3));
    }
}
