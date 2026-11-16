using System.Collections.Generic;
using System.Text;

namespace NbtCompanion.Core
{
    /// <summary>What the connected head unit is, read from the unit itself (read only).</summary>
    public sealed class UnitInfo
    {
        /// <summary>
        /// Head-unit software version, the one iDrive shows under Navigation > Settings > Position and software
        /// version > Software version (e.g. NBTevo_Y21432J). Same file and same rule nbt-aad uses: first line,
        /// trimmed, /opt/sys/etc/nbt_version.txt then /etc/nbt_version.txt.
        /// </summary>
        public static readonly string[] VersionFiles = { "/opt/sys/etc/nbt_version.txt", "/etc/nbt_version.txt" };

        public string SwVersion { get; set; } = "";
        public string VersionFile { get; set; } = "";
        public List<string> Brands { get; set; } = new List<string>();

        /// <summary>Every software version of the NBT EVO starts with "NBTevo_" (list of the cars confirmed for NBT-AAD).</summary>
        public bool IsNbtEvo
        {
            get { return SwVersion.StartsWith("NBTevo_", System.StringComparison.Ordinal); }
        }

        public string Family
        {
            get
            {
                if (SwVersion.Length == 0)
                {
                    return L.T("non letta", "not read");
                }
                return IsNbtEvo ? "NBT EVO" : L.T("non riconosciuta", "not recognised");
            }
        }

        public string Summary()
        {
            string v = SwVersion.Length > 0 ? SwVersion : L.T("versione software non letta", "software version not read");
            if (Brands.Count > 0)
            {
                v += " - " + string.Join(", ", Brands);
            }
            return v;
        }

        public static UnitInfo Read(HeadUnit hu, List<string> brands)
        {
            var u = new UnitInfo { Brands = brands };
            foreach (string f in VersionFiles)
            {
                byte[]? b = hu.Download(f);
                if (b == null)
                {
                    continue;
                }
                string v = ParseVersion(b);
                if (v.Length > 0)
                {
                    u.SwVersion = v;
                    u.VersionFile = f;
                    break;
                }
            }
            Log.Write("Testata: unita' " + u.Family + ", versione software " + (u.SwVersion.Length > 0 ? u.SwVersion + " (" + u.VersionFile + ")" : "non letta")
                + ", marca nell'HMI " + (brands.Count > 0 ? string.Join(", ", brands) : "non riconosciuta"));
            return u;
        }

        /// <summary>First line, trimmed (the file on the car ends with CRLF); only printable ASCII, at most 64 characters.</summary>
        public static string ParseVersion(byte[] data)
        {
            string text = Encoding.Latin1.GetString(data);
            int end = text.IndexOfAny(new[] { '\r', '\n' });
            string line = (end >= 0 ? text.Substring(0, end) : text).Trim();
            if (line.Length == 0 || line.Length > 64)
            {
                return "";
            }
            foreach (char c in line)
            {
                if (c < 0x20 || c > 0x7E)
                {
                    return "";
                }
            }
            return line;
        }
    }
}
