using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace SteamVRLibrary;

public sealed record Game(string Id, string Name, string InstallDirectory, string Origin = "Added by you")
{
    public bool Installed => Directory.Exists(InstallDirectory);
}

public sealed class LibraryState
{
    public int Version { get; set; } = 1;
    public List<Game> Games { get; set; } = [];
}

public sealed class LibraryStore(string path)
{
    public string Path { get; } = path;
    private string? fingerprint;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static string? Fingerprint(string path) => File.Exists(path)
        ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) : null;

    public LibraryState? Load()
    {
        if (!File.Exists(Path)) { fingerprint = null; return null; }
        var bytes = File.ReadAllBytes(Path);
        var state = JsonSerializer.Deserialize<LibraryState>(bytes)
            ?? throw new InvalidDataException("The saved library is empty.");
        if (state.Version != 1 || state.Games == null || state.Games.Any(g =>
                g == null || !uint.TryParse(g.Id, out var id) || id == 0 || string.IsNullOrWhiteSpace(g.Name)))
            throw new InvalidDataException("The saved library has an unsupported format. Your file has been left unchanged.");
        state.Games = state.Games.DistinctBy(g => g.Id).ToList();
        fingerprint = Convert.ToHexString(SHA256.HashData(bytes));
        return state;
    }

    public void Save(LibraryState state)
    {
        if (Fingerprint(Path) != fingerprint)
            throw new IOException("The library changed in another window. Refresh before making more changes.");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(state, Json);
        var temp = Path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(bytes); stream.Flush(true); }
            if (Fingerprint(Path) != fingerprint) throw new IOException("The library changed while saving. Refresh and try again.");
            if (File.Exists(Path)) File.Replace(temp, Path, Path + ".bak");
            else File.Move(temp, Path);
            fingerprint = Convert.ToHexString(SHA256.HashData(bytes));
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}

public sealed record ScanResult(List<Game> Games, HashSet<string> CachedVrIds, List<string> Warnings);

public static class SteamScanner
{
    // Valve's text VDF format allows nested objects, quoted escapes and // comments.
    public static List<string> Tokens(string text)
    {
        var result = new List<string>();
        for (var i = 0; i < text.Length;)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
            { while (i < text.Length && text[i] != '\n') i++; continue; }
            if (c is '{' or '}') { result.Add(c.ToString()); i++; continue; }
            var token = new StringBuilder();
            if (c == '"')
            {
                i++; var closed = false;
                while (i < text.Length)
                {
                    c = text[i++];
                    if (c == '"') { closed = true; break; }
                    if (c == '\\' && i < text.Length && text[i] is '\\' or '"') c = text[i++];
                    token.Append(c);
                }
                if (!closed) throw new InvalidDataException("Unterminated quoted value in Steam manifest.");
            }
            else { while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] is not '{' and not '}') token.Append(text[i++]); }
            result.Add(token.ToString());
        }
        return result;
    }

    public static Dictionary<string, string> Values(string text)
    {
        var tokens = Tokens(text);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i + 1 < tokens.Count; i++)
        {
            if (tokens[i] is "{" or "}" || tokens[i + 1] is "{" or "}") continue;
            values[tokens[i]] = tokens[++i];
        }
        return values;
    }

    public static HashSet<string> ReadVrCache(string path)
    {
        var result = new HashSet<string>();
        if (!File.Exists(path)) return result;
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        if (!doc.RootElement.TryGetProperty("SteamProductInfos", out var infos)) return result;
        foreach (var property in infos.EnumerateObject())
        {
            if (new[] { "OpenVRSupport", "OpenXRSupport", "OtherVRSupport" }.Any(key =>
                property.Value.TryGetProperty(key, out var flag) && flag.ValueKind == JsonValueKind.True)) result.Add(property.Name);
        }
        return result;
    }

    public static ScanResult Scan(string? steamRoot = null, string? cachePath = null)
    {
        var warnings = new List<string>();
        steamRoot ??= Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string
            ?? Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string
            ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { steamRoot };
        var libraryFile = System.IO.Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        try
        {
            if (File.Exists(libraryFile))
            {
                var tokens = Tokens(File.ReadAllText(libraryFile));
                for (var i = 0; i + 1 < tokens.Count; i++)
                    if (tokens[i].Equals("path", StringComparison.OrdinalIgnoreCase)
                        || (uint.TryParse(tokens[i], out _) && System.IO.Path.IsPathRooted(tokens[i + 1])))
                        roots.Add(tokens[i + 1]);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        { warnings.Add("Steam library folders: " + e.Message); }
        var games = new Dictionary<string, Game>();
        foreach (var root in roots)
        {
            var apps = System.IO.Path.Combine(root, "steamapps");
            try
            {
                if (!Directory.Exists(apps)) continue;
                foreach (var file in Directory.EnumerateFiles(apps, "appmanifest_*.acf"))
                {
                    try
                    {
                        var data = Values(File.ReadAllText(file));
                        if (!data.TryGetValue("appid", out var id) || !uint.TryParse(id, out _)
                            || id is "228980" or "250820" || !data.TryGetValue("name", out var name)
                            || !data.TryGetValue("installdir", out var dir)) continue;
                        var common = System.IO.Path.GetFullPath(System.IO.Path.Combine(apps, "common")) + System.IO.Path.DirectorySeparatorChar;
                        var install = System.IO.Path.GetFullPath(System.IO.Path.Combine(common, dir));
                        if (!install.StartsWith(common, StringComparison.OrdinalIgnoreCase)) continue;
                        if (Directory.Exists(install)) games[id] = new Game(id, name, install);
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException)
                    { warnings.Add(System.IO.Path.GetFileName(file) + ": " + e.Message); }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            { warnings.Add(apps + ": " + e.Message); }
        }
        cachePath ??= System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Virtual Desktop", "GameSettings.json");
        HashSet<string> cached = [];
        try { cached = ReadVrCache(cachePath); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        { warnings.Add("Virtual Desktop cache: " + e.Message); }
        return new ScanResult(games.Values.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).ToList(), cached, warnings);
    }
}
