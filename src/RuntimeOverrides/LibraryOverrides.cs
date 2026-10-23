using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Collections.Generic;
using System.Xml;
using System.Security.Cryptography;

[assembly: AssemblyVersion("0.1.6.0")]
[assembly: AssemblyTitle("Virtual Desktop Library Overrides")]
[assembly: AssemblyFileVersion("0.1.6.0")]

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
                bool updated=assembly.GetName().Version==new Version(1,34,25,0);
                if (!updated && assembly.GetName().Version != new Version(1, 34, 23, 0))
                    throw new NotSupportedException("Library overrides support only verified Streamer 1.34.23.0 and 1.34.25.0 binaries.");
                string expectedHash=updated?"0229A322137F55687240D2B51F5F7002056D9277D7FA001EC6CB4EB25AF0BFB8":"FEAD548C57DF539006E6C97A7A29D46FDE172F756F40D65DBDB77ABAAFA3AEAD";
                using(var sha=SHA256.Create())
                    if(BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(assembly.Location))).Replace("-","")!=expectedHash)
                        throw new NotSupportedException("Streamer binary does not match a verified library override build.");
                string testDirectory = Environment.GetEnvironmentVariable("VD_LIBRARY_TEST_DATA");
                if (!String.IsNullOrEmpty(testDirectory))
                {
                    if (!Directory.Exists(testDirectory)) throw new DirectoryNotFoundException(testDirectory);
                    assembly.ManifestModule.ResolveField(updated?0x04014843:0x0401483f).SetValue(null, testDirectory);
                    assembly.ManifestModule.ResolveField(updated?0x04014844:0x04014840).SetValue(null, testDirectory);
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
                    uint[] ids = ReadOverrides();
                    // Explicit selections must also bypass built-in mod-file checks.
                    // Unselected built-in exceptions retain their original behavior.
                    foreach (uint id in ids) overrides[id] = Enum.ToObject(arguments[1], 3);
                    Log("Installed OpenXR overrides: " + String.Join(",", ids) + "; entries=" + originalCount + " -> " + overrides.Count + "; executable=" + assembly.Location);
                    if (String.IsNullOrEmpty(testDirectory) || Environment.GetEnvironmentVariable("VD_BITRATE_TEST") == "1")
                    {
                        try { BitrateRuntime.Install(assembly,Log); }
                        catch (Exception e) { Log("Bitrate hook unavailable; original bitrate behavior retained: "+e); }
                    }
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

        private static uint[] ReadOverrides()
        {
            string path = Environment.GetEnvironmentVariable("VD_LIBRARY_OVERRIDES_FILE");
            if (String.IsNullOrEmpty(path)) path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Virtual Desktop Unlock", "GameOverrides.xml");
            // Preserve the working RE3 behavior until the updated app creates the shared selection.
            if (!File.Exists(path)) return new uint[] { 952060 };
            if (new FileInfo(path).Length > 131072) throw new InvalidDataException("Override file is too large.");
            var ids = new SortedSet<uint>();
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 131072 };
            using (var reader = XmlReader.Create(path, settings))
            {
                reader.MoveToContent();
                if (reader.Name != "VirtualDesktopLibrary" || reader.GetAttribute("version") != "1") throw new InvalidDataException("Unsupported override file format.");
                while (reader.Read())
                {
                    if (reader.NodeType != XmlNodeType.Element) continue;
                    uint id;
                    if (reader.Name != "Game" || !UInt32.TryParse(reader.GetAttribute("appId"), out id) || id == 0) throw new InvalidDataException("Invalid Steam app ID in override file.");
                    ids.Add(id);
                    if (ids.Count > 1000) throw new InvalidDataException("Too many game overrides.");
                }
            }
            var result = new uint[ids.Count]; ids.CopyTo(result); return result;
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
