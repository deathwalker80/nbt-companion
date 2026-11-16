using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace NbtCompanion.Core
{
    /// <summary>One package of the nbt-evo-apps stick (pacchetti/&lt;Name&gt;/package.txt).</summary>
    public sealed class EvoPackage
    {
        public string Name { get; set; } = "";
        public string Title { get; set; } = "";
        public string Version { get; set; } = "";
        public string Brands { get; set; } = "";
        public bool PhoneVariant { get; set; }
        public int FileCount { get; set; }

        /// <summary>Tried in a car and working (MINI Sport, 16/11) or marked status=tested in its package.txt.</summary>
        public bool Tested { get; set; }

        /// <summary>yes = for this car, no = for another brand, "" = the car is not chosen yet.</summary>
        public string ForCar(string car)
        {
            if (Brands.Length == 0 || Brands == "all")
            {
                return "yes";
            }
            if (car.Length == 0)
            {
                return "";
            }
            return Brands.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(car) ? "yes" : "no";
        }
    }

    /// <summary>
    /// nbt-evo-apps (apps that run on the head unit, e.g. MINI Sport) through its own package manager pkg.sh.
    /// The release zip (nbt-evo-apps*.zip with the stick folder nbt-evo-apps/) is checked here first: SHA-256 of
    /// the asset, files.sum of every package (POSIX cksum, the same pkg.sh checks), QNX ARM libraries. Then it is
    /// copied into omap's RAM and pkg.sh runs from there with NEA_STICK pointing at that copy: the script is the
    /// one of the stick, not a modified copy. Before an update pkg.sh keeps the previous version (only one), a
    /// removed app goes to /opt/aa/plugins_off. Nothing starts by itself; the reboot is by hand.
    /// </summary>
    public static class EvoApps
    {
        public const string Repo = "deathwalker80/nbt-evo-apps";
        public const string Folder = "nbt-evo-apps";
        public const string Wrapper = "nbtc-pkg.sh";
        public const string DataDir = "/opt/aa/nbt-evo-apps";

        /// <summary>Packages tried in the car and working (user, 16/11: MINI Sport build "f").</summary>
        private static readonly string[] TestedInCar = { "MiniSport" };

        private static readonly Regex AssetName = new Regex(@"^nbt-evo-apps[A-Za-z0-9._-]*\.zip$", RegexOptions.Compiled);

        public sealed class Found
        {
            public GhRelease Release { get; set; } = null!;
            public GhAsset Asset { get; set; } = null!;
        }

        public sealed class EvoKit
        {
            public Kit Kit { get; set; } = null!;
            public List<EvoPackage> Packages { get; } = new List<EvoPackage>();
            public byte[] Zip { get; set; } = Array.Empty<byte>();
            public string AssetName { get; set; } = "";
        }

        /// <summary>Latest release (not draft, not pre-release) with the stick zip.</summary>
        public static Found? Latest(IEnumerable<GhRelease> releases)
        {
            foreach (GhRelease r in releases.Where(r => !r.Prerelease).OrderByDescending(r => r.Published))
            {
                GhAsset? a = r.Assets.FirstOrDefault(x => AssetName.IsMatch(x.Name));
                if (a != null)
                {
                    return new Found { Release = r, Asset = a };
                }
            }
            return null;
        }

        public static EvoKit Fetch(IEnumerable<GhRelease> releases)
        {
            Found f = Latest(releases) ?? throw new StepException(L.T(
                "Nessuna release di " + Repo + " ha ancora lo zip della chiavetta (nbt-evo-apps*.zip).",
                "No release of " + Repo + " has the stick zip (nbt-evo-apps*.zip) yet."));
            byte[] zip = GitHub.DownloadAsset(f.Release, f.Asset, false);
            EvoKit k = FromZip(zip, f.Release.Name + " / " + f.Asset.Name);
            k.AssetName = f.Asset.Name;
            return k;
        }

        public static EvoKit FromZip(byte[] zip, string label)
        {
            var kit = new Kit { Folder = Folder, ReleaseName = label };
            try
            {
                using var z = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
                foreach (ZipArchiveEntry e in z.Entries)
                {
                    if (e.FullName.EndsWith("/") || !e.FullName.StartsWith(Folder + "/", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    string rel = e.FullName.Substring(Folder.Length + 1);
                    if (rel.Contains(' ') || rel.Contains('\'') || rel.Contains("..") || rel.Contains('\\'))
                    {
                        throw new StepException(label + L.T(": nome di file non ammesso: ", ": file name not allowed: ") + rel);
                    }
                    using var ms = new MemoryStream();
                    using (Stream s = e.Open())
                    {
                        s.CopyTo(ms);
                    }
                    kit.Files[rel] = ms.ToArray();
                }
            }
            catch (InvalidDataException ex)
            {
                throw new StepException(label + L.T(": zip non valido: ", ": not a valid zip: ") + ex.Message);
            }
            if (!kit.Files.ContainsKey("pkg.sh"))
            {
                throw new StepException(label + L.T(": manca nbt-evo-apps/pkg.sh", ": nbt-evo-apps/pkg.sh missing"));
            }
            string pkg = Encoding.UTF8.GetString(kit.Files["pkg.sh"]);
            if (!pkg.Contains("NEA_STICK"))
            {
                throw new StepException(label + L.T(": pkg.sh non accetta NEA_STICK (versione troppo vecchia): non lo uso.",
                    ": pkg.sh does not accept NEA_STICK (too old): not used."));
            }
            var result = new EvoKit { Kit = kit, Zip = zip };
            foreach (string pt in kit.Files.Keys.Where(k => Regex.IsMatch(k, @"^pacchetti/[A-Za-z0-9_]+/package\.txt$")).OrderBy(k => k, StringComparer.Ordinal).ToList())
            {
                string name = pt.Split('/')[1];
                result.Packages.Add(CheckPackage(kit, name, label));
            }
            if (result.Packages.Count == 0)
            {
                throw new StepException(label + L.T(": nessun pacchetto in nbt-evo-apps/pacchetti", ": no package in nbt-evo-apps/pacchetti"));
            }
            // wrapper run on omap: the stick folder is the copy in RAM, pkg.sh is the release's one, exit code reported
            string dir = HeadUnit.Stage + "/" + Folder;
            kit.Files[Wrapper] = Encoding.ASCII.GetBytes(
                "NEA_STICK=" + dir + "\nexport NEA_STICK\n/bin/sh " + dir + "/pkg.sh \"$@\"\necho \"NBTC-EXIT:$?\"\n");
            Log.Write("Kit " + label + ": " + kit.Files.Count + " file, pacchetti " + string.Join(", ", result.Packages.Select(p => p.Name + " " + p.Version))
                + " (files.sum e librerie QNX controllati)");
            return result;
        }

        private static EvoPackage CheckPackage(Kit kit, string name, string label)
        {
            string prefix = "pacchetti/" + name + "/";
            Dictionary<string, string> f = Fields(kit.Files[prefix + "package.txt"]);
            var p = new EvoPackage
            {
                Name = name,
                Title = f.GetValueOrDefault("title", name),
                Version = f.GetValueOrDefault("version", ""),
                Brands = f.GetValueOrDefault("brands", "all").Trim(),
                PhoneVariant = f.GetValueOrDefault("variant_from", "") == "phone",
            };
            p.Tested = TestedInCar.Contains(name) || f.GetValueOrDefault("status", "") == "tested";
            if (f.GetValueOrDefault("name", "") != name || p.Version.Length == 0)
            {
                throw new StepException(label + ": " + name + L.T(": package.txt senza nome o versione corretti", ": package.txt without the right name or version"));
            }
            if (f.GetValueOrDefault("dest", "") != "plugin")
            {
                throw new StepException(label + ": " + name + L.T(": destinazione non prevista: ", ": unexpected destination: ") + f.GetValueOrDefault("dest", ""));
            }
            if (!kit.Files.TryGetValue(prefix + "files.sum", out byte[]? sums))
            {
                throw new StepException(label + ": " + name + L.T(": manca files.sum", ": files.sum missing"));
            }
            int n = 0;
            foreach (string line in Encoding.UTF8.GetString(sums).Split('\n'))
            {
                string[] w = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (w.Length == 0)
                {
                    continue;
                }
                if (w.Length != 3 || !uint.TryParse(w[0], out uint crc) || !long.TryParse(w[1], out long size))
                {
                    throw new StepException(label + ": " + name + L.T(": riga di files.sum non valida: ", ": invalid files.sum line: ") + line.Trim());
                }
                if (!kit.Files.TryGetValue(prefix + w[2], out byte[]? data))
                {
                    throw new StepException(label + ": " + name + "/" + w[2] + L.T(" elencato in files.sum ma assente", " listed in files.sum but missing"));
                }
                if (data.LongLength != size || Cksum(data) != crc)
                {
                    throw new StepException(label + ": " + name + "/" + w[2] + L.T(" non corrisponde a files.sum", " does not match files.sum"));
                }
                if (w[2].EndsWith(".so", StringComparison.Ordinal))
                {
                    AadPackage.CheckQnxArmLibrary(data, name + "/" + w[2]);
                }
                n++;
            }
            int onDisk = kit.Files.Keys.Count(k => k.StartsWith(prefix, StringComparison.Ordinal)) - 2;
            if (n == 0 || n != onDisk)
            {
                throw new StepException(label + ": " + name + L.T(": files.sum non elenca tutti i file del pacchetto (" + n + " su " + onDisk + ")",
                    ": files.sum does not list every file of the package (" + n + " of " + onDisk + ")"));
            }
            p.FileCount = n;
            return p;
        }

        public static Dictionary<string, string> Fields(byte[] data)
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string raw in Encoding.UTF8.GetString(data).Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                int i = line.IndexOf('=');
                if (i > 0 && !line.StartsWith("#", StringComparison.Ordinal))
                {
                    d[line.Substring(0, i)] = line.Substring(i + 1);
                }
            }
            return d;
        }

        private static readonly uint[] CrcTable = BuildTable();

        private static uint[] BuildTable()
        {
            var t = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i << 24;
                for (int k = 0; k < 8; k++)
                {
                    c = (c & 0x80000000) != 0 ? (c << 1) ^ 0x04C11DB7 : c << 1;
                }
                t[i] = c;
            }
            return t;
        }

        /// <summary>The number of the POSIX cksum command (the one pkg.sh compares with files.sum).</summary>
        public static uint Cksum(byte[] data)
        {
            uint c = 0;
            foreach (byte b in data)
            {
                c = (c << 8) ^ CrcTable[(c >> 24) ^ b];
            }
            long n = data.LongLength;
            while (n != 0)
            {
                c = (c << 8) ^ CrcTable[(c >> 24) ^ (uint)(n & 0xFF)];
                n >>= 8;
            }
            return ~c;
        }

        /// <summary>pkg.sh car name from the brand found in the HMI: mini, bmw (bmw, bmwm, bmwi) or "" (choose by hand).</summary>
        public static string CarFromBrands(IEnumerable<string> brands)
        {
            var b = brands.ToList();
            if (b.Contains("mini") && !b.Any(x => x.StartsWith("bmw", StringComparison.Ordinal)))
            {
                return "mini";
            }
            if (b.Any(x => x.StartsWith("bmw", StringComparison.Ordinal)) && !b.Contains("mini"))
            {
                return "bmw";
            }
            return "";
        }

        /// <summary>Installed version of a package (from /opt/aa/nbt-evo-apps/installed/&lt;Name&gt;.txt), "" if not installed.</summary>
        public static string Installed(HeadUnit hu, string name)
        {
            byte[]? b = hu.Download(DataDir + "/installed/" + name + ".txt");
            return b == null ? "" : Fields(b).GetValueOrDefault("version", "?");
        }

        public static string SavedCar(HeadUnit hu)
        {
            byte[]? b = hu.Download(DataDir + "/brand");
            return b == null ? "" : Encoding.ASCII.GetString(b).Trim();
        }

        private static string RunPkg(HeadUnit hu, EvoKit k, string args, bool write)
        {
            TileKits.Upload(hu, k.Kit);
            if (write)
            {
                hu.RemountRw();
            }
            string o = TileKits.RunKitScript(hu, k.Kit, Wrapper, args, 300);
            Match m = Regex.Match(o, @"NBTC-EXIT:(\d+)");
            if (!m.Success)
            {
                throw new StepException(L.T("pkg.sh non ha finito (vedi log).", "pkg.sh did not finish (see the log)."));
            }
            if (m.Groups[1].Value != "0" || Regex.IsMatch(o, @"(^|\n)ERROR"))
            {
                throw new StepException(L.T("pkg.sh ha segnalato un errore (vedi log): ", "pkg.sh reported an error (see the log): ")
                    + (Regex.Match(o, @"ERROR[^\n]*").Value is string e && e.Length > 0 ? e : "exit " + m.Groups[1].Value));
            }
            return o;
        }

        private static void CheckNames(EvoKit k, IEnumerable<string> names)
        {
            foreach (string n in names)
            {
                if (!k.Packages.Any(p => p.Name == n))
                {
                    throw new StepException(n + L.T(": pacchetto non presente nella release", ": package not in the release"));
                }
            }
        }

        /// <summary>Car (and iDrive Plus phone app, only if a chosen package depends on it) written, then the packages installed.</summary>
        public static string Install(HeadUnit hu, EvoKit k, string car, string phone, IList<string> names)
        {
            Log.Section("nbt-evo-apps: installazione di " + string.Join(", ", names) + " (auto " + car + ")");
            CheckNames(k, names);
            if (car != "mini" && car != "bmw" && car != "supra")
            {
                throw new StepException(L.T("Scegli l'auto: MINI, BMW o Supra.", "Choose the car: MINI, BMW or Supra."));
            }
            foreach (string n in names)
            {
                EvoPackage p = k.Packages.First(x => x.Name == n);
                if (p.ForCar(car) == "no")
                {
                    throw new StepException(p.Title + L.T(" e' per ", " is for ") + p.Brands + L.T(", non per ", ", not for ") + car + ".");
                }
            }
            var sb = new StringBuilder();
            sb.Append(RunPkg(hu, k, "brand " + car, true));
            if (k.Packages.Where(p => names.Contains(p.Name)).Any(p => p.PhoneVariant))
            {
                sb.Append(RunPkg(hu, k, "phone " + phone, true));
            }
            sb.Append(RunPkg(hu, k, "install " + string.Join(" ", names), true));
            return sb.ToString();
        }

        public static string Remove(HeadUnit hu, EvoKit k, string name)
        {
            Log.Section("nbt-evo-apps: rimozione di " + name + " (spostata in /opt/aa/plugins_off)");
            CheckNames(k, new[] { name });
            return RunPkg(hu, k, "remove " + name, true);
        }

        public static string Restore(HeadUnit hu, EvoKit k, string name)
        {
            Log.Section("nbt-evo-apps: versione precedente di " + name);
            CheckNames(k, new[] { name });
            return RunPkg(hu, k, "restore " + name, true);
        }

        /// <summary>The release zip, unchanged, for a USB stick (Documents\NBT-Companion\nbt-evo-apps).</summary>
        public static string SaveZip(EvoKit k)
        {
            string dir = Path.Combine(AppPaths.Diagnostics, "nbt-evo-apps");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, k.AssetName.Length > 0 ? k.AssetName : "nbt-evo-apps.zip");
            File.WriteAllBytes(path, k.Zip);
            Log.Write("Zip della chiavetta salvato: " + path + " (" + k.Zip.Length + " byte, sha256 " + Hash.Sha256(k.Zip) + ")");
            return path;
        }
    }
}
