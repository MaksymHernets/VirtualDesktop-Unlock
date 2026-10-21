using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;

[assembly: AssemblyVersion("0.1.0.0")]
[assembly: AssemblyTitle("Virtual Desktop RE3 Library Override")]
[assembly: AssemblyFileVersion("0.1.0.0")]

namespace VirtualDesktop.LibraryOverrides
{
    public sealed class Manager : AppDomainManager
    {
        private static int installed;
        private static int queued;
        public override void InitializeNewDomain(AppDomainSetup setup)
        {
            base.InitializeNewDomain(setup);
            AppDomain.CurrentDomain.AssemblyLoad += delegate(object sender, AssemblyLoadEventArgs e) { TryInstall(e.LoadedAssembly); };
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies()) TryInstall(assembly);
        }

        private static void TryInstall(Assembly assembly)
        {
            if (assembly.GetName().Name != "VirtualDesktop.Streamer") return;
            if (String.IsNullOrEmpty(Environment.GetEnvironmentVariable("VD_LIBRARY_TEST_DATA")) || Environment.GetEnvironmentVariable("VD_LIBRARY_TEST_DELAYED") == "1")
            {
                if (Interlocked.CompareExchange(ref queued, 1, 0) != 0) return;
                ThreadPool.QueueUserWorkItem(delegate
                {
                    try
                    {
                        DateTime deadline = DateTime.UtcNow.AddSeconds(60);
                        while (DateTime.UtcNow < deadline)
                        {
                            Assembly entry = Assembly.GetEntryAssembly();
                            var application = System.Windows.Application.Current;
                            if (entry != null && application != null && application.Dispatcher != null && !application.Dispatcher.HasShutdownStarted)
                            {
                                application.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Send,
                                    new Action(delegate { InstallOverride(assembly); }));
                                return;
                            }
                            Thread.Sleep(100);
                        }
                        Log("Timed out waiting for Streamer application initialization; no override applied.");
                    }
                    catch (Exception e) { Log("Waiting for application startup failed: " + e); }
                });
                return;
            }
            InstallOverride(assembly);
        }

        private static void InstallOverride(Assembly assembly)
        {
            if (Interlocked.CompareExchange(ref installed, 1, 0) != 0) return;
            try
            {
                if (assembly.GetName().Version != new Version(1, 34, 23, 0))
                    throw new NotSupportedException("This library override was verified only with Streamer 1.34.23.0.");
                string testDirectory = Environment.GetEnvironmentVariable("VD_LIBRARY_TEST_DATA");
                if (!String.IsNullOrEmpty(testDirectory))
                {
                    if (!Directory.Exists(testDirectory)) throw new DirectoryNotFoundException(testDirectory);
                    assembly.ManifestModule.ResolveField(0x0401483f).SetValue(null, testDirectory);
                    assembly.ManifestModule.ResolveField(0x04014840).SetValue(null, testDirectory);
                }
                Type manager = assembly.GetType("VirtualDesktop.Streamer.GameManager", true);
                RuntimeHelpers.RunClassConstructor(manager.TypeHandle);
                foreach (FieldInfo field in manager.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public))
                {
                    Type type = field.FieldType;
                    if (!type.IsGenericType || type.GetGenericTypeDefinition() != typeof(System.Collections.Generic.Dictionary<,>)) continue;
                    Type[] arguments = type.GetGenericArguments();
                    if (arguments[0] != typeof(uint) || !arguments[1].IsEnum) continue;
                    IDictionary overrides = (IDictionary)field.GetValue(null);
                    int originalCount = overrides.Count;
                    overrides[(uint)952060] = Enum.ToObject(arguments[1], 3);
                    Log("Installed RE3 OpenXR override; entries=" + originalCount + " -> " + overrides.Count + "; executable=" + assembly.Location);
                    return;
                }
                throw new MissingFieldException("GameManager mod override dictionary was not found.");
            }
            catch (Exception e)
            {
                // Fail open for normal Streamer startup; the original program remains usable.
                Log("Override failed: " + e);
            }
        }

        private static void Log(string text)
        {
            try
            {
                string log = Environment.GetEnvironmentVariable("VD_LIBRARY_OVERRIDE_LOG");
                if (String.IsNullOrEmpty(log)) log = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Virtual Desktop", "LibraryOverrides.log");
                File.AppendAllText(log, DateTimeOffset.Now.ToString("O") + " [" + System.Diagnostics.Process.GetCurrentProcess().Id + "] " + text + Environment.NewLine);
            }
            catch { }
        }
    }
}
