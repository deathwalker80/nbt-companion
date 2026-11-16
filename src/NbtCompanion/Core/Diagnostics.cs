using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace NbtCompanion.Core
{
    /// <summary>
    /// Read-only collection of logs and state into a ZIP on the PC. Only commands and regular files: never raw devices
    /// (reading /dev/fs0 of the Jacinto rebooted the unit on 12/11; sysetadjread crashes there with SIGBUS).
    /// </summary>
    public static class Diagnostics
    {
        private static readonly (string name, string cmd)[] Jacinto =
        {
            ("jacinto-uname.txt", "uname -a"),
            ("jacinto-net.txt", "ls /net"),
            ("jacinto-ifconfig.txt", "ifconfig"),
            ("jacinto-pidin.txt", "pidin"),
            ("jacinto-sloginfo.txt", "sloginfo"),
            ("jacinto-df.txt", "df -k"),
        };

        private static readonly (string name, string cmd)[] OmapCmds =
        {
            ("omap-uname.txt", "uname -a"),
            ("omap-ifconfig.txt", "ifconfig"),
            ("omap-pfctl-rules.txt", "pfctl -s rules"),
            ("omap-pidin.txt", "pidin"),
            ("omap-sloginfo.txt", "sloginfo"),
            ("omap-df.txt", "df -k"),
            ("omap-ls-aa.txt", "ls -l /opt/aa/ /opt/hmi/ID5/data/ro/Plugins/NbtAad/ /opt/aa-tile/ /var/opt/aa-tile/"),
            ("omap-ls-hmi-ro.txt", "ls -l /opt/hmi/ID5/data/ro/"),
            ("omap-mnt.txt", "ls -l /mnt"),
        };

        private static readonly string[] OmapFiles =
        {
            "/opt/sys/etc/nbt_version.txt",
            "/etc/nbt_version.txt",
            "/dev/shmem/nbt-aad.log",
            "/dev/shmem/nbt-aad-app.log",
            "/dev/shmem/nbt-aad-state",
            "/dev/shmem/aa-tile.log",
            "/opt/aa/nbt-aad.env",
            "/opt/hmi/ID5/data/ro/Plugins/NbtAad/NbtAad.xml",
            "/opt/hmi/ID5/data/ro/Plugins/NbtAad/NbtAad.manifest",
            "/opt/conn/bin/start_webserver.sh",
            "/var/opt/aa-tile/mode",
            "/var/opt/aa-tile/last",
            "/var/opt/aa-tile/boots",
            "/var/opt/aa-tile/CRASHED",
            "/var/opt/aa-tile/DISABLED",
            "/opt/aa/menu-look-backup/files.txt",
            "/opt/aa/menu-look-backup/brand",
            "/fs/sda0/etc/pf.conf",
            "/etc/pf.conf",
        };

        public static string Collect(HeadUnit hu, string vin)
        {
            Log.Section("Diagnostica (sola lettura)");
            string name = "diagnostica-" + (vin.Length > 0 ? vin : "testata") + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip";
            string path = Path.Combine(AppPaths.Diagnostics, name);
            var index = new StringBuilder();
            index.AppendLine("NBT Companion " + AppInfo.Version + " - " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            index.AppendLine("Host " + hu.Host + ", VIN " + vin + ", trasferimento " + hu.TransferDescription);
            using (FileStream fs = File.Create(path))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                foreach ((string n, string cmd) in Jacinto)
                {
                    CmdResult r = hu.Run(cmd, 60, null, true);
                    Add(zip, n, Encoding.UTF8.GetBytes("$ " + cmd + "\n" + r.All + "\n(exit " + r.Exit + (r.TimedOut ? ", timeout" : "") + ")\n"));
                    index.AppendLine(n + "  <-  jacinto: " + cmd + "  (exit " + r.Exit + ")");
                }
                foreach ((string n, string cmd) in OmapCmds)
                {
                    CmdResult r = hu.OmapShell(cmd, 60, null, true);
                    Add(zip, n, Encoding.UTF8.GetBytes("$ " + cmd + "\n" + r.All + "\n(exit " + r.Exit + (r.TimedOut ? ", timeout" : "") + ")\n"));
                    index.AppendLine(n + "  <-  omap: " + cmd + "  (exit " + r.Exit + ")");
                }
                foreach (string f in OmapFiles)
                {
                    try
                    {
                        byte[]? b = hu.Download(f);
                        string entry = "files" + f;
                        if (b == null)
                        {
                            index.AppendLine(entry + "  (non presente)");
                            continue;
                        }
                        Add(zip, entry, b);
                        index.AppendLine(entry + "  (" + b.Length + " byte, sha256 " + Hash.Sha256(b) + ")");
                    }
                    catch (StepException ex)
                    {
                        index.AppendLine("files" + f + "  (lettura non riuscita: " + ex.Message + ")");
                    }
                }
                Add(zip, "nbt-companion.log", File.ReadAllBytes(Log.FilePath));
                Add(zip, "INDEX.txt", Encoding.UTF8.GetBytes(index.ToString()));
            }
            Log.Write("Diagnostica salvata: " + path);
            return path;
        }

        private static void Add(ZipArchive zip, string name, byte[] data)
        {
            ZipArchiveEntry e = zip.CreateEntry(name, CompressionLevel.Optimal);
            using Stream s = e.Open();
            s.Write(data, 0, data.Length);
        }
    }
}
