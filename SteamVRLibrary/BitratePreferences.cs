using System.Diagnostics;
using System.Xml;
using System.Xml.Linq;
using VirtualDesktop.Unlock;

namespace SteamVRLibrary;

public sealed class BitratePreferences
{
    public string Path { get; }
    public string StatusPath => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path)!, "BitrateStatus.xml");
    public BitrateConfiguration Configuration { get; private set; }
    public string? LoadError { get; private set; }
    public event EventHandler? Changed;
    public BitratePreferences(string path)
    {
        Path = path;
        try { Configuration = BitrateConfiguration.Read(path); }
        catch (Exception e) when (e is IOException or XmlException or UnauthorizedAccessException)
        { Configuration = BitrateConfiguration.Defaults(); LoadError = e.Message; }
    }
    public void Save(BitrateConfiguration value)
    {
        value.Save(Path);
        Configuration = value.Copy();
        LoadError = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }
    public void Toggle()
    {
        var next = Configuration.Copy(); next.Enabled = !next.Enabled; Save(next);
    }
    private XElement? LiveStatus()
    {
        try
        {
            using var reader = XmlReader.Create(StatusPath, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 8192 });
            var root = XDocument.Load(reader).Root!;
            using var process = Process.GetProcessById(int.Parse(root.Attribute("processId")!.Value));
            return !process.HasExited && process.ProcessName == "VirtualDesktop.Streamer"
                && process.StartTime.ToUniversalTime().Ticks == long.Parse(root.Attribute("startedUtcTicks")!.Value) ? root : null;
        }
        catch (Exception e) when (e is IOException or XmlException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or FormatException or NullReferenceException) { return null; }
    }
    public bool Acknowledged
    {
        get
        {
            try
            {
                var root = LiveStatus();
                return root?.Attribute("ready")?.Value == "true" && root.Attribute("revision")?.Value == BitrateConfiguration.Revision(Path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
        }
    }
    public bool RequiresRestart => !Acknowledged && (Configuration.Enabled || LiveStatus()?.Attribute("enabled")?.Value == "true" || LiveStatus()?.Attribute("ready")?.Value == "false");
    public string StatusText => LoadError != null ? "Could not read bitrate profiles. Open Bitrate profiles and save to repair them."
        : RequiresRestart ? "Bitrate changes pending · restart Streamer if they do not apply automatically."
        : Configuration.Enabled ? "Higher bitrate limits enabled in Streamer · client performance remains to be tested."
        : "Stock bitrate limits";
}
