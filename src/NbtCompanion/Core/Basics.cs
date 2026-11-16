using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NbtCompanion.Core
{
    /// <summary>Interface language: every visible text is written as T("italiano", "english").</summary>
    public static class L
    {
        public static bool English { get; set; }

        public static string T(string it, string en)
        {
            return English ? en : it;
        }
    }

    public static class AppInfo
    {
        public static string Version
        {
            get
            {
                var a = Assembly.GetExecutingAssembly();
                var info = a.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                if (info != null && !string.IsNullOrEmpty(info.InformationalVersion))
                {
                    return info.InformationalVersion;
                }
                return a.GetName().Version?.ToString(3) ?? "?";
            }
        }

        public static string UserAgent
        {
            get { return "NBT-Companion/" + Version; }
        }
    }

    /// <summary>Folders on the PC. Nothing of the head unit's password is ever written here.</summary>
    public static class AppPaths
    {
        public static string Root
        {
            get
            {
                string p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NBT-Companion");
                Directory.CreateDirectory(p);
                return p;
            }
        }

        public static string Sub(string name)
        {
            string p = Path.Combine(Root, name);
            Directory.CreateDirectory(p);
            return p;
        }

        public static string Logs { get { return Sub("logs"); } }
        public static string Downloads { get { return Sub("downloads"); } }
        public static string Unlocker { get { return Sub("unlocker"); } }
        public static string Icons { get { return Sub("icons"); } }
        public static string PcBackup { get { return Sub("backup"); } }

        public static string Diagnostics
        {
            get
            {
                string p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NBT-Companion");
                Directory.CreateDirectory(p);
                return p;
            }
        }

        public static string SettingsFile { get { return Path.Combine(Root, "settings.json"); } }
        public static string ColorIconsFile { get { return Path.Combine(Icons, "NbtAad_ALL_Images.zip"); } }
    }

    /// <summary>Complete log of every operation, on screen and in a file. The password never reaches it.</summary>
    public static class Log
    {
        private static readonly object Gate = new object();
        private static string? _file;
        private static readonly List<string> Secrets = new List<string>();

        public static event Action<string>? Line;

        public static string FilePath
        {
            get
            {
                lock (Gate)
                {
                    if (_file == null)
                    {
                        _file = Path.Combine(AppPaths.Logs, "nbt-companion-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log");
                    }
                    return _file;
                }
            }
        }

        /// <summary>A value that must never appear in the log (the SSH password while it is in memory).</summary>
        public static void AddSecret(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return;
            }
            lock (Gate)
            {
                if (!Secrets.Contains(s))
                {
                    Secrets.Add(s);
                }
            }
        }

        public static void ClearSecrets()
        {
            lock (Gate)
            {
                Secrets.Clear();
            }
        }

        public static void Write(string text)
        {
            string clean = text.Replace("\r\n", "\n").Replace('\r', '\n');
            lock (Gate)
            {
                foreach (string s in Secrets)
                {
                    clean = clean.Replace(s, "********");
                }
            }
            string stamp = DateTime.Now.ToString("HH:mm:ss");
            var sb = new StringBuilder();
            foreach (string part in clean.Split('\n'))
            {
                sb.Append(stamp).Append("  ").Append(part).Append(Environment.NewLine);
            }
            string block = sb.ToString();
            lock (Gate)
            {
                File.AppendAllText(FilePath, block, new UTF8Encoding(false));
            }
            Line?.Invoke(block);
        }

        public static void Section(string title)
        {
            Write("==== " + title + " ====");
        }
    }

    public static class Hash
    {
        public static string Sha256(byte[] data)
        {
            return Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
        }

        public static string Sha256File(string path)
        {
            using var fs = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
        }
    }

    public sealed class Settings
    {
        public string Language { get; set; } = "it";
        public string Host { get; set; } = "";
        public string Brand { get; set; } = "";
        public int KeepVersions { get; set; } = 2;
        public bool ColorIcons { get; set; } = true;
        public bool MenuInConnected { get; set; } = true;
        public bool ShowPrereleases { get; set; } = true;
        public string TileMode { get; set; } = "follow";
        public string MenuLookImages { get; set; } = "";
        public string MenuLookLabel { get; set; } = "Android Auto";
        public string PlusApp { get; set; } = "nbt";
        public string ApprovedUnlockerCommit { get; set; } = "";
        public string ApprovedUnlockerSha256 { get; set; } = "";

        public static Settings Load()
        {
            try
            {
                if (File.Exists(AppPaths.SettingsFile))
                {
                    var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(AppPaths.SettingsFile));
                    if (s != null)
                    {
                        return s;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Write("settings.json non leggibile, uso i valori iniziali: " + ex.Message);
            }
            return new Settings();
        }

        public void Save()
        {
            var opt = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(AppPaths.SettingsFile, JsonSerializer.Serialize(this, opt), new UTF8Encoding(false));
        }
    }

    /// <summary>Error with a message meant for the user (already in the current language).</summary>
    public sealed class StepException : Exception
    {
        public StepException(string message) : base(message)
        {
        }

        public StepException(string message, bool authRejected) : base(message)
        {
            AuthRejected = authRejected;
        }

        /// <summary>The head unit refused the SSH password (the automatic connection stops trying).</summary>
        public bool AuthRejected { get; }
    }
}
