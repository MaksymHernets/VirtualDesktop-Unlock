using System.Text.Json;

namespace SteamVRLibrary;

public sealed class TraySettings
{
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Virtual Desktop Unlock", "Settings.json");
    private readonly string path;
    public bool Enabled { get; private set; } = true;
    public event EventHandler? Changed;

    public TraySettings(string path)
    {
        this.path = path;
        if (File.Exists(path))
            Enabled = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(path))?.ShowInSystemTray ?? true;
    }

    public void SetEnabled(bool enabled)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(new Preferences { ShowInSystemTray = enabled }, new JsonSerializerOptions { WriteIndented = true }));
            if (File.Exists(path)) File.Replace(temp, path, path + ".bak");
            else File.Move(temp, path);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
        Enabled = enabled;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private sealed class Preferences
    {
        public bool ShowInSystemTray { get; set; } = true;
    }
}
