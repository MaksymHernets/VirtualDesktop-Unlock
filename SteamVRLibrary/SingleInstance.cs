using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace SteamVRLibrary;

internal static class NativeWindowActivation
{
    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")]
    private static extern IntPtr GetThreadDesktop(uint threadId);
    [DllImport("user32.dll")]
    private static extern IntPtr GetProcessWindowStation();
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetUserObjectInformation(IntPtr handle, int index, StringBuilder value, int length, out int needed);

    private static string ObjectName(IntPtr handle)
    {
        var buffer = new StringBuilder(512);
        return GetUserObjectInformation(handle, 2, buffer, buffer.Capacity * 2, out _) ? buffer.ToString() : "unknown";
    }

    // Windows desktops in the same login session share named mutexes. A hidden desktop's
    // process must not prevent a user from opening a visible instance on their own desktop.
    internal static readonly string DesktopName = ObjectName(GetProcessWindowStation()) + "/" + ObjectName(GetThreadDesktop(GetCurrentThreadId()));
    internal static readonly string InstanceScope = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        Environment.UserName + "|" + Process.GetCurrentProcess().SessionId + "|" + DesktopName)))[..24];
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AllowSetForegroundWindow(int processId);
}

internal sealed class SingleInstance : IDisposable
{
    internal static readonly string MutexName = @"Local\SteamVRCompanionLibrary." + NativeWindowActivation.InstanceScope;
    private static readonly string PipeName = "SteamVRCompanionLibrary.Activate." + NativeWindowActivation.InstanceScope;
    private readonly CancellationTokenSource stop = new();
    private readonly Task server;

    public SingleInstance(LibraryForm form)
    {
        server = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await pipe.WaitForConnectionAsync(stop.Token);
                    var request = new byte[1];
                    if (await pipe.ReadAsync(request, stop.Token) != 1 || request[0] != 1) continue;
                    var restored = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    form.BeginInvoke(() =>
                    {
                        try { form.RestoreAndActivate(); restored.TrySetResult(); }
                        catch (Exception e) { restored.TrySetException(e); }
                    });
                    await restored.Task.WaitAsync(stop.Token);
                    await pipe.WriteAsync(new byte[] { 1 }, stop.Token);
                    await pipe.FlushAsync(stop.Token);
                }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (InvalidOperationException) { break; }
                catch (IOException) { /* A closing second instance must not stop future activation requests. */ }
            }
        });
    }

    public static bool ActivateExisting()
    {
        try
        {
            // The foreground process launching the second instance can permit the original instance to activate.
            foreach (var process in Process.GetProcessesByName(Process.GetCurrentProcess().ProcessName))
            {
                using (process)
                    if (process.Id != Environment.ProcessId) NativeWindowActivation.AllowSetForegroundWindow(process.Id);
            }
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            pipe.Connect(3000);
            using var timeout = new CancellationTokenSource(3000);
            pipe.WriteAsync(new byte[] { 1 }, timeout.Token).AsTask().GetAwaiter().GetResult();
            pipe.Flush();
            var response = new byte[1];
            return pipe.ReadAsync(response, timeout.Token).AsTask().GetAwaiter().GetResult() == 1 && response[0] == 1;
        }
        catch (Exception e) when (e is IOException or TimeoutException or OperationCanceledException or UnauthorizedAccessException) { return false; }
    }

    public void Dispose()
    {
        stop.Cancel();
        // Do not block the UI thread while an activation callback may be queued on it.
        _ = server.ContinueWith(_ => stop.Dispose(), TaskScheduler.Default);
    }
}
