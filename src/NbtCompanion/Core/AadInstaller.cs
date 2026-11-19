using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace NbtCompanion.Core
{
    public sealed class InstalledAad
    {
        public bool DaemonPresent { get; set; }
        public string DaemonSha { get; set; } = "";
        public string Build { get; set; } = "";
        public string ReleaseName { get; set; } = "";
        public bool PluginPresent { get; set; }
        public List<AadPackage.MenuEntry> Menu { get; set; } = new List<AadPackage.MenuEntry>();
        public bool OldDaemon { get; set; }
        public bool OldPlugin { get; set; }
        public string OldBuild { get; set; } = "";
        public bool EnvPresent { get; set; }

        /// <summary>Installed as an NBTApps package (executable nbt-aad in the package app dir, NBT-AAD #216).</summary>
        public bool Package { get; set; }

        /// <summary>Package install and also a hand install in /opt/aa: two nbt-aad on the same unit.</summary>
        public bool HandToo { get; set; }

        public string Summary()
        {
            if (!DaemonPresent)
            {
                return L.T("NBT-AAD non installato", "NBT-AAD not installed");
            }
            string v = Build.Length > 0 ? Build : L.T("versione non leggibile", "version not readable");
            if (ReleaseName.Length > 0)
            {
                v += " (" + ReleaseName + ")";
            }
            if (Package)
            {
                v += L.T(" - pacchetto NBTApps", " - NBTApps package");
            }
            if (HandToo)
            {
                v += L.T(" + copia a mano in /opt/aa", " + hand copy in /opt/aa");
            }
            return v;
        }
    }

    public sealed class InstallOptions
    {
        public int KeepVersions { get; set; } = 2;
        public bool ColorIcons { get; set; } = true;
        public bool MenuInConnected { get; set; } = true;
        /// <summary>Colour icons chosen by the user on the PC when neither the head unit nor the PC copy has them.</summary>
        public string ColorIconsFile { get; set; } = "";
    }

    /// <summary>Install, update, restore of NBT-AAD on hu-omap. Same steps as Sq00ky's updating.md and 20-installa-aa.sh v3.</summary>
    public static class AadInstaller
    {
        private static string NewDir { get { return HeadUnit.Stage + "/new"; } }

        /// <summary>
        /// NBT-AAD #216 (in the releases from v1.9.84): the same build runs as a hand install (/opt/aa, data /var/opt/aa)
        /// or as an NBTApps package. The daemon decides from its own path, the plugin from an executable nbt-aad in
        /// the package app dir. Package install: settings file in the data dir, updates from the NBTApps catalogue.
        /// </summary>
        public const string PkgAppDir = "/var/opt/nbtapp/apps/com.github.sq00ky.android-auto";
        public const string PkgDataDir = "/var/opt/nbtapp/data/com.github.sq00ky.android-auto";
        public const string HandDataDir = "/var/opt/aa";

        /// <summary>True when NBT-AAD is installed as an NBTApps package (executable nbt-aad in its app dir).</summary>
        public static bool IsPackage(HeadUnit hu)
        {
            CmdResult r = hu.OmapShell("if [ -x " + PkgAppDir + "/nbt-aad ]; then echo NBTC-PKG; fi", 30, null, true);
            return r.Text.Contains("NBTC-PKG");
        }

        /// <summary>nbt-aad.env: conf dir /opt/aa for a hand install, the package data dir for an NBTApps install.</summary>
        public static string EnvPath(bool package)
        {
            return package ? PkgDataDir + "/nbt-aad.env" : HeadUnit.AaDir + "/nbt-aad.env";
        }

        private static void RefusePackage(HeadUnit hu)
        {
            if (IsPackage(hu))
            {
                throw new StepException(L.T(
                    "NBT-AAD su questa testata e' installato come pacchetto NBTApps (" + PkgAppDir + "): si aggiorna o ripristina dal catalogo NBTApps. " +
                    "Un'installazione a mano in /opt/aa avrebbe due nbt-aad sulla stessa testata: non la faccio.",
                    "NBT-AAD on this head unit is installed as an NBTApps package (" + PkgAppDir + "): update or restore it from the NBTApps catalogue. " +
                    "A hand install in /opt/aa would put two nbt-aad on the same unit: not done."));
            }
        }

        public static InstalledAad ReadInstalled(HeadUnit hu, IEnumerable<GhRelease>? releases, Dictionary<string, string>? shaToRelease)
        {
            var s = new InstalledAad();
            hu.OmapShell("ls -l " + HeadUnit.AaDir + "/ " + HeadUnit.PluginDir + "/ " + PkgAppDir + "/", 30);
            CmdResult have = hu.OmapShell("for f in " + HeadUnit.AaDir + "/nbt-aad.old " + HeadUnit.AaDir + "/NbtAad.old " + HeadUnit.AaDir
                + "/nbt-aad.env " + PkgDataDir + "/nbt-aad.env; do if [ -e $f ]; then echo HAVE $f; fi; done; if [ -x " + PkgAppDir + "/nbt-aad ]; then echo HAVE-PKG; fi", 30);
            string text = have.Text;
            s.Package = text.Contains("HAVE-PKG");
            s.OldDaemon = text.Contains("HAVE " + HeadUnit.AaDir + "/nbt-aad.old");
            s.OldPlugin = text.Contains("HAVE " + HeadUnit.AaDir + "/NbtAad.old");
            s.EnvPresent = text.Contains("HAVE " + EnvPath(s.Package));
            byte[]? hand = hu.Download(HeadUnit.AaDir + "/nbt-aad");
            byte[]? daemon = hand;
            if (s.Package)
            {
                daemon = hu.Download(PkgAppDir + "/nbt-aad");
                s.HandToo = hand != null;
            }
            if (daemon != null)
            {
                s.DaemonPresent = true;
                s.DaemonSha = Hash.Sha256(daemon);
                s.Build = AadPackage.BuildOf(daemon);
                if (shaToRelease != null && shaToRelease.TryGetValue(s.DaemonSha, out string? rn))
                {
                    s.ReleaseName = rn;
                }
            }
            byte[]? xml = hu.Download(HeadUnit.PluginDir + "/NbtAad.xml");
            if (xml != null)
            {
                s.PluginPresent = true;
                s.Menu = AadPackage.MenuEntries(Encoding.UTF8.GetString(xml));
                File.WriteAllBytes(Path.Combine(AppPaths.PcBackup, "NbtAad.xml-" + DateTime.Now.ToString("yyyyMMdd-HHmmss")), xml);
            }
            if (s.OldDaemon)
            {
                byte[]? old = hu.Download(HeadUnit.AaDir + "/nbt-aad.old");
                if (old != null)
                {
                    s.OldBuild = AadPackage.BuildOf(old);
                }
            }
            if (s.HandToo)
            {
                Log.Write("ATTENZIONE: NBT-AAD e' installato sia come pacchetto NBTApps (" + PkgAppDir + ") sia a mano (/opt/aa): due nbt-aad sulla stessa testata");
            }
            Log.Write("Installato: " + s.Summary() + "; precedente conservata: " + (s.OldDaemon ? (s.OldBuild.Length > 0 ? s.OldBuild : "si") : "no")
                + "; menu: " + string.Join(", ", s.Menu.Select(m => m.Text + " -> " + AadPackage.MenuStateName(m.StateId))));
            return s;
        }

        /// <summary>Free kilobytes on /fs/sda0 of hu-omap from "df -k"; -1 if not readable.</summary>
        public static long FreeKb(HeadUnit hu)
        {
            CmdResult r = hu.OmapShell("df -k /fs/sda0", 30);
            foreach (string line in r.Text.Split('\n'))
            {
                if (!line.Contains("/fs/sda0"))
                {
                    continue;
                }
                string[] f = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                var nums = f.Where(x => Regex.IsMatch(x, @"^\d+$")).Select(x => long.Parse(x, CultureInfo.InvariantCulture)).ToList();
                if (nums.Count >= 3)
                {
                    return nums[2];
                }
            }
            return -1;
        }

        /// <summary>
        /// Picks the Images zip to install: the colour icons are kept at every update (user rule). Order: the zip of
        /// the plugin now on the head unit, unless it is the release's plain one; then the copy kept on this PC;
        /// then the file chosen by the user. Without any of them the update stops.
        /// </summary>
        public static byte[] ChooseImages(HeadUnit hu, AadPackage pkg, InstallOptions opt)
        {
            byte[] baseZip = pkg.Plugin["NbtAad_ALL_Images.zip"];
            if (!opt.ColorIcons)
            {
                Log.Write("Icone: quelle della release (scelta dell'utente)");
                return baseZip;
            }
            byte[]? onUnit = hu.Download(HeadUnit.PluginDir + "/NbtAad_ALL_Images.zip");
            byte[]? onPc = File.Exists(AppPaths.ColorIconsFile) ? File.ReadAllBytes(AppPaths.ColorIconsFile) : null;
            if (onUnit != null && !onUnit.SequenceEqual(baseZip))
            {
                if (onPc == null || !onPc.SequenceEqual(onUnit))
                {
                    File.WriteAllBytes(AppPaths.ColorIconsFile, onUnit);
                    Log.Write("Icone a colori della testata salvate sul PC: " + AppPaths.ColorIconsFile);
                }
                Log.Write("Icone: quelle a colori del plugin attuale (" + Hash.Sha256(onUnit) + ")");
                return onUnit;
            }
            if (onPc != null && !onPc.SequenceEqual(baseZip))
            {
                Log.Write("Icone: copia a colori conservata sul PC (" + Hash.Sha256(onPc) + ")");
                return onPc;
            }
            if (opt.ColorIconsFile.Length > 0 && File.Exists(opt.ColorIconsFile))
            {
                byte[] chosen = File.ReadAllBytes(opt.ColorIconsFile);
                using (var z = new System.IO.Compression.ZipArchive(new MemoryStream(chosen), System.IO.Compression.ZipArchiveMode.Read))
                {
                    if (z.Entries.Count == 0)
                    {
                        throw new StepException(L.T("Il file delle icone scelto e' uno zip vuoto.", "The chosen icon file is an empty zip."));
                    }
                }
                File.WriteAllBytes(AppPaths.ColorIconsFile, chosen);
                Log.Write("Icone: file scelto dall'utente " + opt.ColorIconsFile + " (" + Hash.Sha256(chosen) + "), salvato sul PC");
                return chosen;
            }
            throw new StepException(L.T(
                "Icone a colori non trovate (ne' sulla testata ne' sul PC). Scegli il file NbtAad_ALL_Images.zip a colori, oppure togli la spunta \"Icone a colori\".",
                "Colour icons not found (neither on the head unit nor on the PC). Choose the colour NbtAad_ALL_Images.zip, or untick \"Colour icons\"."));
        }

        public static void Install(HeadUnit hu, AadPackage pkg, InstallOptions opt)
        {
            Log.Section("Installazione NBT-AAD " + pkg.Tag + " (" + pkg.Build + ")");
            RefusePackage(hu);

            // 1. files exactly as they will be on the head unit
            var files = new Dictionary<string, byte[]>(pkg.Plugin);
            files["NbtAad_ALL_Images.zip"] = ChooseImages(hu, pkg, opt);
            if (opt.MenuInConnected)
            {
                files["NbtAad.xml"] = AadPackage.SetMenuState(pkg.Plugin["NbtAad.xml"], "50", out int changed);
                if (changed == 0)
                {
                    throw new StepException(L.T("NbtAad.xml: voci \"Android Auto\" non trovate, spostamento non possibile.",
                        "NbtAad.xml: \"Android Auto\" entries not found, cannot move them."));
                }
                Log.Write("Menu: " + changed + " voci portate in MINI Connected / ConnectedDrive (insertStateID 50)");
            }

            // 2. free space (the previous version stays, so the new one needs its own room)
            long need = (pkg.TotalBytes + files["NbtAad_ALL_Images.zip"].Length) / 1024 * 2 + 512;
            long free = FreeKb(hu);
            if (free >= 0)
            {
                Log.Write("Spazio libero su /fs/sda0: " + free + " KB, servono almeno " + need + " KB");
                if (free < need)
                {
                    throw new StepException(L.T("Spazio insufficiente su /fs/sda0: ", "Not enough space on /fs/sda0: ") + free + " KB < " + need + " KB");
                }
            }
            else
            {
                Log.Write("ATTENZIONE: spazio libero non leggibile con df -k: controllo saltato");
            }

            // 3. copy to omap RAM, each file read back and compared
            hu.OmapShell("rm -rf " + NewDir + " && mkdir -p " + NewDir + "/Releases", 30);
            hu.Upload(pkg.Daemon, NewDir + "/nbt-aad");
            foreach (string f in AadPackage.PluginFiles)
            {
                hu.Upload(files[f], NewDir + "/Releases/" + f);
            }

            // 4. the new binary must start on this head unit before anything is changed
            if (pkg.HasLicenseFlag)
            {
                CmdResult t = hu.OmapShell("cp /bin/ls " + HeadUnit.Stage + "/t-aad && cat " + NewDir + "/nbt-aad > " + HeadUnit.Stage
                    + "/t-aad && " + HeadUnit.Stage + "/t-aad --license", 40);
                hu.OmapShell("rm -f " + HeadUnit.Stage + "/t-aad", 20, null, true);
                if (t.TimedOut || !t.Text.Contains("PolyForm"))
                {
                    throw new StepException(L.T("Il nuovo nbt-aad non parte su questa testata (prova --license fallita): non installo nulla.",
                        "The new nbt-aad does not start on this head unit (--license test failed): nothing installed."));
                }
                Log.Write("Prova di avvio (--license): OK");
            }
            else
            {
                Log.Write("Prova di avvio saltata: questo nbt-aad non ha l'opzione --license");
            }

            // 5. swap on the head unit
            hu.RemountRw();
            CmdResult job = hu.RunOmapScript("install-aad.sh", InstallScript(pkg.Daemon.Length, files), "", 180);
            if (!job.Text.Contains("NBTC-RESULT: OK"))
            {
                throw new StepException(L.T("Installazione non completata: ", "Install not completed: ") + LastError(job));
            }

            // 6. what is now on the head unit, byte for byte
            VerifyInstalled(hu, pkg.Daemon, files);

            // 7. firewall rule (pf_aa built into nbt-aad)
            PfCheck(hu, pkg.HasPfFlags);

            // 8. how many versions stay
            if (opt.KeepVersions == 1)
            {
                CmdResult k = hu.RunOmapScript("keep-one.sh", KeepOneScript(), "", 60);
                if (!k.Text.Contains("NBTC-RESULT: OK"))
                {
                    throw new StepException(L.T("Rimozione della versione precedente non riuscita: ", "Removal of the previous version failed: ") + LastError(k));
                }
            }
            hu.OmapShell("rm -rf " + NewDir, 30, null, true);
            Log.Write("NBT-AAD " + pkg.Build + " installato. Riavvia la testata a mano (tasto power 30 s).");
        }

        private static void VerifyInstalled(HeadUnit hu, byte[] daemon, Dictionary<string, byte[]> files)
        {
            byte[]? d = hu.Download(HeadUnit.AaDir + "/nbt-aad");
            if (d == null || !d.SequenceEqual(daemon))
            {
                throw new StepException(L.T("Verifica fallita: /opt/aa/nbt-aad non e' quello scaricato. Usa \"Ripristina precedente\".",
                    "Check failed: /opt/aa/nbt-aad is not the downloaded one. Use \"Restore previous\"."));
            }
            foreach (string f in AadPackage.PluginFiles)
            {
                byte[]? b = hu.Download(HeadUnit.PluginDir + "/" + f);
                if (b == null || !b.SequenceEqual(files[f]))
                {
                    throw new StepException(L.T("Verifica fallita su ", "Check failed on ") + f + L.T(". Usa \"Ripristina precedente\".", ". Use \"Restore previous\"."));
                }
            }
            Log.Write("Verifica: i 6 file sulla testata sono identici a quelli preparati (SHA-256)");
        }

        public static void PfCheck(HeadUnit hu, bool hasPf)
        {
            if (!hasPf)
            {
                Log.Write("Regola firewall: questo nbt-aad non ha pf_aa integrato (--pf-check): controllo saltato");
                return;
            }
            int rc = PfRun(hu, "--pf-check");
            if (rc == 0)
            {
                Log.Write("Regola firewall pf_aa: presente e attiva");
                return;
            }
            if (rc == 9)
            {
                Log.Write("Regola firewall pf_aa: presente, si attiva al riavvio");
                return;
            }
            Log.Write("Regola firewall pf_aa assente (codice " + rc + "): la installo");
            int inst = PfRun(hu, "--pf-install");
            if (inst != 0)
            {
                throw new StepException(L.T("pf_aa --install non riuscito (codice ", "pf_aa --install failed (code ") + inst + ").");
            }
            Log.Write("Regola firewall pf_aa installata: si attiva al riavvio");
        }

        private static int PfRun(HeadUnit hu, string arg)
        {
            CmdResult r = hu.OmapShell(HeadUnit.AaDir + "/nbt-aad " + arg, 60);
            Match m = Regex.Match(r.Text, @"pf_aa-exit:\s*(\d+)");
            return m.Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : -1;
        }

        /// <summary>Swaps current and previous version (daemon and plugin). Running it again swaps back.</summary>
        public static void RestorePrevious(HeadUnit hu)
        {
            Log.Section("Ripristino della versione precedente di NBT-AAD");
            RefusePackage(hu);
            hu.RemountRw();
            CmdResult job = hu.RunOmapScript("swap-aad.sh", SwapScript(), "", 120);
            if (!job.Text.Contains("NBTC-RESULT: OK"))
            {
                throw new StepException(L.T("Ripristino non completato: ", "Restore not completed: ") + LastError(job));
            }
            Log.Write("Versione precedente rimessa (quella di prima e' ora la .old). Riavvia la testata a mano (tasto power 30 s).");
        }

        /// <summary>Moves the two entries of the installed plugin: "50" MINI Connected / ConnectedDrive, "20" Media/Radio.</summary>
        public static void ApplyMenu(HeadUnit hu, string state)
        {
            Log.Section("Voci NBT-AAD in " + AadPackage.MenuStateName(state));
            byte[]? xml = hu.Download(HeadUnit.PluginDir + "/NbtAad.xml");
            if (xml == null)
            {
                throw new StepException(L.T("Plugin NbtAad non installato.", "NbtAad plugin not installed."));
            }
            File.WriteAllBytes(Path.Combine(AppPaths.PcBackup, "NbtAad.xml-" + DateTime.Now.ToString("yyyyMMdd-HHmmss")), xml);
            byte[] edited = AadPackage.SetMenuState(xml, state, out int changed);
            if (changed == 0)
            {
                throw new StepException(L.T("Voci \"Android Auto\" non trovate in NbtAad.xml.", "\"Android Auto\" entries not found in NbtAad.xml."));
            }
            if (edited.SequenceEqual(xml))
            {
                Log.Write("Le voci sono gia' in " + AadPackage.MenuStateName(state) + ": niente da cambiare");
                return;
            }
            hu.OmapShell("mkdir -p " + HeadUnit.Stage, 20, null, true);
            hu.Upload(edited, HeadUnit.Stage + "/NbtAad.xml");
            hu.RemountRw();
            CmdResult job = hu.RunOmapScript("menu-aad.sh", ReplaceFileScript(HeadUnit.Stage + "/NbtAad.xml", HeadUnit.PluginDir + "/NbtAad.xml", edited.Length), "", 60);
            if (!job.Text.Contains("NBTC-RESULT: OK"))
            {
                throw new StepException(L.T("Modifica non completata: ", "Change not completed: ") + LastError(job));
            }
            byte[]? back = hu.Download(HeadUnit.PluginDir + "/NbtAad.xml");
            if (back == null || !back.SequenceEqual(edited))
            {
                throw new StepException(L.T("Verifica di NbtAad.xml fallita.", "NbtAad.xml check failed."));
            }
            Log.Write(changed + " voci in " + AadPackage.MenuStateName(state) + ". Riavvia la testata a mano (tasto power 30 s).");
        }

        public static string? ReadEnv(HeadUnit hu)
        {
            string path = EnvPath(IsPackage(hu));
            Log.Write("nbt-aad.env di questa testata: " + path);
            byte[]? b = hu.Download(path);
            if (b == null)
            {
                return null;
            }
            File.WriteAllBytes(Path.Combine(AppPaths.PcBackup, "nbt-aad.env-" + DateTime.Now.ToString("yyyyMMdd-HHmmss")), b);
            return Encoding.UTF8.GetString(b);
        }

        public static void WriteEnv(HeadUnit hu, string text)
        {
            bool package = IsPackage(hu);
            string path = EnvPath(package);
            Log.Section("Scrittura di " + path);
            byte[] data = new UTF8Encoding(false).GetBytes(text.Replace("\r\n", "\n"));
            byte[]? before = hu.Download(path);
            if (before != null)
            {
                File.WriteAllBytes(Path.Combine(AppPaths.PcBackup, "nbt-aad.env-" + DateTime.Now.ToString("yyyyMMdd-HHmmss")), before);
            }
            hu.OmapShell("mkdir -p " + HeadUnit.Stage, 20, null, true);
            hu.Upload(data, HeadUnit.Stage + "/nbt-aad.env");
            if (package)
            {
                CmdResult dir = hu.OmapShell("if [ -d " + PkgDataDir + " ]; then echo NBTC-DIR; fi", 30, null, true);
                if (!dir.Text.Contains("NBTC-DIR"))
                {
                    throw new StepException(L.T("Cartella dati del pacchetto NBTApps assente: ", "NBTApps package data folder missing: ") + PkgDataDir);
                }
            }
            else
            {
                hu.RemountRw();
            }
            CmdResult job = hu.RunOmapScript("env-aad.sh", ReplaceFileScript(HeadUnit.Stage + "/nbt-aad.env", path, data.Length), "", 60);
            if (!job.Text.Contains("NBTC-RESULT: OK"))
            {
                throw new StepException(L.T("Scrittura non completata: ", "Write not completed: ") + LastError(job));
            }
            byte[]? back = hu.Download(path);
            if (back == null || !back.SequenceEqual(data))
            {
                throw new StepException(L.T("Verifica di nbt-aad.env fallita.", "nbt-aad.env check failed."));
            }
            Log.Write("nbt-aad.env scritto e verificato. Vale dal prossimo avvio di Android Auto.");
        }

        public static string LastError(CmdResult r)
        {
            string[] lines = r.Text.Split('\n');
            string? err = lines.LastOrDefault(l => l.Contains("NBTC-ERROR"));
            if (err != null)
            {
                return err.Replace("NBTC-ERROR:", "").Trim();
            }
            return r.TimedOut ? L.T("nessuna risposta dalla testata", "no answer from the head unit") : r.All.Trim();
        }

        // ------------------------------------------------------------------ scripts run by omap's /bin/sh
        // Only commands already used on this head unit: cp mv rm mkdir chmod cat wc grep pidin kill sleep ls.
        // Not available there: printf tr strings touch cmp head env which basename.

        private const string Common =
            "exec 2>&1\n" +
            "A=" + HeadUnit.AaDir + "\n" +
            "P=" + HeadUnit.PluginDir + "\n" +
            "size() { Z=$(wc -c < \"$1\" 2>/dev/null); echo $Z; }\n" +
            "fail() { echo \"NBTC-ERROR: $1\"; exit 1; }\n" +
            "if [ ! -d /opt/hmi ]; then fail \"not running on hu-omap\"; fi\n" +
            "stop_aad() {\n" +
            "  pidin | grep nbt-aad | while read p r; do kill $p 2>/dev/null; done\n" +
            "  n=0\n" +
            "  while pidin | grep nbt-aad >/dev/null; do\n" +
            "    n=$((n+1))\n" +
            "    if [ $n -gt 15 ]; then fail \"nbt-aad still running after 15 s\"; fi\n" +
            "    sleep 1\n" +
            "  done\n" +
            "  echo \"OK: nbt-aad not running\"\n" +
            "}\n";

        public static string InstallScript(int daemonSize, Dictionary<string, byte[]> files)
        {
            var sb = new StringBuilder();
            sb.Append("#!/bin/sh\n# NBT Companion - install nbt-aad (hu-omap)\n");
            sb.Append(Common);
            sb.Append("K=" + NewDir + "\n");
            sb.Append("check() {\n");
            sb.Append("  if [ ! -f \"$1\" ]; then fail \"missing $1\"; fi\n");
            sb.Append("  if [ \"$(size \"$1\")\" != \"$2\" ]; then fail \"$1 is $(size \"$1\") bytes, expected $2\"; fi\n");
            sb.Append("}\n");
            sb.Append("check $K/nbt-aad " + daemonSize + "\n");
            foreach (string f in AadPackage.PluginFiles)
            {
                sb.Append("check $K/Releases/" + f + " " + files[f].Length + "\n");
            }
            sb.Append("echo \"OK: new files ready\"\n");
            sb.Append("if [ ! -d $A ]; then mkdir /fs/sda0/opt/aa 2>/dev/null; fi\n");
            sb.Append("if [ ! -d $A ]; then fail \"cannot create $A\"; fi\n");
            sb.Append("stop_aad\n");
            sb.Append("OLDBIN=0\nOLDPLG=0\n");
            sb.Append("if [ -f $A/nbt-aad ]; then\n");
            sb.Append("  rm -f $A/nbt-aad.old\n");
            sb.Append("  mv $A/nbt-aad $A/nbt-aad.old || fail \"cannot rename $A/nbt-aad\"\n");
            sb.Append("  OLDBIN=1\n  echo \"OK: nbt-aad -> nbt-aad.old\"\n");
            sb.Append("fi\n");
            sb.Append("if [ -d $P ]; then\n");
            sb.Append("  rm -rf $A/NbtAad.old\n");
            sb.Append("  if ! mv $P $A/NbtAad.old; then\n");
            sb.Append("    if [ $OLDBIN = 1 ]; then mv $A/nbt-aad.old $A/nbt-aad; fi\n");
            sb.Append("    fail \"cannot move $P (nothing changed)\"\n");
            sb.Append("  fi\n");
            sb.Append("  OLDPLG=1\n  echo \"OK: plugin -> $A/NbtAad.old\"\n");
            sb.Append("fi\n");
            sb.Append("undo() {\n");
            sb.Append("  rm -f $A/nbt-aad\n  rm -rf $P\n");
            sb.Append("  if [ $OLDBIN = 1 ]; then mv $A/nbt-aad.old $A/nbt-aad; fi\n");
            sb.Append("  if [ $OLDPLG = 1 ]; then mv $A/NbtAad.old $P; fi\n");
            sb.Append("  fail \"$1 (previous version put back)\"\n");
            sb.Append("}\n");
            sb.Append("cp $K/nbt-aad $A/nbt-aad || undo \"copy of nbt-aad failed\"\n");
            sb.Append("chmod 755 $A/nbt-aad || undo \"chmod of nbt-aad failed\"\n");
            sb.Append("mkdir -p $P || undo \"cannot create $P\"\n");
            sb.Append("for f in " + string.Join(" ", AadPackage.PluginFiles) + "; do\n");
            sb.Append("  cp $K/Releases/$f $P/$f || undo \"copy of $f failed\"\n");
            sb.Append("done\n");
            sb.Append("if [ \"$(size $A/nbt-aad)\" != \"" + daemonSize + "\" ]; then undo \"wrong size of $A/nbt-aad\"; fi\n");
            foreach (string f in AadPackage.PluginFiles)
            {
                sb.Append("if [ \"$(size $P/" + f + ")\" != \"" + files[f].Length + "\" ]; then undo \"wrong size of " + f + "\"; fi\n");
            }
            sb.Append("ls -l $A/ $P/\n");
            sb.Append("echo \"NBTC-RESULT: OK\"\n");
            return sb.ToString();
        }

        public static string SwapScript()
        {
            var sb = new StringBuilder();
            sb.Append("#!/bin/sh\n# NBT Companion - swap current and previous nbt-aad (hu-omap)\n");
            sb.Append(Common);
            sb.Append("if [ ! -f $A/nbt-aad.old ]; then fail \"no previous nbt-aad ($A/nbt-aad.old)\"; fi\n");
            sb.Append("if [ ! -d $A/NbtAad.old ]; then fail \"no previous plugin ($A/NbtAad.old)\"; fi\n");
            sb.Append("if [ ! -f $A/nbt-aad ]; then fail \"no current nbt-aad\"; fi\n");
            sb.Append("if [ ! -d $P ]; then fail \"no current plugin\"; fi\n");
            sb.Append("rm -f $A/nbt-aad.swap\nrm -rf $A/NbtAad.swap\n");
            sb.Append("stop_aad\n");
            sb.Append("mv $A/nbt-aad $A/nbt-aad.swap || fail \"cannot rename nbt-aad\"\n");
            sb.Append("if ! mv $A/nbt-aad.old $A/nbt-aad; then mv $A/nbt-aad.swap $A/nbt-aad; fail \"cannot move nbt-aad.old\"; fi\n");
            sb.Append("mv $A/nbt-aad.swap $A/nbt-aad.old || fail \"cannot rename nbt-aad.swap (daemon already swapped)\"\n");
            sb.Append("echo \"OK: daemon swapped\"\n");
            sb.Append("if ! mv $P $A/NbtAad.swap; then\n");
            sb.Append("  mv $A/nbt-aad $A/nbt-aad.swap; mv $A/nbt-aad.old $A/nbt-aad; mv $A/nbt-aad.swap $A/nbt-aad.old\n");
            sb.Append("  fail \"cannot move the plugin (daemon put back)\"\n");
            sb.Append("fi\n");
            sb.Append("if ! mv $A/NbtAad.old $P; then\n");
            sb.Append("  mv $A/NbtAad.swap $P\n");
            sb.Append("  mv $A/nbt-aad $A/nbt-aad.swap; mv $A/nbt-aad.old $A/nbt-aad; mv $A/nbt-aad.swap $A/nbt-aad.old\n");
            sb.Append("  fail \"cannot move NbtAad.old (everything put back)\"\n");
            sb.Append("fi\n");
            sb.Append("mv $A/NbtAad.swap $A/NbtAad.old || fail \"cannot rename NbtAad.swap\"\n");
            sb.Append("chmod 755 $A/nbt-aad\n");
            sb.Append("ls -l $A/ $P/\n");
            sb.Append("echo \"NBTC-RESULT: OK\"\n");
            return sb.ToString();
        }

        public static string KeepOneScript()
        {
            var sb = new StringBuilder();
            sb.Append("#!/bin/sh\n# NBT Companion - keep only the installed nbt-aad (hu-omap)\n");
            sb.Append(Common);
            sb.Append("rm -f $A/nbt-aad.old || fail \"cannot remove nbt-aad.old\"\n");
            sb.Append("rm -rf $A/NbtAad.old || fail \"cannot remove NbtAad.old\"\n");
            sb.Append("echo \"OK: previous version removed\"\n");
            sb.Append("echo \"NBTC-RESULT: OK\"\n");
            return sb.ToString();
        }

        /// <summary>Replaces one file: the current one is moved aside, the new one copied and its size checked, then the aside copy is dropped.</summary>
        public static string ReplaceFileScript(string source, string target, int expectedSize)
        {
            var sb = new StringBuilder();
            sb.Append("#!/bin/sh\n# NBT Companion - replace " + target + " (hu-omap)\n");
            sb.Append(Common);
            sb.Append("S=" + source + "\nD=" + target + "\nV=" + target + ".nbtc-prev\n");
            sb.Append("if [ \"$(size $S)\" != \"" + expectedSize + "\" ]; then fail \"$S has the wrong size\"; fi\n");
            sb.Append("rm -f $V\n");
            sb.Append("HAD=0\n");
            sb.Append("if [ -f $D ]; then mv $D $V || fail \"cannot rename $D\"; HAD=1; fi\n");
            sb.Append("if cp $S $D && [ \"$(size $D)\" = \"" + expectedSize + "\" ]; then\n");
            sb.Append("  rm -f $V\n");
            sb.Append("  echo \"OK: $D\"\n");
            sb.Append("  echo \"NBTC-RESULT: OK\"\n");
            sb.Append("else\n");
            sb.Append("  rm -f $D\n");
            sb.Append("  if [ $HAD = 1 ]; then mv $V $D; fi\n");
            sb.Append("  fail \"copy to $D failed, previous file put back\"\n");
            sb.Append("fi\n");
            return sb.ToString();
        }
    }
}
