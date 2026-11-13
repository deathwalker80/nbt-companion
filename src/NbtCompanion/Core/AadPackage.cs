using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace NbtCompanion.Core
{
    /// <summary>One NBT-AAD release ready to be written: daemon + the five plugin files, all checked on the PC.</summary>
    public sealed class AadPackage
    {
        public static readonly string[] PluginFiles =
        {
            "libNbtAad.so", "NbtAad.manifest", "NbtAad.xml", "NbtAad_ALL_Texts.zip", "NbtAad_ALL_Images.zip",
        };

        public string Tag { get; set; } = "";
        public byte[] Daemon { get; set; } = Array.Empty<byte>();
        public Dictionary<string, byte[]> Plugin { get; } = new Dictionary<string, byte[]>();
        public string Build { get; set; } = "";
        public bool HasLicenseFlag { get; set; }
        public bool HasPfFlags { get; set; }

        public static AadPackage FromRelease(string tag, byte[] daemon, byte[] releasesZip)
        {
            var p = new AadPackage { Tag = tag, Daemon = daemon };
            CheckQnxArmProgram(daemon, "nbt-aad");
            p.Build = BuildOf(daemon);
            p.HasLicenseFlag = Contains(daemon, "--license");
            p.HasPfFlags = Contains(daemon, "nbt-aad --pf-install");
            using (var zip = new ZipArchive(new MemoryStream(releasesZip), ZipArchiveMode.Read))
            {
                foreach (string name in PluginFiles)
                {
                    List<ZipArchiveEntry> hits = zip.Entries.Where(e => e.Name == name).ToList();
                    if (hits.Count != 1)
                    {
                        throw new StepException("Releases.zip: " + name + (hits.Count == 0
                            ? L.T(" manca", " is missing")
                            : L.T(" c'e' " + hits.Count + " volte", " is there " + hits.Count + " times")));
                    }
                    using var ms = new MemoryStream();
                    using (Stream s = hits[0].Open())
                    {
                        s.CopyTo(ms);
                    }
                    if (ms.Length == 0)
                    {
                        throw new StepException("Releases.zip: " + name + L.T(" e' vuoto", " is empty"));
                    }
                    p.Plugin[name] = ms.ToArray();
                }
            }
            CheckQnxArmLibrary(p.Plugin["libNbtAad.so"], "libNbtAad.so");
            foreach (string z in new[] { "NbtAad_ALL_Texts.zip", "NbtAad_ALL_Images.zip" })
            {
                CheckZip(p.Plugin[z], z);
            }
            string xml = Encoding.UTF8.GetString(p.Plugin["NbtAad.xml"]);
            if (MenuEntries(xml).Count == 0)
            {
                throw new StepException(L.T("NbtAad.xml: nessuna voce pluginApp trovata.", "NbtAad.xml: no pluginApp entry found."));
            }
            Log.Write("Pacchetto " + tag + ": nbt-aad " + daemon.Length + " byte, build " + (p.Build.Length > 0 ? p.Build : "(non scritta nel binario)")
                + ", --license " + (p.HasLicenseFlag ? "si" : "no") + ", pf_aa integrato " + (p.HasPfFlags ? "si" : "no"));
            return p;
        }

        public long TotalBytes
        {
            get { return Daemon.Length + Plugin.Values.Sum(b => (long)b.Length); }
        }

        /// <summary>
        /// Program for the head unit: ELF 32 bit, little endian, ARM (e_machine 40), executable (e_type 2),
        /// QNX loader /usr/lib/ldqnx.so.2 as interpreter.
        /// </summary>
        public static void CheckQnxArmProgram(byte[] b, string what)
        {
            int type = CheckArmElf(b, what);
            if (type != 2)
            {
                throw new StepException(what + L.T(": non e' un programma eseguibile (tipo ELF ", ": not an executable program (ELF type ") + type + ").");
            }
            if (!Contains(b, "ldqnx.so"))
            {
                throw new StepException(what + L.T(": non e' un programma QNX (manca ldqnx.so).", ": not a QNX program (no ldqnx.so)."));
            }
        }

        /// <summary>
        /// Library for the HMI: ELF 32 bit, little endian, ARM, shared object (e_type 3). A shared object has no
        /// interpreter: it is recognised as QNX by its C library (libc.so.N, never Linux's ld-linux / libc.so.6).
        /// </summary>
        public static void CheckQnxArmLibrary(byte[] b, string what)
        {
            int type = CheckArmElf(b, what);
            if (type != 3)
            {
                throw new StepException(what + L.T(": non e' una libreria condivisa (tipo ELF ", ": not a shared library (ELF type ") + type + ").");
            }
            if (!Contains(b, "libc.so.") || Contains(b, "ld-linux") || Contains(b, "libc.so.6"))
            {
                throw new StepException(what + L.T(": non e' una libreria QNX.", ": not a QNX library."));
            }
        }

        /// <summary>Common header checks; returns e_type.</summary>
        private static int CheckArmElf(byte[] b, string what)
        {
            bool elf = b.Length > 52 && b[0] == 0x7F && b[1] == (byte)'E' && b[2] == (byte)'L' && b[3] == (byte)'F';
            if (!elf || b[4] != 1 || b[5] != 1)
            {
                throw new StepException(what + L.T(": non e' un file ELF ARM a 32 bit.", ": not a 32-bit ARM ELF file."));
            }
            int machine = b[18] | (b[19] << 8);
            if (machine != 40)
            {
                throw new StepException(what + L.T(": ELF non ARM (macchina ", ": ELF not ARM (machine ") + machine + ").");
            }
            return b[16] | (b[17] << 8);
        }

        private static void CheckZip(byte[] b, string what)
        {
            try
            {
                using var z = new ZipArchive(new MemoryStream(b), ZipArchiveMode.Read);
                if (z.Entries.Count == 0)
                {
                    throw new StepException(what + L.T(": zip vuoto.", ": empty zip."));
                }
            }
            catch (InvalidDataException)
            {
                throw new StepException(what + L.T(": non e' uno zip valido.", ": not a valid zip."));
            }
        }

        public static bool Contains(byte[] hay, string text)
        {
            byte[] needle = Encoding.ASCII.GetBytes(text);
            return hay.AsSpan().IndexOf(needle) >= 0;
        }

        /// <summary>"  Build: v1.9.62 (%s %s)" is a literal of the binary (main.cpp): it gives the version without running it.</summary>
        public static string BuildOf(byte[] daemon)
        {
            string s = Encoding.Latin1.GetString(daemon);
            Match m = Regex.Match(s, @"  Build: ([\x21-\x7E]+) \(%s %s\)");
            return m.Success ? m.Groups[1].Value : "";
        }

        // ------------------------------------------------------------------ menu position of the two entries

        public sealed class MenuEntry
        {
            public string Text { get; set; } = "";
            public string StateId { get; set; } = "";
        }

        private static readonly Regex PluginAppTag = new Regex(@"<pluginApp\b[^>]*>", RegexOptions.Compiled);
        private static readonly Regex TextAttr = new Regex(@"\btext=""([^""]*)""", RegexOptions.Compiled);
        private static readonly Regex StateAttr = new Regex(@"\binsertStateID=""(\d+)""", RegexOptions.Compiled);

        public static List<MenuEntry> MenuEntries(string xml)
        {
            var list = new List<MenuEntry>();
            foreach (Match m in PluginAppTag.Matches(xml))
            {
                Match t = TextAttr.Match(m.Value);
                Match s = StateAttr.Match(m.Value);
                list.Add(new MenuEntry { Text = t.Success ? t.Groups[1].Value : "", StateId = s.Success ? s.Groups[1].Value : "" });
            }
            return list;
        }

        /// <summary>
        /// Sets insertStateID of the entries "Android Auto" and "Open Android Auto" (20 = Media/Radio,
        /// 50 = MINI Connected / ConnectedDrive). Only those attribute values change; every other byte stays.
        /// </summary>
        public static byte[] SetMenuState(byte[] xmlBytes, string state, out int changed)
        {
            bool bom = xmlBytes.Length >= 3 && xmlBytes[0] == 0xEF && xmlBytes[1] == 0xBB && xmlBytes[2] == 0xBF;
            string xml = Encoding.UTF8.GetString(xmlBytes, bom ? 3 : 0, xmlBytes.Length - (bom ? 3 : 0));
            int count = 0;
            string result = PluginAppTag.Replace(xml, m =>
            {
                Match t = TextAttr.Match(m.Value);
                if (!t.Success || (t.Groups[1].Value != "Android Auto" && t.Groups[1].Value != "Open Android Auto"))
                {
                    return m.Value;
                }
                if (!StateAttr.IsMatch(m.Value))
                {
                    return m.Value;
                }
                count++;
                return StateAttr.Replace(m.Value, "insertStateID=\"" + state + "\"", 1);
            });
            changed = count;
            byte[] body = new UTF8Encoding(false).GetBytes(result);
            if (!bom)
            {
                return body;
            }
            var all = new byte[body.Length + 3];
            all[0] = 0xEF;
            all[1] = 0xBB;
            all[2] = 0xBF;
            Buffer.BlockCopy(body, 0, all, 3, body.Length);
            return all;
        }

        public static string MenuStateName(string id)
        {
            switch (id)
            {
                case "20":
                    return "Media/Radio";
                case "50":
                    return "MINI Connected / ConnectedDrive";
                default:
                    return "stato " + id;
            }
        }
    }
}
