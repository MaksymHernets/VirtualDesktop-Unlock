using System.Text.Json;

namespace SteamVRLibrary;

internal static class SelfTests
{
    public static int Run()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "test-results", DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(root);
        var checks = new List<string>();
        void Check(bool condition, string name)
        { if (!condition) throw new Exception(name); checks.Add("PASS " + name); }
        try
        {
            var steam = Path.Combine(root, "Steam");
            var secondary = Path.Combine(root, "Second library ü");
            Directory.CreateDirectory(Path.Combine(steam, "steamapps"));
            Directory.CreateDirectory(Path.Combine(secondary, "steamapps"));
            static string Quote(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
            File.WriteAllText(Path.Combine(steam, "steamapps", "libraryfolders.vdf"),
                "// test comment\n\"libraryfolders\" { \"0\" { \"path\" " + Quote(steam) + " } \"1\" { \"path\" " + Quote(secondary) + " } }");
            void Manifest(string library, string id, string name, string dir)
            {
                Directory.CreateDirectory(Path.Combine(library, "steamapps", "common", dir));
                File.WriteAllText(Path.Combine(library, "steamapps", "appmanifest_" + id + ".acf"),
                    "\"AppState\" { \"appid\" " + Quote(id) + " \"name\" " + Quote(name) + " \"installdir\" " + Quote(dir) + " }");
            }
            Manifest(steam, "111", "Native VR", "Native");
            Manifest(secondary, "952060", "Resident Evil 3", "RE3");
            Manifest(steam, "228980", "Steamworks Redistributables", "Redistributables");
            File.WriteAllText(Path.Combine(steam, "steamapps", "appmanifest_333.acf"), "\"AppState\" { \"appid\" \"333\" \"name\" \"Uninstalled\" \"installdir\" \"Missing\" }");
            File.WriteAllText(Path.Combine(steam, "steamapps", "appmanifest_444.acf"), "\"AppState\" { \"appid\" \"444\" \"name\" \"Escape\" \"installdir\" " + Quote("..\\..\\") + " }");
            var cache = Path.Combine(root, "GameSettings.json");
            var cacheText = "{\"SteamProductInfos\":{\"111\":{\"OpenVRSupport\":true},\"952060\":{\"OpenXRSupport\":false},\"999\":{\"OpenXRSupport\":true}}}";
            File.WriteAllText(cache, cacheText);
            var scan = SteamScanner.Scan(steam, cache);
            Check(scan.Warnings.Count == 0 && scan.Games.Count == 2, "Scans multiple Steam libraries; excludes tools, missing installs and escaped paths");
            Check(scan.Games.Single(g => g.Id == "952060").InstallDirectory.Contains("Second library ü"), "Handles spaces, Unicode and VDF backslash escapes");
            Check(scan.CachedVrIds.SetEquals(["111", "999"]), "VR cache uses true support flags, including OpenXR");
            Check(File.ReadAllText(cache) == cacheText, "Steam scanning leaves Virtual Desktop cache unchanged");
            var path = Path.Combine(root, "Data", "library.json");
            var store = new LibraryStore(path);
            Check(store.Load() == null, "First launch detects missing companion library");
            var state = new LibraryState { Games = scan.Games.Where(g => scan.CachedVrIds.Contains(g.Id)).ToList() };
            store.Save(state);
            state.Games.Add(scan.Games.Single(g => g.Id == "952060"));
            store.Save(state);
            var reopened = new LibraryStore(path);
            Check(reopened.Load()!.Games.Any(g => g.Id == "952060"), "Moved RE3 persists across store restart");
            Check(JsonSerializer.Deserialize<LibraryState>(File.ReadAllText(path + ".bak"))!.Games.Count == 1, "Atomic saves retain the prior library as a backup");
            reopened.Save(new LibraryState());
            Check(new LibraryStore(path).Load()!.Games.Count == 0, "An intentionally empty VR library stays empty on restart");
            var other = new LibraryStore(path);
            other.Load(); other.Save(new LibraryState { Games = scan.Games });
            var staleRejected = false;
            try { reopened.Save(new LibraryState()); } catch (IOException) { staleRejected = true; }
            Check(staleRejected && new LibraryStore(path).Load()!.Games.Count == 2, "Rejects stale writes without losing changes from another instance");
            File.WriteAllText(path, "{bad json");
            var corruptRejected = false;
            try { new LibraryStore(path).Load(); } catch (JsonException) { corruptRejected = true; }
            Check(corruptRejected && File.ReadAllText(path) == "{bad json", "Corrupt library is reported and not overwritten");
            var malformed = false;
            try { SteamScanner.Values("\"unclosed"); } catch (InvalidDataException) { malformed = true; }
            Check(malformed, "Malformed Steam manifest fails explicitly");
            var sharedPath = Path.Combine(root, "Shared", "GameOverrides.xml");
            var shared = new SharedOverrides(sharedPath);
            var additions = new LibraryState { Games = [new Game("65540", "Gothic 1 Classic", "unused"), new Game("952060", "Resident Evil 3", "unused", "Imported from Virtual Desktop cache"), new Game("111", "Native VR", "unused", "Imported from Virtual Desktop cache")] };
            shared.Save(additions);
            string[] ReadSharedIds(string file) => System.Xml.Linq.XDocument.Load(file).Root!.Elements("Game").Select(x => x.Attribute("appId")!.Value).ToArray();
            Check(ReadSharedIds(sharedPath).SequenceEqual(["111", "65540", "952060"]), "Exports every selected game regardless of origin or VR metadata");
            shared.Save(new LibraryState());
            Check(ReadSharedIds(sharedPath).Length == 0, "Empty custom selection is exported explicitly");
            Check(ReadSharedIds(sharedPath + ".bak").SequenceEqual(["111", "65540", "952060"]), "Shared override updates retain a backup");
            File.WriteAllText(Path.Combine(steam, "steamapps", "appmanifest_555.acf"), "\"unclosed");
            var partialScan = SteamScanner.Scan(steam, cache);
            Check(partialScan.Games.Count == 2 && partialScan.Warnings.Count == 1, "One corrupt manifest does not prevent other games from loading");

            var uiPath = Path.Combine(root, "UI", "library.json");
            var streamer = new TestStreamerControl();
            using (var form = new LibraryForm(true, uiPath, streamer))
            {
                form.LoadPreview(scan);
                form.ShowInTaskbar = false; form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-20000, -20000); form.Show(); Application.DoEvents();
                IEnumerable<Control> Descendants(Control c) => c.Controls.Cast<Control>().SelectMany(x => new[] { x }.Concat(Descendants(x)));
                var controls = Descendants(form).ToList();
                var left = controls.OfType<ListView>().Single(x => x.AccessibleName!.StartsWith("Installed"));
                var right = controls.OfType<ListView>().Single(x => x.AccessibleName == "VD cache games");
                var addButton = controls.OfType<Button>().Single(x => x.Text.StartsWith("Add to VR"));
                var removeButton = controls.OfType<Button>().Single(x => x.Text.Contains("Remove"));
                var undoButton = controls.OfType<Button>().Single(x => x.Text == "Undo move");
                var search = controls.OfType<TextBox>().Single();
                var banner = controls.OfType<Label>().Single(x => x.AccessibleName == "Virtual Desktop status");
                Check(form.Text.StartsWith("Virtual Desktop Unlock") && controls.OfType<Label>().Any(x => x.Text == "VD cache games")
                    && !controls.Any(x => x.Text.Contains("Steam VR Library")), "Uses the new application and cache panel names without the old label");
                Check(banner.Text.Contains("Streamer: Running") && banner.Text.Contains("Library: Loaded") && banner.BackColor == Color.FromArgb(22, 48, 49), "Running Streamer and loaded library show green status");
                streamer.IsRunning = false; form.UpdateConnectionStatus();
                Check(banner.Text.Contains("Streamer: Not running") && banner.Text.Contains("Library: Loaded") && banner.BackColor == Color.FromArgb(77, 28, 36), "Stopped Streamer with loaded library shows red status");
                streamer.IsRunning = true; form.UpdateConnectionStatus();
                var restart = controls.OfType<LinkLabel>().Single(x => x.AccessibleName == "Restart Streamer");
                Check(left.Items.Count == 1 && right.Items.Count == 1, "UI seeds only installed cached VR games");
                left.Items[0].Selected = true; addButton.PerformClick();
                Check(left.Items.Count == 0 && right.Items.Count == 2 && new LibraryStore(uiPath).Load()!.Games.Count == 2, "Add button moves RE3 and persists the selection");
                var uiShared = Path.Combine(Path.GetDirectoryName(uiPath)!, "GameOverrides.xml");
                Check(ReadSharedIds(uiShared).SequenceEqual(["111", "952060"]), "Add button syncs custom game IDs for the startup helper");
                Check(restart.Visible && restart.Enabled, "Unapplied selection shows a clickable restart message");
                using (var image = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size));
                    image.Save(Path.Combine(root, "updated-ui.png"));
                }
                streamer.Fail = true;
                form.RestartStreamerAsync().GetAwaiter().GetResult();
                Check(restart.Visible && restart.Enabled && streamer.Restarts == 1, "Failed restart preserves the message and allows retry");
                streamer.Fail = false;
                form.RestartStreamerAsync().GetAwaiter().GetResult();
                Check(!restart.Visible && streamer.Restarts == 2, "Confirmed restart hides the message");
                right.Items.Cast<ListViewItem>().Single(x => ((Game)x.Tag!).Id == "952060").Selected = true;
                removeButton.PerformClick();
                Check(left.Items.Count == 1 && right.Items.Count == 1 && new LibraryStore(uiPath).Load()!.Games.Count == 1, "Remove button moves RE3 back and saves");
                Check(ReadSharedIds(uiShared).SequenceEqual(["111"]), "Remove button removes the custom helper override");
                Check(restart.Visible, "Removal also requires a Streamer restart");
                undoButton.PerformClick();
                Check(right.Items.Count == 2 && new LibraryStore(uiPath).Load()!.Games.Count == 2, "Undo restores and saves the prior selection");
                Check(ReadSharedIds(uiShared).SequenceEqual(["111", "952060"]), "Undo also restores the helper override");
                Check(!restart.Visible, "Undo back to the live selection clears the restart message");
                search.Text = "952060";
                Check(right.Items.Count == 1 && ((Game)right.Items[0].Tag!).Id == "952060", "Search filters by Steam ID");
                search.Clear();
                foreach (ListViewItem item in right.Items) item.Selected = true;
                removeButton.PerformClick();
                Check(left.Items.Count == 2 && right.Items.Count == 0 && new LibraryStore(uiPath).Load()!.Games.Count == 0, "Multiple selections can empty the VR library persistently");
                form.Hide();
                form.WindowState = FormWindowState.Minimized;
                form.Location = new Point(-20000, -20000);
                form.RestoreAndActivate();
                Application.DoEvents();
                Check(form.Visible && form.WindowState == FormWindowState.Normal && Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(form.Bounds)),
                    $"Restores a hidden, minimized, off-screen window onto an available display (visible={form.Visible}, state={form.WindowState}, bounds={form.Bounds})");
                using (var activation = new SingleInstance(form))
                {
                    var request = Task.Run(SingleInstance.ActivateExisting);
                    var deadline = DateTime.UtcNow.AddSeconds(8);
                    while (!request.IsCompleted && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(10); }
                    Check(request.IsCompletedSuccessfully && request.Result && form.Visible,
                        "Second-instance activation reaches the existing UI and receives acknowledgement");
                }
            }
            using (var unloaded = new LibraryForm(true, Path.Combine(root, "Unloaded", "library.json"), streamer))
            {
                var banner = unloaded.Controls[0].Controls.OfType<Label>().Single(x => x.AccessibleName == "Virtual Desktop status");
                streamer.IsRunning = false; unloaded.UpdateConnectionStatus();
                Check(banner.Text.Contains("Streamer: Not running") && banner.Text.Contains("Library: Not loaded") && banner.BackColor == Color.FromArgb(83, 52, 18), "Stopped Streamer and unloaded library show orange status");
                streamer.IsRunning = true; unloaded.UpdateConnectionStatus();
                Check(banner.Text.Contains("Streamer: Running") && banner.Text.Contains("Library: Not loaded") && banner.BackColor == Color.FromArgb(83, 52, 18), "Running Streamer while library is not loaded shows orange status");
            }
            File.WriteAllLines(Path.Combine(root, "results.txt"), checks);
            var startup = new TestStartupRegistration();
            using (var settings = new SettingsForm(startup, showErrors: false))
            {
                settings.ShowInTaskbar = false; settings.StartPosition = FormStartPosition.Manual;
                settings.Location = new Point(-20000, -20000); settings.Show(); Application.DoEvents();
                var settingsControls = settings.Controls[0].Controls.Cast<Control>().ToArray();
                var autoStart = settingsControls.OfType<CheckBox>().Single(c => c.AccessibleName == "Launch at Windows startup");
                Check(!autoStart.Checked && startup.Writes == 0, "Opening settings leaves Windows startup unchanged");
                Check(!settingsControls.OfType<ComboBox>().Single().Enabled && !settingsControls.OfType<CheckBox>().Single(c => c.AccessibleName == "Remove streaming bitrate limits").Enabled, "Future language and bitrate options are disabled");
                autoStart.Checked = true;
                Check(startup.Enabled && startup.Writes == 1, "Startup checkbox enables automatic launch");
                startup.Fail = true; autoStart.Checked = false;
                Check(autoStart.Checked && startup.Enabled, "Failed startup write restores the checkbox state");
                startup.Fail = false; autoStart.Checked = false;
                Check(!startup.Enabled, "Startup checkbox disables automatic launch");
                autoStart.Checked = true;
                using var image = new Bitmap(settings.Width, settings.Height);
                settings.DrawToBitmap(image, new Rectangle(Point.Empty, settings.Size));
                image.Save(Path.Combine(root, "settings-ui.png"));
            }
            using (var settings = new SettingsForm(startup, showErrors: false))
            {
                Check(settings.Controls[0].Controls.OfType<CheckBox>().Single(c => c.AccessibleName == "Launch at Windows startup").Checked, "Reopened settings reflect persisted startup preference");
            }
            File.WriteAllLines(Path.Combine(root, "results.txt"), checks);
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "self-test-result.txt"), string.Join(Environment.NewLine, checks));
            return 0;
        }
        catch (Exception e)
        {
            checks.Add("FAIL " + e);
            File.WriteAllLines(Path.Combine(root, "results.txt"), checks);
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "self-test-result.txt"), string.Join(Environment.NewLine, checks));
            return 1;
        }
    }

    private sealed class TestStreamerControl : IStreamerControl
    {
        private HashSet<uint> applied = [111];
        private uint[] requested = [];
        public bool Fail { get; set; }
        public bool IsRunning { get; set; } = true;
        public int Restarts { get; private set; }
        public bool IsApplied(IReadOnlyCollection<uint> ids)
        { requested = ids.ToArray(); return applied.SetEquals(ids); }
        public Task RestartAsync()
        {
            Restarts++;
            if (Fail) return Task.FromException(new IOException("Simulated restart failure"));
            applied = requested.ToHashSet();
            return Task.CompletedTask;
        }
    }

    private sealed class TestStartupRegistration : IStartupRegistration
    {
        public bool Enabled { get; private set; }
        public bool Fail { get; set; }
        public int Writes { get; private set; }
        public void SetEnabled(bool enabled)
        {
            if (Fail) throw new IOException("Simulated registry failure");
            Enabled = enabled; Writes++;
        }
    }
}
