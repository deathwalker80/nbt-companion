using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace NbtCompanion.Core
{
    /// <summary>
    /// SSH opening / closing with Xayanix's nbt-evo-ssh-unlocker. Its repository has no licence, so nothing of it is in
    /// this program: the script is downloaded from GitHub onto this PC, pinned to a commit whose file was checked
    /// (SHA-256), and run with the user's Python. A newer commit is used only after the user approves it in the app.
    /// </summary>
    public static class Unlocker
    {
        public const string Repo = "Xayanix/nbt-evo-ssh-unlocker";
        public const string ScriptName = "nbt_optdebug_flash.py";
        public const string PinnedCommit = "1791228bb5d8e4b4f7e48013dd2eadbbc0f57fd7";
        public const string PinnedSha256 = "1bb80ecab6f209364ed9d498407597aac5a9008ac9eb3a7599221470d652e426";
        public const string PinnedDate = "2026-11-07";

        public sealed class Python
        {
            public string Exe { get; set; } = "";
            public string Prefix { get; set; } = "";
            public string Version { get; set; } = "";
        }

        public static void Commit(Settings s, out string commit, out string sha)
        {
            if (s.ApprovedUnlockerCommit.Length == 40 && s.ApprovedUnlockerSha256.Length == 64)
            {
                commit = s.ApprovedUnlockerCommit;
                sha = s.ApprovedUnlockerSha256;
            }
            else
            {
                commit = PinnedCommit;
                sha = PinnedSha256;
            }
        }

        public static string RawUrl(string commit)
        {
            return "https://raw.githubusercontent.com/" + Repo + "/" + commit + "/" + ScriptName;
        }

        /// <summary>Script of the approved commit on this PC, downloaded once and checked every time.</summary>
        public static string EnsureScript(Settings s)
        {
            Commit(s, out string commit, out string sha);
            string dir = Path.Combine(AppPaths.Unlocker, commit.Substring(0, 12));
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, ScriptName);
            if (!File.Exists(path) || Hash.Sha256File(path) != sha)
            {
                byte[] data = GitHub.Download(RawUrl(commit));
                string got = Hash.Sha256(data);
                if (got != sha)
                {
                    throw new StepException(L.T("Lo script di Xayanix scaricato non e' quello verificato (SHA-256 ", "The downloaded Xayanix script is not the checked one (SHA-256 ")
                        + got + " / " + sha + ").");
                }
                File.WriteAllBytes(path, data);
            }
            Log.Write("Script di sblocco: " + Repo + " @ " + commit.Substring(0, 7) + ", SHA-256 verificato");
            return path;
        }

        /// <summary>Newest commit of Xayanix's repository, or null if it is the one in use.</summary>
        public static GhCommit? NewerCommit(Settings s)
        {
            Commit(s, out string commit, out _);
            GhCommit last = GitHub.LatestCommit(Repo);
            return last.Sha == commit ? null : last;
        }

        /// <summary>The user approves the newest commit: its file is downloaded now and its SHA-256 is what gets checked from now on.</summary>
        public static void Approve(Settings s, GhCommit c)
        {
            byte[] data = GitHub.Download(RawUrl(c.Sha));
            s.ApprovedUnlockerCommit = c.Sha;
            s.ApprovedUnlockerSha256 = Hash.Sha256(data);
            s.Save();
            string dir = Path.Combine(AppPaths.Unlocker, c.Sha.Substring(0, 12));
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, ScriptName), data);
            Log.Write("Approvato il commit " + c.Sha + " di " + Repo + " (SHA-256 " + s.ApprovedUnlockerSha256 + ")");
        }

        public static Python? FindPython()
        {
            var candidates = new List<(string exe, string prefix)> { ("py", "-3 "), ("python", "") };
            string local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Python");
            if (Directory.Exists(local))
            {
                foreach (string d in Directory.GetDirectories(local, "Python3*"))
                {
                    string exe = Path.Combine(d, "python.exe");
                    if (File.Exists(exe))
                    {
                        candidates.Add((exe, ""));
                    }
                }
            }
            foreach ((string exe, string prefix) in candidates)
            {
                try
                {
                    var psi = new ProcessStartInfo(exe, prefix + "--version")
                    {
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                    };
                    using Process? p = Process.Start(psi);
                    if (p == null)
                    {
                        continue;
                    }
                    string o = (p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd()).Trim();
                    p.WaitForExit(10000);
                    if (o.StartsWith("Python 3.", StringComparison.Ordinal))
                    {
                        return new Python { Exe = exe, Prefix = prefix, Version = o };
                    }
                }
                catch (Exception)
                {
                    // not this one
                }
            }
            return null;
        }

        /// <summary>Opens winget in a visible window: the user reads and accepts its terms there.</summary>
        public static void InstallPythonWithWinget()
        {
            var psi = new ProcessStartInfo("cmd.exe", "/k winget install --id Python.Python.3.12 -e --source winget")
            {
                UseShellExecute = true,
            };
            Process.Start(psi);
        }

        /// <summary>Runs the script; every line goes to the log. Returns the whole output.</summary>
        public static string Run(Python py, string script, string args)
        {
            string arguments = py.Prefix + "-I -u \"" + script + "\" " + args;
            Log.Write("> " + py.Exe + " " + arguments);
            var psi = new ProcessStartInfo(py.Exe, arguments)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(script) ?? AppPaths.Unlocker,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            psi.Environment["PYTHONIOENCODING"] = "utf-8";
            var all = new StringBuilder();
            using var p = new Process { StartInfo = psi };
            p.OutputDataReceived += (s, e) =>
            {
                if (e.Data != null)
                {
                    lock (all)
                    {
                        all.AppendLine(e.Data);
                    }
                    Log.Write(e.Data);
                }
            };
            p.ErrorDataReceived += (s, e) =>
            {
                if (e.Data != null)
                {
                    lock (all)
                    {
                        all.AppendLine(e.Data);
                    }
                    Log.Write(e.Data);
                }
            };
            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            // Never killed: stopping it in the middle of a write to the head unit is worse than waiting.
            // The script has its own timeouts (sockets 6 s, reboot wait 50 s).
            DateTime start = DateTime.Now;
            p.WaitForExit();
            Log.Write("(script terminato, codice " + p.ExitCode + ", " + (int)(DateTime.Now - start).TotalSeconds + " s)");
            lock (all)
            {
                return all.ToString();
            }
        }

        public static bool SelfTest(Python py, string script)
        {
            string o = Run(py, script, "--selftest");
            return o.Contains("[selftest] ALL PASS");
        }

        /// <summary>revert = false: OPT_DEBUG RUN (SSH open after the reboot); true: factory starter.cfg (SSH closed).</summary>
        public static bool Flash(Python py, string script, string? host, bool revert)
        {
            string args = "--commit" + (revert ? " --revert" : "") + (string.IsNullOrWhiteSpace(host) ? "" : " --host " + host);
            string o = Run(py, script, args);
            return revert ? o.Contains("REVERT WRITE SENT") : o.Contains("OPT_DEBUG WRITE SENT");
        }
    }
}
