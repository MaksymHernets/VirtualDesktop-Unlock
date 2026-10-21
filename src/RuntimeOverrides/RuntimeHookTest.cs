using System;
using System.Collections;
using System.IO;
using System.Reflection;

public static class RuntimeHookTest
{
    public static int Main(string[] args)
    {
        Console.WriteLine("Domain manager: " + (AppDomain.CurrentDomain.DomainManager == null ? "none" : AppDomain.CurrentDomain.DomainManager.GetType().FullName));
        if (AppDomain.CurrentDomain.DomainManager == null) return 1;
        if (args.Length == 0) return 0;
        if (args.Length > 1 && args[1] == "--delayed")
        {
            var application = new System.Windows.Application();
            var assembly = Assembly.LoadFrom(args[0]);
            int result = 4;
            DateTime deadline = DateTime.UtcNow.AddSeconds(10);
            var timer = new System.Windows.Threading.DispatcherTimer();
            timer.Interval = TimeSpan.FromMilliseconds(200);
            timer.Tick += delegate
            {
                string log = Environment.GetEnvironmentVariable("VD_LIBRARY_OVERRIDE_LOG");
                if (File.Exists(log) && File.ReadAllText(log).Contains("Installed RE3 OpenXR override"))
                { result = Check(assembly); timer.Stop(); application.Shutdown(); }
                else if (DateTime.UtcNow > deadline) { timer.Stop(); application.Shutdown(); }
            };
            timer.Start(); application.Run();
            return result;
        }
        return Check(Assembly.LoadFrom(args[0]));
    }

    private static int Check(Assembly assembly)
    {
        var manager = assembly.GetType("VirtualDesktop.Streamer.GameManager", true);
        foreach (var field in manager.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public))
        {
            var type = field.FieldType;
            if (!type.IsGenericType || type.GetGenericTypeDefinition() != typeof(System.Collections.Generic.Dictionary<,>)) continue;
            var types = type.GetGenericArguments();
            if (types[0] != typeof(uint) || !types[1].IsEnum) continue;
            var dictionary = (IDictionary)field.GetValue(null);
            Console.WriteLine("RE3 override present: " + dictionary.Contains((uint)952060));
            Console.WriteLine("Exception count: " + dictionary.Count);
            return dictionary.Contains((uint)952060) && dictionary.Count == 14 ? 0 : 2;
        }
        return 3;
    }
}
