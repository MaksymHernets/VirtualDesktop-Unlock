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
                if (File.Exists(log) && File.ReadAllText(log).Contains("Installed OpenXR overrides:"))
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
            string expected = Environment.GetEnvironmentVariable("VD_LIBRARY_TEST_EXPECTED_IDS") ?? "952060";
            string[] ids = expected.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            bool matches = true;
            uint[] builtins = {271590,1174180,1091500,1030840,1030830,360430,1151640,1245620,6020,6030,622650,9010,2537590};
            int added = 0;
            foreach (string id in ids)
            {
                uint key = UInt32.Parse(id);
                bool correct = dictionary.Contains(key) && Convert.ToInt32(dictionary[key]) == 3;
                Console.WriteLine("Override " + id + " is unconditional OpenXR: " + correct); matches &= correct;
                if (Array.IndexOf(builtins, key) < 0) added++;
            }
            foreach (uint id in builtins)
            {
                int expectedType = id == 2537590 ? 3 : id == 6020 || id == 6030 || id == 622650 || id == 9010 ? 2 : 1;
                if (Array.IndexOf(ids, id.ToString()) < 0)
                    matches &= dictionary.Contains(id) && Convert.ToInt32(dictionary[id]) == expectedType;
            }
            Console.WriteLine("Exception count: " + dictionary.Count);
            return matches && dictionary.Count == 13 + added ? 0 : 2;
        }
        return 3;
    }
}
