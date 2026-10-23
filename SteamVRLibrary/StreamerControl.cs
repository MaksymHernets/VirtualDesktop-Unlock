using System.Diagnostics;

namespace SteamVRLibrary;

public interface IStreamerControl
{
    bool IsRunning { get; }
    bool IsApplied(IReadOnlyCollection<uint> ids);
    Task RestartAsync();
}

public sealed class StreamerControl : IStreamerControl
{
    public bool IsRunning
    {
        get
        {
            var processes = Process.GetProcessesByName("VirtualDesktop.Streamer");
            // Enumeration already identifies running processes. HasExited opens a
            // synchronization handle that Windows may deny for an elevated Streamer.
            // A process exiting after this snapshot is handled by the next refresh.
            try { return processes.Length != 0; }
            finally { foreach (var process in processes) process.Dispose(); }
        }
    }
    private static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Virtual Desktop Streamer");
    private static string Executable => Path.Combine(Folder, "VirtualDesktop.Streamer.exe");
    private static string Log => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Virtual Desktop", "LibraryOverrides.log");

    public bool IsApplied(IReadOnlyCollection<uint> ids)
    {
        try
        {
            var lines = File.ReadAllLines(Log);
            foreach (var process in Process.GetProcessesByName("VirtualDesktop.Streamer"))
            {
                using (process)
                {
                    string prefix = $"[{process.Id}] Installed OpenXR overrides: ";
                    var line = lines.LastOrDefault(l => l.Contains(prefix, StringComparison.Ordinal)
                        && DateTimeOffset.TryParse(l.Split(' ')[0], out var time)
                        && time.UtcDateTime >= process.StartTime.ToUniversalTime().AddSeconds(-1));
                    if (line == null) continue;
                    var value = line[(line.IndexOf(prefix, StringComparison.Ordinal) + prefix.Length)..].Split(';')[0];
                    var applied = value.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(uint.Parse).ToHashSet();
                    if (applied.SetEquals(ids)) return true;
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException or FormatException) { }
        return false;
    }

    public async Task RestartAsync()
    {
        // Use a separate elevated instance only for restarting the installed Streamer.
        // The library window continues running without administrator privileges.
        using var helper = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--restart-streamer")
        { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden })
            ?? throw new IOException("Could not start the Streamer restart helper.");
        await helper.WaitForExitAsync();
        if (helper.ExitCode != 0) throw new IOException("Could not restart Streamer. Check that Streamer and the library integration are installed, then try again.");
    }

    internal static int RestartInstalledStreamer()
    {
        try
        {
            if (!File.Exists(Executable) || !File.Exists(Path.Combine(Folder, "VirtualDesktop.LibraryOverrides.dll"))
                || !File.Exists(Executable + ".config")) return 2;
            foreach (var process in Process.GetProcessesByName("VirtualDesktop.Streamer"))
            {
                using (process)
                {
                    if (!string.Equals(process.MainModule?.FileName, Executable, StringComparison.OrdinalIgnoreCase)) continue;
                    process.CloseMainWindow();
                    if (!process.WaitForExit(3000))
                    {
                        process.Kill();
                        if (!process.WaitForExit(5000)) return 3;
                    }
                }
            }
            using var started = Process.Start(new ProcessStartInfo(Executable)
            { WorkingDirectory = Folder, UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden });
            return started != null && !started.WaitForExit(4000) ? 0 : 4;
        }
        catch { return 1; }
    }
}
