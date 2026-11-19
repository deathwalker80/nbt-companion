using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace NbtCompanion.Core
{
    public sealed class CmdResult
    {
        public int? Exit { get; set; }
        public byte[] Output { get; set; } = Array.Empty<byte>();
        public string Error { get; set; } = "";
        public bool TimedOut { get; set; }

        public string Text
        {
            get { return Encoding.UTF8.GetString(Output); }
        }

        public string All
        {
            get
            {
                string t = Text;
                if (Error.Length > 0)
                {
                    t += (t.Length > 0 && !t.EndsWith("\n") ? "\n" : "") + Error;
                }
                return t;
            }
        }
    }

    /// <summary>
    /// SSH session with the head unit. The login lands on hu-jacinto; everything of nbt-aad, the HMI and the plugins is
    /// on hu-omap, reached with  on -f /net/hu-omap/ /bin/sh ...  Files for omap are staged in a folder of omap's /var
    /// partition (Stage, e.g. /fs/sda1/opt/nbtc), emptied after every operation, with the transfer method that passed
    /// the self-test of this session. /dev/shmem cannot be used: on the cars (19/11, BMW NBTevo_Y22292I) it refuses
    /// folders (mkdir: Function not implemented), so the scripts of the kits could not list their own files.
    /// </summary>
    public sealed class HeadUnit : IDisposable
    {
        public const string Omap = "/net/hu-omap/";
        /// <summary>Written in the kit scripts in place of the staging folder, replaced at upload time.</summary>
        public const string StageToken = "@NBTC_STAGE@";
        /// <summary>Free space needed in the staging partition before anything is copied there.</summary>
        public const long StageMinFreeKb = 65536;
        /// <summary>Staging folder on hu-omap, set by the transfer self-test of the session (physical path of /var/opt/nbtc).</summary>
        public static string Stage { get; private set; } = "/var/opt/nbtc";
        /// <summary>Single file in jacinto's RAM used only by the ViaJacinto transfer (no folder).</summary>
        private const string JacintoTmp = "/dev/shmem/nbtc-up.tmp";
        public const string AaDir = "/opt/aa";
        public const string PluginDir = "/opt/hmi/ID5/data/ro/Plugins/NbtAad";
        public const string HmiRo = "/opt/hmi/ID5/data/ro";
        /// <summary>Default root password of NBT EVO ID5/ID6 head units, pre-filled in the connection page (editable).</summary>
        public const string DefaultPassword = "ts&SK412";

        private enum WriteWay { None, DirectQnet, ViaJacinto, OnStdin }
        private enum ReadWay { None, OnCat, DirectQnet }

        private readonly SshClient _client;
        private WriteWay _write = WriteWay.None;
        private ReadWay _read = ReadWay.None;
        private string _jacintoNode = "";

        public string Host { get; }
        public bool TransferReady { get { return _write != WriteWay.None && _read != ReadWay.None; } }
        public string TransferDescription { get; private set; } = "";

        private HeadUnit(string host, SshClient client)
        {
            Host = host;
            _client = client;
        }

        public static HeadUnit Connect(string host, string password)
        {
            Log.AddSecret(password);
            var pwd = new PasswordAuthenticationMethod("root", password);
            var kbd = new KeyboardInteractiveAuthenticationMethod("root");
            kbd.AuthenticationPrompt += (s, e) =>
            {
                foreach (AuthenticationPrompt p in e.Prompts)
                {
                    p.Response = password;
                }
            };
            var info = new ConnectionInfo(host, 22, "root", pwd, kbd)
            {
                Timeout = TimeSpan.FromSeconds(15),
            };
            var client = new SshClient(info);
            client.HostKeyReceived += (s, e) =>
            {
                Log.Write("SSH host key " + e.HostKeyName + " " + e.FingerPrintSHA256);
                e.CanTrust = true;
            };
            client.KeepAliveInterval = TimeSpan.FromSeconds(10);
            try
            {
                client.Connect();
            }
            catch (SshAuthenticationException)
            {
                client.Dispose();
                throw new StepException(L.T("Password SSH rifiutata dalla testata.", "SSH password rejected by the head unit."), true);
            }
            catch (Exception ex)
            {
                client.Dispose();
                throw new StepException(L.T("Collegamento SSH a " + host + " non riuscito: ", "SSH connection to " + host + " failed: ") + ex.Message);
            }
            Log.Write("SSH: collegato a " + host + " (" + client.ConnectionInfo.CurrentServerEncryption + ", "
                + client.ConnectionInfo.CurrentServerHmacAlgorithm + ", " + client.ConnectionInfo.CurrentHostKeyAlgorithm + ")");
            return new HeadUnit(host, client);
        }

        public bool Connected
        {
            get { return _client.IsConnected; }
        }

        public void Dispose()
        {
            try
            {
                _client.Disconnect();
            }
            catch (Exception)
            {
                // already closed
            }
            _client.Dispose();
        }

        // ------------------------------------------------------------------ plain commands (hu-jacinto)

        public CmdResult Run(string command, int timeoutSec = 60, byte[]? stdin = null, bool quiet = false)
        {
            if (!quiet)
            {
                Log.Write("$ " + command + (stdin != null ? "   [" + stdin.Length + " byte su stdin]" : ""));
            }
            var r = new CmdResult();
            using SshCommand cmd = _client.CreateCommand(command);
            IAsyncResult ar = cmd.BeginExecute();
            // stdout is read while the command runs (binary safe); EndExecute would turn it into text.
            var buffer = new MemoryStream();
            Stream output = cmd.OutputStream;
            Task reader = Task.Run(() =>
            {
                var chunk = new byte[65536];
                try
                {
                    int n;
                    while ((n = output.Read(chunk, 0, chunk.Length)) > 0)
                    {
                        buffer.Write(chunk, 0, n);
                    }
                }
                catch (ObjectDisposedException)
                {
                    // stream closed with the channel
                }
            });
            if (stdin != null)
            {
                using (Stream input = cmd.CreateInputStream())
                {
                    input.Write(stdin, 0, stdin.Length);
                    input.Flush();
                }
            }
            DateTime end = DateTime.Now.AddSeconds(timeoutSec);
            while (!ar.IsCompleted)
            {
                if (DateTime.Now > end)
                {
                    r.TimedOut = true;
                    try
                    {
                        cmd.CancelAsync();
                    }
                    catch (Exception)
                    {
                        // channel already gone
                    }
                    break;
                }
                ar.AsyncWaitHandle.WaitOne(200);
            }
            if (!reader.Wait(TimeSpan.FromSeconds(10)))
            {
                output.Dispose();
                reader.Wait(TimeSpan.FromSeconds(2));
            }
            try
            {
                cmd.EndExecute(ar);
            }
            catch (Exception ex)
            {
                if (!r.TimedOut)
                {
                    r.Error = ex.Message;
                }
            }
            r.Output = buffer.ToArray();
            r.Error = (r.Error + cmd.Error).Trim();
            r.Exit = cmd.ExitStatus;
            if (!quiet)
            {
                string shown = r.All.TrimEnd();
                if (shown.Length > 0)
                {
                    Log.Write(shown);
                }
                Log.Write(r.TimedOut
                    ? "(fermato dopo " + timeoutSec + " s senza risposta)"
                    : "(uscita " + (r.Exit?.ToString() ?? "?") + ", " + r.Output.Length + " byte)");
            }
            return r;
        }

        // ------------------------------------------------------------------ hu-omap

        /// <summary>Runs one line on hu-omap with its own /bin/sh. The line must not contain single quotes.</summary>
        public CmdResult OmapShell(string line, int timeoutSec = 60, byte[]? stdin = null, bool quiet = false)
        {
            if (line.Contains('\''))
            {
                throw new ArgumentException("single quote in an omap command");
            }
            return Run("on -f " + Omap + " /bin/sh -c '" + line + "'", timeoutSec, stdin, quiet);
        }

        public void RemountRw()
        {
            CmdResult r = Run("mount -o remount rw /net/hu-omap/fs/sda0", 30);
            if (r.TimedOut || (r.Exit.HasValue && r.Exit.Value != 0))
            {
                throw new StepException(L.T("mount -o remount rw non riuscito: ", "mount -o remount rw failed: ") + r.All.Trim());
            }
        }

        /// <summary>
        /// Prepares the staging folder (PrepareStage) and finds a way to write files into it and to read them back,
        /// checked byte for byte with a test file that holds all 256 byte values. Nothing outside the staging folder
        /// is touched (and jacinto's /dev/shmem/nbtc-up.tmp, for the ViaJacinto way).
        /// </summary>
        public void TransferSelfTest()
        {
            Log.Section("Auto-test del trasferimento");
            CmdResult nodes = Run("ls /net", 20);
            string[] names = nodes.Text.Split(new[] { '\n', '\r', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (!names.Contains("hu-omap"))
            {
                throw new StepException(L.T("In /net non c'e' hu-omap: questa non e' una NBT EVO come quelle previste.",
                    "hu-omap is not in /net: this is not an NBT EVO as expected."));
            }
            _jacintoNode = names.FirstOrDefault(n => n.Contains("jacinto")) ?? "";
            PrepareStage();

            var rnd = new Random(1980);
            byte[] probe = new byte[49152];
            rnd.NextBytes(probe);
            for (int i = 0; i < 256; i++)
            {
                probe[i] = (byte)i;
            }
            string target = Stage + "/selftest.bin";

            var writes = new List<WriteWay> { WriteWay.DirectQnet };
            if (_jacintoNode.Length > 0)
            {
                writes.Add(WriteWay.ViaJacinto);
            }
            writes.Add(WriteWay.OnStdin);
            var reads = new[] { ReadWay.OnCat, ReadWay.DirectQnet };

            foreach (WriteWay w in writes)
            {
                _write = w;
                if (!TryWrite(probe, target))
                {
                    Log.Write("scrittura " + w + ": non riuscita");
                    continue;
                }
                foreach (ReadWay rw in reads)
                {
                    _read = rw;
                    byte[]? back = TryRead(target);
                    if (back != null && back.SequenceEqual(probe))
                    {
                        TransferDescription = "write " + w + ", read " + rw;
                        Log.Write("Auto-test OK: " + probe.Length + " byte scritti e riletti identici (" + TransferDescription + ")");
                        OmapShell("rm -f " + target, 20, null, true);
                        return;
                    }
                    Log.Write("scrittura " + w + " / lettura " + rw + ": dati diversi o lettura non riuscita");
                }
            }
            _write = WriteWay.None;
            _read = ReadWay.None;
            throw new StepException(L.T("Auto-test del trasferimento NON superato: nessun modo verificato per copiare file su hu-omap. Non installo nulla.",
                "Transfer self-test FAILED: no verified way to copy files to hu-omap. Nothing will be installed."));
        }

        private void RequireTransfer()
        {
            if (!TransferReady)
            {
                throw new StepException(L.T("Auto-test del trasferimento non eseguito.", "Transfer self-test not run."));
            }
        }

        private bool TryWrite(byte[] data, string omapPath)
        {
            CmdResult r;
            switch (_write)
            {
                case WriteWay.DirectQnet:
                    r = Run("cat > /net/hu-omap" + omapPath, 120, data, true);
                    break;
                case WriteWay.ViaJacinto:
                    r = Run("cat > " + JacintoTmp, 120, data, true);
                    if (r.TimedOut || r.Exit != 0)
                    {
                        return false;
                    }
                    r = OmapShell("cp /net/" + _jacintoNode + JacintoTmp + " " + omapPath, 120, null, true);
                    Run("rm -f " + JacintoTmp, 20, null, true);
                    break;
                case WriteWay.OnStdin:
                    r = OmapShell("cat > " + omapPath, 120, data, true);
                    break;
                default:
                    return false;
            }
            return !r.TimedOut && r.Exit == 0;
        }

        private byte[]? TryRead(string omapPath)
        {
            CmdResult r;
            switch (_read)
            {
                case ReadWay.OnCat:
                    r = OmapShell("cat " + omapPath, 180, null, true);
                    break;
                case ReadWay.DirectQnet:
                    r = Run("cat /net/hu-omap" + omapPath, 180, null, true);
                    break;
                default:
                    return null;
            }
            if (r.TimedOut || r.Exit != 0)
            {
                return null;
            }
            return r.Output;
        }

        /// <summary>Copies bytes to a file on hu-omap and checks them by reading them back.</summary>
        public void Upload(byte[] data, string omapPath)
        {
            RequireTransfer();
            Log.Write("-> " + omapPath + " (" + data.Length + " byte, sha256 " + Hash.Sha256(data) + ")");
            if (!TryWrite(data, omapPath))
            {
                throw new StepException(L.T("Copia non riuscita: ", "Copy failed: ") + omapPath);
            }
            byte[]? back = TryRead(omapPath);
            if (back == null || !back.SequenceEqual(data))
            {
                throw new StepException(L.T("Il file copiato non e' identico all'originale: ", "The copied file differs from the original: ") + omapPath);
            }
        }

        public void UploadText(string text, string omapPath)
        {
            Upload(Encoding.UTF8.GetBytes(text.Replace("\r\n", "\n")), omapPath);
        }

        /// <summary>Reads a regular file of hu-omap. Null if it does not exist.</summary>
        public byte[]? Download(string omapPath)
        {
            RequireTransfer();
            CmdResult t = OmapShell("test -f " + omapPath + " && echo YES", 20, null, true);
            if (!t.Text.Contains("YES"))
            {
                return null;
            }
            byte[]? data = TryRead(omapPath);
            if (data == null)
            {
                throw new StepException(L.T("Lettura non riuscita: ", "Read failed: ") + omapPath);
            }
            Log.Write("<- " + omapPath + " (" + data.Length + " byte, sha256 " + Hash.Sha256(data) + ")");
            return data;
        }

        /// <summary>Uploads a script to omap RAM and runs it with omap's /bin/sh, stderr merged into stdout.</summary>
        public CmdResult RunOmapScript(string name, string script, string args, int timeoutSec)
        {
            string path = Stage + "/" + name;
            UploadText(script, path);
            return Run("on -f " + Omap + " /bin/sh " + path + (args.Length > 0 ? " " + args : "") + " 2>&1", timeoutSec);
        }

        public void CleanStage()
        {
            if (TransferReady)
            {
                OmapShell("rm -rf " + Stage, 60, null, true);
            }
        }

        /// <summary>
        /// Staging folder in omap's /var partition (on the cars /var -> /fs/sda1, read-write without remount, the
        /// same partition where NBT-AAD and NBTApps keep their data and run their programs). The physical path is
        /// used, so the direct writes through /net/hu-omap do not depend on the /var link. Before use: the folder
        /// must be created, the partition must have StageMinFreeKb free, and sub-folders must really work
        /// (create, write, list, remove). Otherwise nothing is installed.
        /// </summary>
        private void PrepareStage()
        {
            CmdResult v = OmapShell("ls -ld /var", 20);
            if (v.TimedOut)
            {
                throw new StepException(L.T("hu-omap non risponde a  on -f.", "hu-omap does not answer to  on -f."));
            }
            string root = StageRootFromLs(v.Text)
                ?? throw new StepException(L.T("Non riconosco /var su hu-omap (", "Cannot recognise /var on hu-omap (") + v.All.Trim()
                    + L.T("): non installo nulla.", "): nothing will be installed."));
            Stage = root + "/opt/nbtc";
            CmdResult mk = OmapShell("mkdir -p " + Stage + " && echo MK-OK", 30);
            if (!mk.Text.Contains("MK-OK"))
            {
                throw new StepException(L.T("Non riesco a creare la cartella di appoggio ", "Cannot create the staging folder ") + Stage
                    + L.T(": non installo nulla.", ": nothing will be installed."));
            }
            CmdResult df = OmapShell("df -k " + Stage, 20);
            long free = FreeKbFromDf(df.Text);
            if (free < StageMinFreeKb)
            {
                throw new StepException(L.T("Spazio libero insufficiente o non leggibile per ", "Free space too low or unreadable for ") + Stage + " ("
                    + (free < 0 ? "?" : (free / 1024) + " MB") + L.T(", ne servono almeno ", ", at least ") + (StageMinFreeKb / 1024)
                    + L.T(" MB): non installo nulla.", " MB needed): nothing will be installed."));
            }
            string t = Stage + "/dirtest";
            CmdResult d = OmapShell("rm -rf " + t + " && mkdir -p " + t + "/a && echo x > " + t + "/a/f && ls " + t + "/a && rm -rf " + t
                + " && test ! -d " + t + " && echo DIR-OK", 30);
            if (!d.Text.Contains("DIR-OK") || !Regex.IsMatch(d.Text, @"(^|\n)f(\r?\n|$)"))
            {
                throw new StepException(L.T("Nella cartella di appoggio ", "In the staging folder ") + Stage
                    + L.T(" le sottocartelle non funzionano: non installo nulla.", " sub-folders do not work: nothing will be installed."));
            }
            Log.Write("Cartella di appoggio: " + Stage + " (" + (free / 1024) + " MB liberi), sottocartelle verificate");
        }

        /// <summary>Physical root of /var from  ls -ld /var : the link target, or /var itself if it is a folder.</summary>
        public static string? StageRootFromLs(string ls)
        {
            string line = ls.Split('\n').Select(x => x.Trim()).FirstOrDefault(x => x.Length > 0) ?? "";
            string root;
            int arrow = line.IndexOf(" -> ", StringComparison.Ordinal);
            if (line.StartsWith("l", StringComparison.Ordinal) && arrow > 0)
            {
                root = line.Substring(arrow + 4).Trim();
            }
            else if (line.StartsWith("d", StringComparison.Ordinal))
            {
                root = "/var";
            }
            else
            {
                return null;
            }
            root = root.TrimEnd('/');
            return Regex.IsMatch(root, @"^/[A-Za-z0-9_./-]+$") && !root.Contains("..") ? root : null;
        }

        /// <summary>Available KB from  df -k  (QNX and Linux): the field before the one with the percent sign. -1 if not found.</summary>
        public static long FreeKbFromDf(string df)
        {
            foreach (string line in df.Split('\n').Reverse())
            {
                string[] w = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 1; i < w.Length; i++)
                {
                    if (w[i].EndsWith("%", StringComparison.Ordinal) && w[i].TrimEnd('%').All(char.IsDigit) && long.TryParse(w[i - 1], out long kb))
                    {
                        return kb;
                    }
                }
            }
            return -1;
        }
    }
}
