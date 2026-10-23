using System.Text;

namespace SteamVRLibrary;

// Read Steam's original flags: VD's persisted OpenXRSupport also includes our overrides.
internal static class SteamMetadata
{
    public static HashSet<string> ReadVrIds(string path, HashSet<string> candidates)
    {
        HashSet<string> result = [];
        if (!File.Exists(path) || candidates.Count == 0) return result;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new BinaryReader(stream, Encoding.UTF8);
        var magic = reader.ReadUInt32();
        if (magic is not (0x07564427 or 0x07564428 or 0x07564429))
            throw new InvalidDataException("Unsupported Steam metadata format.");
        reader.ReadUInt32(); // universe
        string[]? strings = null;
        if (magic == 0x07564429)
        {
            var offset = reader.ReadInt64();
            if (offset < stream.Position || offset > stream.Length - 4) throw new InvalidDataException("Invalid Steam string table.");
            var position = stream.Position;
            stream.Position = offset;
            var count = reader.ReadUInt32();
            if (count > 1_000_000) throw new InvalidDataException("Steam string table is too large.");
            strings = new string[count];
            for (var i = 0; i < count; i++) strings[i] = ReadString(reader, stream.Length);
            stream.Position = position;
        }
        while (stream.Position <= stream.Length - 4)
        {
            var id = reader.ReadUInt32();
            if (id == 0) break;
            var size = reader.ReadUInt32();
            var end = checked(stream.Position + size);
            var headerSize = magic == 0x07564427 ? 40 : 60;
            if (size < headerSize || end > stream.Length) throw new InvalidDataException("Invalid Steam app record.");
            var key = id.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (candidates.Contains(key))
            {
                stream.Position += headerSize;
                if (ReadObject(reader, end, strings, "", 0)) result.Add(key);
            }
            stream.Position = end;
        }
        return result;
    }

    private static string ReadString(BinaryReader reader, long end)
    {
        var bytes = new List<byte>();
        while (reader.BaseStream.Position < end && bytes.Count < 1_048_576)
        {
            var value = reader.ReadByte();
            if (value == 0) return Encoding.UTF8.GetString(bytes.ToArray());
            bytes.Add(value);
        }
        throw new InvalidDataException("Unterminated Steam metadata string.");
    }

    private static bool ReadObject(BinaryReader reader, long end, string[]? strings, string parent, int depth)
    {
        if (depth > 64) throw new InvalidDataException("Steam metadata nesting is too deep.");
        var found = false;
        while (reader.BaseStream.Position < end)
        {
            var type = reader.ReadByte();
            if (type == 8) return found;
            string name;
            if (strings == null) name = ReadString(reader, end);
            else
            {
                var index = reader.ReadUInt32();
                if (index >= strings.Length) throw new InvalidDataException("Invalid Steam metadata key.");
                name = strings[index];
            }
            var path = parent.Length == 0 ? name : parent + "/" + name;
            var vrFlag = path is "appinfo/common/openvrsupport" or "appinfo/common/openxrsupport" or "appinfo/common/othervrsupport";
            switch (type)
            {
                case 0: found |= ReadObject(reader, end, strings, path, depth + 1); break;
                case 1:
                    var text = ReadString(reader, end);
                    found |= vrFlag && (text == "1" || text.Equals("true", StringComparison.OrdinalIgnoreCase));
                    break;
                case 2: found |= reader.ReadInt32() != 0 && vrFlag; break;
                case 3: case 4: case 6: reader.BaseStream.Position += 4; break;
                case 7: case 10: reader.BaseStream.Position += 8; break;
                default: throw new InvalidDataException("Unsupported Steam metadata value.");
            }
            if (reader.BaseStream.Position > end) throw new InvalidDataException("Truncated Steam metadata value.");
        }
        throw new InvalidDataException("Unterminated Steam metadata object.");
    }
}
