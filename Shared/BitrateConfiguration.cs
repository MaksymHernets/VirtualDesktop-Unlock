using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Xml;

namespace VirtualDesktop.Unlock
{
    public sealed class BitrateProfile
    {
        public int H264Mbps;
        public int HevcMbps;
        public int Av1Mbps;
        public BitrateProfile(int h264, int hevc, int av1) { H264Mbps = h264; HevcMbps = hevc; Av1Mbps = av1; }
        public BitrateProfile Copy() { return new BitrateProfile(H264Mbps, HevcMbps, Av1Mbps); }
    }

    public sealed class BitrateConfiguration
    {
        public bool Enabled;
        public readonly Dictionary<int, BitrateProfile> Profiles = new Dictionary<int, BitrateProfile>();
        public static string DefaultPath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Virtual Desktop Unlock", "BitrateOverrides.xml"); } }
        public static readonly Dictionary<int, string> Headsets = new Dictionary<int, string> {
            {259,"Quest"},{320,"Quest 2"},{400,"Quest Pro"},{600,"Vive Focus 3"},{650,"Vive XR Elite"},
            {700,"Pico Neo 3"},{701,"Pico Neo 3 Link"},{800,"Pico 4"},{1000,"Quest 3"},{1001,"Quest 3S"},
            {1010,"Pico 4 Ultra"},{1100,"Samsung Galaxy XR"},{1400,"Play for Dream MR"},{1500,"Steam Frame"},
            {1600,"XREAL Aura"},{1700,"Pico Space Pro"},{1710,"Meta VR Glasses"}
        };
        public static BitrateConfiguration Defaults()
        {
            var result = new BitrateConfiguration();
            result.Profiles.Add(1000, new BitrateProfile(1000, 300, 250));
            return result;
        }
        public BitrateConfiguration Copy()
        {
            var copy = new BitrateConfiguration { Enabled = Enabled };
            foreach (var pair in Profiles) copy.Profiles.Add(pair.Key, pair.Value.Copy());
            return copy;
        }
        public static BitrateConfiguration Read(string path)
        {
            if (!File.Exists(path)) return Defaults();
            if (new FileInfo(path).Length > 65536) throw new InvalidDataException("Bitrate configuration exceeds 64 KB.");
            var result = new BitrateConfiguration();
            using (var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 65536 }))
            {
                reader.MoveToContent();
                if (reader.Name != "BitrateOverrides" || reader.GetAttribute("version") != "1" || !Boolean.TryParse(reader.GetAttribute("enabled"), out result.Enabled))
                    throw new InvalidDataException("Invalid bitrate configuration header.");
                while (reader.Read())
                {
                    if (reader.NodeType != XmlNodeType.Element) continue;
                    int headset;
                    if (reader.Name != "Profile" || reader.Depth != 1 || !Int32.TryParse(reader.GetAttribute("headset"), out headset) || !Headsets.ContainsKey(headset) || result.Profiles.ContainsKey(headset))
                        throw new InvalidDataException("Invalid or duplicate headset bitrate profile.");
                    result.Profiles.Add(headset, new BitrateProfile(ReadMbps(reader,"h264"), ReadMbps(reader,"hevc"), ReadMbps(reader,"av1")));
                }
            }
            return result;
        }
        private static int ReadMbps(XmlReader reader, string name)
        {
            int value;
            if (!Int32.TryParse(reader.GetAttribute(name), out value) || value < 10 || value > 2000) throw new InvalidDataException("Bitrate limits must be between 10 and 2000 Mbps.");
            return value;
        }
        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".");
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var writer = XmlWriter.Create(temp, new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) }))
                {
                    writer.WriteStartElement("BitrateOverrides"); writer.WriteAttributeString("version","1"); writer.WriteAttributeString("enabled", Enabled ? "true" : "false");
                    var keys = new List<int>(Profiles.Keys); keys.Sort();
                    foreach (int headset in keys)
                    {
                        var p = Profiles[headset];
                        if (!Headsets.ContainsKey(headset) || p.H264Mbps < 10 || p.H264Mbps > 2000 || p.HevcMbps < 10 || p.HevcMbps > 2000 || p.Av1Mbps < 10 || p.Av1Mbps > 2000) throw new InvalidDataException("Invalid bitrate profile.");
                        writer.WriteStartElement("Profile"); writer.WriteAttributeString("headset",headset.ToString());
                        writer.WriteAttributeString("h264",p.H264Mbps.ToString()); writer.WriteAttributeString("hevc",p.HevcMbps.ToString()); writer.WriteAttributeString("av1",p.Av1Mbps.ToString()); writer.WriteEndElement();
                    }
                    writer.WriteEndElement();
                }
                if (File.Exists(path)) File.Replace(temp,path,path+".bak"); else File.Move(temp,path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        public static string Revision(string path)
        {
            if (!File.Exists(path)) return "missing";
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
        }
        public int Limit(int headset, int codec, int stock)
        {
            if (!Enabled || !Profiles.ContainsKey(headset)) return stock;
            var profile = Profiles[headset];
            int requested = codec == 1 || codec == 5 ? profile.H264Mbps : codec == 2 || codec == 6 ? profile.HevcMbps : codec == 10 || codec == 11 ? profile.Av1Mbps : 0;
            return requested == 0 ? stock : Math.Max(stock, checked(requested * 1000000));
        }
    }
}
