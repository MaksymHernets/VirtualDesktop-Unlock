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
            File.WriteAllText(Path.Combine(steam, "steamapps", "appmanifest_555.acf"), "\"unclosed");
            var partialScan = SteamScanner.Scan(steam, cache);
            Check(partialScan.Games.Count == 2 && partialScan.Warnings.Count == 1, "One corrupt manifest does not prevent other games from loading");

            var uiPath = Path.Combine(root, "UI", "library.json");
            using (var form = new LibraryForm(true, uiPath))
            {
                form.LoadPreview(scan);
                form.ShowInTaskbar = false; form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-20000, -20000); form.Show(); Application.DoEvents();
                IEnumerable<Control> Descendants(Control c) => c.Controls.Cast<Control>().SelectMany(x => new[] { x }.Concat(Descendants(x)));
                var controls = Descendants(form).ToList();
                var left = controls.OfType<ListView>().Single(x => x.AccessibleName!.StartsWith("Installed"));
                var right = controls.OfType<ListView>().Single(x => x.AccessibleName!.StartsWith("My companion"));
                var addButton = controls.OfType<Button>().Single(x => x.Text.StartsWith("Add to VR"));
                var removeButton = controls.OfType<Button>().Single(x => x.Text.Contains("Remove"));
                var undoButton = controls.OfType<Button>().Single(x => x.Text == "Undo move");
                var search = controls.OfType<TextBox>().Single();
                Check(left.Items.Count == 1 && right.Items.Count == 1, "UI seeds only installed cached VR games");
                left.Items[0].Selected = true; addButton.PerformClick();
                Check(left.Items.Count == 0 && right.Items.Count == 2 && new LibraryStore(uiPath).Load()!.Games.Count == 2, "Add button moves RE3 and persists the selection");
                right.Items.Cast<ListViewItem>().Single(x => ((Game)x.Tag!).Id == "952060").Selected = true;
                removeButton.PerformClick();
                Check(left.Items.Count == 1 && right.Items.Count == 1 && new LibraryStore(uiPath).Load()!.Games.Count == 1, "Remove button moves RE3 back and saves");
                undoButton.PerformClick();
                Check(right.Items.Count == 2 && new LibraryStore(uiPath).Load()!.Games.Count == 2, "Undo restores and saves the prior selection");
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
}
