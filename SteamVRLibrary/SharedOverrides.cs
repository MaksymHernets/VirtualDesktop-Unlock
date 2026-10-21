using System.Xml.Linq;

namespace SteamVRLibrary;

public sealed class SharedOverrides(string path)
{
    public static string DefaultPath => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Virtual Desktop Unlock", "GameOverrides.xml");
    public string Path { get; } = path;

    public void Save(LibraryState state)
    {
        // Every selected game participates, regardless of its name, origin or Steam VR metadata.
        var ids = state.Games.Select(g => uint.Parse(g.Id)).Distinct().Order().ToArray();
        var document = new XDocument(new XElement("VirtualDesktopLibrary", new XAttribute("version", 1),
            ids.Select(id => new XElement("Game", new XAttribute("appId", id)))));
        var bytes = System.Text.Encoding.UTF8.GetBytes(document.ToString());
        if (File.Exists(Path) && File.ReadAllBytes(Path).AsSpan().SequenceEqual(bytes)) return;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var temp = Path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temp, bytes);
            if (File.Exists(Path)) File.Replace(temp, Path, Path + ".bak");
            else File.Move(temp, Path);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
