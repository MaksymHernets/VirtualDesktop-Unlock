using System.Text;

namespace SteamVRLibrary;

internal static class SteamMetadataTests
{
    public static void Run(string steamRoot, Action<bool, string> check)
    {
        var path = Path.Combine(steamRoot, "appcache", "appinfo.vdf");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string[] names = ["appinfo", "common", "openvrsupport", "openxrsupport", "othervrsupport", "config"];
        foreach (var version in new[] { 0x07564427u, 0x07564428u, 0x07564429u })
        {
            using var file = new MemoryStream();
            using var writer = new BinaryWriter(file, Encoding.UTF8, true);
            writer.Write(version); writer.Write(1u);
            if (version == 0x07564429) writer.Write(0L);
            void Key(BinaryWriter w, byte type, string name)
            {
                w.Write(type);
                if (version == 0x07564429) w.Write((uint)Array.IndexOf(names, name));
                else { w.Write(Encoding.UTF8.GetBytes(name)); w.Write((byte)0); }
            }
            void Entry(uint id, string flag, bool enabled, bool nestedDecoy = false)
            {
                using var data = new MemoryStream();
                using var kv = new BinaryWriter(data, Encoding.UTF8, true);
                Key(kv, 0, "appinfo"); Key(kv, 0, "common");
                Key(kv, 1, flag); kv.Write(Encoding.UTF8.GetBytes(enabled ? "1" : "0")); kv.Write((byte)0);
                kv.Write((byte)8);
                if (nestedDecoy)
                {
                    Key(kv, 0, "config"); Key(kv, 1, flag); kv.Write(Encoding.UTF8.GetBytes("1")); kv.Write((byte)0); kv.Write((byte)8);
                }
                kv.Write((byte)8); kv.Write((byte)8);
                var headerSize = version == 0x07564427 ? 40 : 60;
                writer.Write(id); writer.Write((uint)(headerSize + data.Length));
                writer.Write(new byte[headerSize]); writer.Write(data.ToArray());
            }
            Entry(111, "openvrsupport", true);
            Entry(952060, "openxrsupport", false, true);
            Entry(999, "openxrsupport", true);
            Entry(222, "othervrsupport", true);
            Entry(333, "openvrsupport", true);
            writer.Write(0u);
            if (version == 0x07564429)
            {
                var tableOffset = file.Position;
                writer.Write((uint)names.Length);
                foreach (var name in names) { writer.Write(Encoding.UTF8.GetBytes(name)); writer.Write((byte)0); }
                file.Position = 8; writer.Write(tableOffset);
            }
            File.WriteAllBytes(path, file.ToArray());
            check(SteamMetadata.ReadVrIds(path, ["111", "952060", "999", "222"]).SetEquals(["111", "999", "222"]),
                $"Steam metadata {version:X} recognizes original common VR flags, excludes overrides, nested decoys and uncached games");
        }
        var valid = File.ReadAllBytes(path);
        File.WriteAllBytes(path, valid[..20]);
        var rejected = false;
        try { SteamMetadata.ReadVrIds(path, ["111"]); }
        catch (Exception e) when (e is IOException or InvalidDataException) { rejected = true; }
        check(rejected, "Truncated Steam metadata cannot produce a blue classification");
        File.WriteAllBytes(path, valid);
    }
}
