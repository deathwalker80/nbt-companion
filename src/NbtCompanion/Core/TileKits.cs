using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace NbtCompanion.Core
{
    /// <summary>A kit folder from a release zip of deathwalker80/nbt-aa-tile, checked with GitHub's digest and its SHA256SUMS.</summary>
    public sealed class Kit
    {
        public string Folder { get; set; } = "";
        public string ReleaseName { get; set; } = "";
        public Dictionary<string, byte[]> Files { get; } = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        public string OmapDir
        {
            get { return HeadUnit.Stage + "/" + Folder; }
        }
    }

    public sealed class PngInfo
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public int ColorType { get; set; }
    }

    /// <summary>nbt-aa-tile (Android Auto tile in the home screen) and its extra aa-menu-look (CarLife labels/images).</summary>
    public static class TileKits
    {
        public const string Repo = "deathwalker80/nbt-aa-tile";
        public static readonly string[] Brands = { "mini", "bmw", "bmwm", "bmwi" };

        // The kit scripts look for the folder on a USB stick; here the folder is in omap's RAM.
        // Exactly this line is replaced, in the copy sent to the head unit only: if it is not there the kit is refused.
        private const string StickLoopTile = "for d in /mnt/umass*; do if [ -d \"$d/nbt-aa-tile\" ]; then U=$d; fi; done";
        private const string StickLoopLook = "for d in /mnt/umass*; do if [ -d \"$d/aa-menu-look\" ]; then U=$d; fi; done";

        public static Kit FetchTile()
        {
            byte[] zip = FetchZip(new Regex(@"^nbt-aa-tile-\d+\.zip$"), "nbt-aa-tile", out string label);
            return TileFromZip(zip, label);
        }

        public static Kit FetchLook()
        {
            byte[] zip = FetchZip(new Regex(@"^aa-menu-look-\d+\.zip$"), "aa-menu-look", out string label);
            return LookFromZip(zip, label);
        }

        /// <summary>Newest non-pre-release of the repository with that zip; checked with GitHub's SHA-256.</summary>
        private static byte[] FetchZip(Regex assetName, string folder, out string label)
        {
            foreach (GhRelease rel in GitHub.Releases(Repo).Where(r => !r.Prerelease))
            {
                GhAsset? a = rel.Assets.FirstOrDefault(x => assetName.IsMatch(x.Name));
                if (a == null)
                {
                    continue;
                }
                label = rel.Name + " / " + a.Name;
                return GitHub.DownloadAsset(rel, a, false);
            }
            throw new StepException(L.T("Nessuna release di " + Repo + " contiene il kit ", "No release of " + Repo + " has the kit ") + folder);
        }

        public static Kit TileFromZip(byte[] zip, string label)
        {
            return FromZip(zip, label, "nbt-aa-tile",
                new[] { "aa-tile", "aa-tile.size", "aa-tile-launcher.sh", "hook.txt", "install-boot.sh", "remove-boot.sh", "status.sh", "test-ram.sh" },
                new[] { "install-boot.sh", "test-ram.sh" }, StickLoopTile);
        }

        public static Kit LookFromZip(byte[] zip, string label)
        {
            return FromZip(zip, label, "aa-menu-look",
                new[] { "aa-look", "aa-look.size", "install.sh", "restore.sh", "list-images.sh" },
                new[] { "install.sh", "list-images.sh" }, StickLoopLook);
        }

        private static Kit FromZip(byte[] zip, string label, string folder, string[] required, string[] patch, string stickLoop)
        {
            var kit = new Kit { Folder = folder, ReleaseName = label };
            using (var z = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read))
            {
                foreach (ZipArchiveEntry e in z.Entries)
                {
                    if (e.FullName.EndsWith("/") || !e.FullName.StartsWith(folder + "/", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    using var ms = new MemoryStream();
                    using (Stream s = e.Open())
                    {
                        s.CopyTo(ms);
                    }
                    kit.Files[e.FullName.Substring(folder.Length + 1)] = ms.ToArray();
                }
            }
            foreach (string f in required)
            {
                if (!kit.Files.ContainsKey(f))
                {
                    throw new StepException(label + L.T(": manca ", ": missing ") + f);
                }
            }
            CheckSums(kit, label);
            foreach (string f in patch)
            {
                string text = Encoding.UTF8.GetString(kit.Files[f]);
                int count = Regex.Matches(text, Regex.Escape(stickLoop)).Count;
                if (count != 1)
                {
                    throw new StepException(label + ": " + f + L.T(" e' diverso da quello previsto (ricerca della chiavetta): non lo uso.",
                        " differs from the expected one (USB stick lookup): not used."));
                }
                kit.Files[f] = Encoding.UTF8.GetBytes(text.Replace(stickLoop, "U=" + HeadUnit.Stage));
            }
            Log.Write("Kit " + kit.ReleaseName + ": " + kit.Files.Count + " file, SHA256SUMS verificato");
            return kit;
        }

        private static void CheckSums(Kit kit, string zipName)
        {
            if (!kit.Files.TryGetValue("SHA256SUMS", out byte[]? sums))
            {
                throw new StepException(zipName + L.T(": manca SHA256SUMS", ": SHA256SUMS missing"));
            }
            int n = 0;
            foreach (string line in Encoding.UTF8.GetString(sums).Split('\n'))
            {
                Match m = Regex.Match(line.Trim(), @"^([0-9a-fA-F]{64})\s+\*?(.+)$");
                if (!m.Success)
                {
                    continue;
                }
                string name = m.Groups[2].Value.Trim();
                if (!kit.Files.TryGetValue(name, out byte[]? data))
                {
                    throw new StepException(zipName + ": " + name + L.T(" elencato in SHA256SUMS ma assente", " listed in SHA256SUMS but missing"));
                }
                if (Hash.Sha256(data) != m.Groups[1].Value.ToLowerInvariant())
                {
                    throw new StepException(zipName + ": " + name + L.T(" non corrisponde a SHA256SUMS", " does not match SHA256SUMS"));
                }
                n++;
            }
            if (n == 0)
            {
                throw new StepException(zipName + L.T(": SHA256SUMS vuoto", ": SHA256SUMS empty"));
            }
        }

        /// <summary>Copies the kit into omap's RAM (each file read back and compared). Extra files can be added (images).</summary>
        public static void Upload(HeadUnit hu, Kit kit, Dictionary<string, byte[]>? extra = null)
        {
            var all = new Dictionary<string, byte[]>(kit.Files);
            if (extra != null)
            {
                foreach (KeyValuePair<string, byte[]> kv in extra)
                {
                    all[kv.Key] = kv.Value;
                }
            }
            var dirs = new SortedSet<string>(StringComparer.Ordinal) { kit.OmapDir };
            foreach (string rel in all.Keys)
            {
                int i = rel.LastIndexOf('/');
                if (i > 0)
                {
                    dirs.Add(kit.OmapDir + "/" + rel.Substring(0, i));
                }
            }
            hu.OmapShell("rm -rf " + kit.OmapDir + " && mkdir -p " + string.Join(" ", dirs), 60);
            foreach (KeyValuePair<string, byte[]> kv in all.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                if (kv.Key.Contains(' ') || kv.Key.Contains('\''))
                {
                    throw new StepException(L.T("Nome di file non ammesso: ", "File name not allowed: ") + kv.Key);
                }
                hu.Upload(kv.Value, kit.OmapDir + "/" + kv.Key);
            }
        }

        public static string RunKitScript(HeadUnit hu, Kit kit, string script, string args, int timeoutSec)
        {
            CmdResult r = hu.Run("on -f " + HeadUnit.Omap + " /bin/sh " + kit.OmapDir + "/" + script + (args.Length > 0 ? " " + args : "") + " 2>&1", timeoutSec);
            return r.Text;
        }

        // ------------------------------------------------------------------ nbt-aa-tile

        public static string TileStatus(HeadUnit hu, Kit kit)
        {
            Upload(hu, kit);
            return RunKitScript(hu, kit, "status.sh", "", 60);
        }

        /// <summary>
        /// Same check as nbt-aa-tile main (commit 55b5fe7, 13/11, not yet in a release): the tile replaces the CarLife
        /// entry, so a software without common/widget/CarLife.pbw (e.g. BMW F30 NBTevo_L17193I) cannot show it.
        /// </summary>
        public static void RequireCarLifeEntry(HeadUnit hu)
        {
            CmdResult r = hu.OmapShell("test -f " + HeadUnit.HmiRo + "/common/widget/CarLife.pbw && echo YES", 20);
            if (!r.Text.Contains("YES"))
            {
                throw new StepException(L.T("Questo software della testata non ha la voce CarLife (common/widget/CarLife.pbw): la casella qui non puo' funzionare.",
                    "This head unit software has no CarLife entry (common/widget/CarLife.pbw): the tile cannot work here."));
            }
        }

        public static string TileTestRam(HeadUnit hu, Kit kit, string mode)
        {
            Log.Section("nbt-aa-tile: prova in RAM (" + mode + ", 120 s)");
            RequireCarLifeEntry(hu);
            Upload(hu, kit);
            return RunKitScript(hu, kit, "test-ram.sh", mode, 240);
        }

        public static string TileInstallBoot(HeadUnit hu, Kit kit, string mode)
        {
            Log.Section("nbt-aa-tile: installazione all'avvio (" + mode + ")");
            RequireCarLifeEntry(hu);
            Upload(hu, kit);
            hu.RemountRw();
            string o = RunKitScript(hu, kit, "install-boot.sh", mode, 120);
            if (!o.Contains("DONE.") && !o.Contains("OK: updated."))
            {
                throw new StepException(L.T("Installazione della casella non completata (vedi log).", "Tile install not completed (see the log)."));
            }
            return o;
        }

        public static string TileRemove(HeadUnit hu, Kit kit)
        {
            Log.Section("nbt-aa-tile: rimozione");
            Upload(hu, kit);
            hu.RemountRw();
            string o = RunKitScript(hu, kit, "remove-boot.sh", "", 120);
            if (!o.Contains("DONE."))
            {
                throw new StepException(L.T("Rimozione della casella non completata (vedi log).", "Tile removal not completed (see the log)."));
            }
            return o;
        }

        // ------------------------------------------------------------------ aa-menu-look

        public static List<string> DetectBrands(HeadUnit hu)
        {
            CmdResult r = hu.OmapShell("ls -d " + HeadUnit.HmiRo + "/*/db/text.db", 30);
            var found = new List<string>();
            foreach (string b in Brands)
            {
                if (r.Text.Contains(HeadUnit.HmiRo + "/" + b + "/db/text.db"))
                {
                    found.Add(b);
                }
            }
            Log.Write("Marche presenti nell'HMI: " + (found.Count > 0 ? string.Join(", ", found) : "nessuna riconosciuta"));
            return found;
        }

        public static PngInfo? ReadPng(byte[] b)
        {
            byte[] sig = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
            if (b.Length < 33 || !b.AsSpan(0, 8).SequenceEqual(sig) || Encoding.ASCII.GetString(b, 12, 4) != "IHDR")
            {
                return null;
            }
            int w = (b[16] << 24) | (b[17] << 16) | (b[18] << 8) | b[19];
            int h = (b[20] << 24) | (b[21] << 16) | (b[22] << 8) | b[23];
            return new PngInfo { Width = w, Height = h, ColorType = b[25] };
        }

        /// <summary>
        /// PNGs from a PC folder that mirrors images/ of the kit (a folder "images" inside it is also accepted):
        /// only &lt;brand&gt;/ and common/, only .png in lower case. Keys are "images/&lt;path&gt;".
        /// </summary>
        public static Dictionary<string, byte[]> CollectImages(string folder, string brand)
        {
            var map = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                return map;
            }
            string root = Directory.Exists(Path.Combine(folder, "images")) ? Path.Combine(folder, "images") : folder;
            foreach (string top in new[] { brand, "common" })
            {
                string d = Path.Combine(root, top);
                if (!Directory.Exists(d))
                {
                    continue;
                }
                foreach (string f in Directory.GetFiles(d, "*.png", SearchOption.AllDirectories))
                {
                    if (!f.EndsWith(".png", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    string rel = Path.GetRelativePath(root, f).Replace('\\', '/');
                    byte[] data = File.ReadAllBytes(f);
                    PngInfo? info = ReadPng(data);
                    if (info == null)
                    {
                        throw new StepException(rel + L.T(": non e' un PNG.", ": not a PNG."));
                    }
                    Log.Write("immagine " + rel + ": " + info.Width + " x " + info.Height + ", tipo colore " + info.ColorType);
                    map["images/" + rel] = data;
                }
            }
            return map;
        }

        public static string CheckLabel(string label)
        {
            string l = (label ?? "").Trim();
            if (l.Length < 1 || l.Length > 40 || !Regex.IsMatch(l, @"^[A-Za-z0-9 .,()+_-]+$"))
            {
                throw new StepException(L.T("Testo della voce non valido: 1-40 caratteri, solo lettere, cifre, spazi e . , ( ) + _ -",
                    "Invalid entry text: 1-40 characters, only letters, digits, spaces and . , ( ) + _ -"));
            }
            return l;
        }

        public static string LookList(HeadUnit hu, Kit kit, string brand)
        {
            Log.Section("aa-menu-look: immagini CarLife della testata (" + brand + ")");
            Upload(hu, kit);
            return RunKitScript(hu, kit, "list-images.sh", brand, 120);
        }

        public static string LookInstall(HeadUnit hu, Kit kit, string brand, string label, string imagesFolder)
        {
            Log.Section("aa-menu-look: testi e immagini (" + brand + ")");
            string l = CheckLabel(label);
            Dictionary<string, byte[]> images = CollectImages(imagesFolder, brand);
            Log.Write("Immagini da inviare: " + images.Count + (images.Count == 0 ? " (cambiano solo i testi)" : ""));
            Upload(hu, kit, images);
            hu.RemountRw();
            string o = RunKitScript(hu, kit, "install.sh", brand + " \"" + l + "\"", 600);
            if (!o.Contains("DONE.") && !o.Contains("Nothing to do."))
            {
                throw new StepException(L.T("aa-menu-look non completato (vedi log).", "aa-menu-look not completed (see the log)."));
            }
            return o;
        }

        public static string LookRestore(HeadUnit hu, Kit kit)
        {
            Log.Section("aa-menu-look: ripristino dei file originali");
            hu.RemountRw();
            CmdResult has = hu.OmapShell("test -f /opt/aa/menu-look-backup/restore.sh && echo YES", 20, null, true);
            string o;
            if (has.Text.Contains("YES"))
            {
                o = hu.Run("on -f " + HeadUnit.Omap + " /bin/sh /opt/aa/menu-look-backup/restore.sh 2>&1", 300).Text;
            }
            else
            {
                Upload(hu, kit);
                o = RunKitScript(hu, kit, "restore.sh", "", 300);
            }
            if (!o.Contains("DONE") && !o.Contains("Nothing to restore"))
            {
                throw new StepException(L.T("Ripristino non completato (vedi log).", "Restore not completed (see the log)."));
            }
            return o;
        }
    }
}
