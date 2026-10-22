namespace SteamVRLibrary;

internal enum ConnectionIcon { Ready, Stopped, Unloaded }

internal sealed class StatusIcons : IDisposable
{
    private readonly Dictionary<ConnectionIcon, Icon> icons = Enum.GetValues<ConnectionIcon>().ToDictionary(state => state, state =>
    {
        using var stream = typeof(StatusIcons).Assembly.GetManifestResourceStream($"Status.{state}.ico")
            ?? throw new InvalidOperationException($"Missing status icon: {state}");
        using var source = new Icon(stream);
        return (Icon)source.Clone();
    });
    public Icon this[ConnectionIcon state] => icons[state];
    public static ConnectionIcon Select(bool running, bool libraryLoaded) => !libraryLoaded ? ConnectionIcon.Unloaded
        : running ? ConnectionIcon.Ready : ConnectionIcon.Stopped;
    public void Dispose() { foreach (var icon in icons.Values) icon.Dispose(); }
}
