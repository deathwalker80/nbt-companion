using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace NbtCompanion.Core
{
    /// <summary>
    /// iDrive Plus / NBT Plus (deathwalker80/idrive-plus): the phone app (APK downloaded to the PC: the generic NBT Plus
    /// of the nbt-v* releases or MINI / BMW / Supra Plus of the v* releases) and the iDrive plugins of the USB_vNN.zip
    /// (Weather, News, Local news, Fuel, Traffic, Vehicle status), installed with the stick's own 00-installa.sh.
    /// </summary>
    public static class NbtPlus
    {
        public const string Repo = "deathwalker80/idrive-plus";
        public const string KitFolder = "idrive-plus";
        public static readonly string[] Plugins = { "Meteo", "Notizie", "Locali", "Carburanti", "Traffico", "Auto" };

        public sealed class AppChoice
        {
            public string Key { get; set; } = "";
            public string ApkPrefix { get; set; } = "";
            public string LabelIt { get; set; } = "";
            public string LabelEn { get; set; } = "";

            public override string ToString()
            {
                return L.T(LabelIt, LabelEn);
            }
        }

        /// <summary>Key = argument of 00-installa.sh (the "waiting for the phone" pages follow the app on the phone).</summary>
        public static readonly AppChoice[] Apps =
        {
            new AppChoice { Key = "nbt", ApkPrefix = "NBTPlus", LabelIt = "NBT Plus (generica, Play Store)", LabelEn = "NBT Plus (generic, Play Store)" },
            new AppChoice { Key = "mini", ApkPrefix = "MINIPlus", LabelIt = "MINI Plus", LabelEn = "MINI Plus" },
            new AppChoice { Key = "bmw", ApkPrefix = "BMWPlus", LabelIt = "BMW Plus", LabelEn = "BMW Plus" },
            new AppChoice { Key = "supra", ApkPrefix = "SupraPlus", LabelIt = "Supra Plus", LabelEn = "Supra Plus" },
        };

        public sealed class Found
        {
            public GhRelease Release { get; set; } = new GhRelease();
            public GhAsset Asset { get; set; } = new GhAsset();
        }

        /// <summary>Newest release (no pre-release) with the APK of that app: nbt-v* for NBT Plus, v* for the others.</summary>
        public static Found? Apk(IEnumerable<GhRelease> releases, AppChoice app)
        {
            var name = new Regex("^" + Regex.Escape(app.ApkPrefix) + @"-[0-9.]+-release\.apk$");
            foreach (GhRelease r in releases.Where(x => !x.Prerelease).OrderByDescending(x => x.Published))
            {
                bool family = app.Key == "nbt" ? r.Tag.StartsWith("nbt-v", StringComparison.Ordinal) : Regex.IsMatch(r.Tag, @"^v\d");
                GhAsset? a = family ? r.Assets.FirstOrDefault(x => name.IsMatch(x.Name)) : null;
                if (a != null)
                {
                    return new Found { Release = r, Asset = a };
                }
            }
            return null;
        }

        /// <summary>Newest release (no pre-release) with the USB stick zip USB_vNN.zip.</summary>
        public static Found? Usb(IEnumerable<GhRelease> releases)
        {
            var name = new Regex(@"^USB_v\d+\.zip$");
            foreach (GhRelease r in releases.Where(x => !x.Prerelease).OrderByDescending(x => x.Published))
            {
                GhAsset? a = r.Assets.FirstOrDefault(x => name.IsMatch(x.Name));
                if (a != null)
                {
                    return new Found { Release = r, Asset = a };
                }
            }
            return null;
        }

        /// <summary>APK checked with GitHub's SHA-256, saved in Documents\NBT-Companion\apk. Returns its path.</summary>
        public static string DownloadApk(Found f)
        {
            byte[] data = GitHub.DownloadAsset(f.Release, f.Asset, false);
            if (data.Length < 4 || data[0] != (byte)'P' || data[1] != (byte)'K')
            {
                throw new StepException(f.Asset.Name + L.T(": non e' un APK (zip) valido.", ": not a valid APK (zip)."));
            }
            string dir = Path.Combine(AppPaths.Diagnostics, "apk");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, f.Asset.Name);
            File.WriteAllBytes(path, data);
            Log.Write("APK salvato: " + path + " (SHA-256 " + f.Asset.Sha256 + ")");
            return path;
        }

        // The stick script looks for the stick; here the kit is in omap's RAM. Exactly this line is replaced, and every
        // "/mnt/$U" becomes the RAM folder, in the copy sent to the head unit only: anything else and the kit is refused.
        private const string StickLoop = "for d in /mnt/umass*; do if [ -d \"$d\" ]; then U=${d#/mnt/}; break; fi; done";

        public static Kit KitFromZip(byte[] zip, string label)
        {
            var kit = new Kit { Folder = KitFolder, ReleaseName = label };
            using (var z = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read))
            {
                foreach (ZipArchiveEntry e in z.Entries)
                {
                    if (e.FullName.EndsWith("/"))
                    {
                        continue;
                    }
                    using var ms = new MemoryStream();
                    using (Stream s = e.Open())
                    {
                        s.CopyTo(ms);
                    }
                    kit.Files[e.FullName] = ms.ToArray();
                }
            }
            foreach (string f in new[] { "00-installa.sh", "10-togli.sh" })
            {
                if (!kit.Files.ContainsKey(f))
                {
                    throw new StepException(label + L.T(": manca ", ": missing ") + f);
                }
            }
            foreach (string n in Plugins)
            {
                string lib = n + "/lib" + n + ".so";
                if (!kit.Files.TryGetValue(lib, out byte[]? so))
                {
                    throw new StepException(label + L.T(": manca ", ": missing ") + lib);
                }
                AadPackage.CheckQnxArmLibrary(so, lib);
                if (!kit.Files.ContainsKey(n + "/attesa.png"))
                {
                    throw new StepException(label + L.T(": manca ", ": missing ") + n + "/attesa.png");
                }
            }
            string script = Encoding.UTF8.GetString(kit.Files["00-installa.sh"]);
            if (Regex.Matches(script, Regex.Escape(StickLoop)).Count != 1)
            {
                throw new StepException(label + L.T(": 00-installa.sh e' diverso da quello previsto (ricerca della chiavetta): non lo uso.",
                    ": 00-installa.sh differs from the expected one (USB stick lookup): not used."));
            }
            string ram = HeadUnit.Stage + "/" + KitFolder;
            script = script.Replace(StickLoop, "U=ram").Replace("/mnt/$U", ram);
            if (script.Split('\n').Any(l => !l.TrimStart().StartsWith("#", StringComparison.Ordinal) && l.Contains("/mnt/")))
            {
                throw new StepException(label + L.T(": 00-installa.sh usa ancora percorsi della chiavetta: non lo uso.",
                    ": 00-installa.sh still uses USB stick paths: not used."));
            }
            kit.Files["00-installa.sh"] = Encoding.UTF8.GetBytes(script);
            Log.Write("Kit " + label + ": " + kit.Files.Count + " file, 6 librerie QNX ARM controllate");
            return kit;
        }

        public static Kit FetchKit(IEnumerable<GhRelease> releases)
        {
            Found? usb = Usb(releases) ?? throw new StepException(L.T("Nessuna release di " + Repo + " ha la chiavetta USB_vNN.zip.",
                "No release of " + Repo + " has the USB_vNN.zip stick."));
            byte[] zip = GitHub.DownloadAsset(usb.Release, usb.Asset, false);
            return KitFromZip(zip, usb.Release.Name + " / " + usb.Asset.Name);
        }

        public static string Install(HeadUnit hu, Kit kit, AppChoice app)
        {
            Log.Section("Plugin iDrive Plus / NBT Plus: installazione (" + app.Key + ")");
            TileKits.Upload(hu, kit);
            hu.RemountRw();
            string o = TileKits.RunKitScript(hu, kit, "00-installa.sh", app.Key, 300);
            if (!o.Contains("DONE - NOW REBOOT"))
            {
                throw new StepException(L.T("Installazione dei plugin non completata (vedi log).", "Plugin install not completed (see the log)."));
            }
            return o;
        }

        public static string Remove(HeadUnit hu, Kit kit)
        {
            Log.Section("Plugin iDrive Plus / NBT Plus: rimozione");
            TileKits.Upload(hu, kit);
            hu.RemountRw();
            string o = TileKits.RunKitScript(hu, kit, "10-togli.sh", "", 300);
            if (!o.Contains("REBOOT"))
            {
                throw new StepException(L.T("Rimozione dei plugin non completata (vedi log).", "Plugin removal not completed (see the log)."));
            }
            return o;
        }

        public static string Status(HeadUnit hu)
        {
            Log.Section("Plugin iDrive Plus / NBT Plus: stato");
            var sb = new StringBuilder();
            sb.Append(hu.OmapShell("ls -l " + HeadUnit.HmiRo + "/Plugins/", 30).All);
            sb.Append("\n");
            sb.Append(hu.OmapShell("ls -l /opt/aa/plugins_off/ /opt/aa/ponte.disable /opt/aa/auto.disable", 30).All);
            return sb.ToString();
        }
    }
}
