using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace NbtCompanion.Core
{
    /// <summary>
    /// Self-update from the releases of deathwalker80/nbt-companion (no pre-releases). The release must carry the asset
    /// NBT-Companion.exe with the SHA-256 published by GitHub; the downloaded exe must report the version of the tag.
    /// The running exe is renamed to NBT-Companion.exe.old (Windows allows renaming a running program), the new one
    /// takes its place and is started; the .old file is deleted at the next start.
    /// </summary>
    public static class SelfUpdate
    {
        public const string Repo = "deathwalker80/nbt-companion";
        public const string AssetName = "NBT-Companion.exe";
        public const string UpdatedArg = "--updated";

        public sealed class Available
        {
            public GhRelease Release { get; set; } = new GhRelease();
            public GhAsset Asset { get; set; } = new GhAsset();
            public Version Version { get; set; } = new Version(0, 0);
        }

        public static Version? ParseVersion(string tag)
        {
            string t = tag.Trim();
            if (t.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            {
                t = t.Substring(1);
            }
            return Version.TryParse(t, out Version? v) ? Normalize(v) : null;
        }

        private static Version Normalize(Version v)
        {
            return new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
        }

        public static Version Current
        {
            get { return ParseVersion(AppInfo.Version) ?? new Version(0, 0, 0); }
        }

        /// <summary>Newest release newer than this program with a usable exe, or null.</summary>
        public static Available? Check()
        {
            return Pick(GitHub.Releases(Repo), Current);
        }

        public static Available? Pick(System.Collections.Generic.IEnumerable<GhRelease> releases, Version current)
        {
            Available? best = null;
            foreach (GhRelease r in releases.Where(x => !x.Prerelease))
            {
                Version? v = ParseVersion(r.Tag);
                GhAsset? a = r.Asset(AssetName);
                if (v == null || a == null || a.Sha256.Length != 64 || v <= current)
                {
                    continue;
                }
                if (best == null || v > best.Version)
                {
                    best = new Available { Release = r, Asset = a, Version = v };
                }
            }
            return best;
        }

        /// <summary>Downloads and checks the new exe, swaps it with the running one and starts it. The caller then exits.</summary>
        public static void Apply(Available up)
        {
            string? current = Environment.ProcessPath;
            if (string.IsNullOrEmpty(current) || !File.Exists(current))
            {
                throw new StepException(L.T("Percorso del programma non leggibile: aggiornamento non possibile.",
                    "Program path not readable: update not possible."));
            }
            byte[] data = GitHub.DownloadAsset(up.Release, up.Asset, false);
            string dir = AppPaths.Sub("update");
            string staged = Path.Combine(dir, "NBT-Companion-" + up.Version + ".exe");
            File.WriteAllBytes(staged, data);
            FileVersionInfo fv = FileVersionInfo.GetVersionInfo(staged);
            Version? inside = ParseVersion(fv.ProductVersion ?? "");
            if (inside == null || inside != up.Version)
            {
                throw new StepException(L.T("L'exe scaricato dichiara la versione ", "The downloaded exe reports version ")
                    + (fv.ProductVersion ?? "?") + L.T(" invece di ", " instead of ") + up.Version + L.T(": non lo installo.", ": not installed."));
            }
            string old = current + ".old";
            try
            {
                if (File.Exists(old))
                {
                    File.Delete(old);
                }
                File.Move(current, old);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                throw new StepException(L.T("Non posso sostituire il programma nella sua cartella (", "Cannot replace the program in its folder (")
                    + ex.Message + L.T("). Spostalo in una cartella tua (es. Documenti) o scarica a mano: ", "). Move it to a folder of yours (e.g. Documents) or download by hand: ")
                    + staged);
            }
            try
            {
                File.Copy(staged, current, false);
            }
            catch (Exception ex)
            {
                File.Move(old, current);
                throw new StepException(L.T("Copia del nuovo programma non riuscita, rimesso quello di prima: ", "Copy of the new program failed, the previous one was put back: ") + ex.Message);
            }
            Log.Write("Aggiornato " + AppInfo.Version + " -> " + up.Version + " (SHA-256 " + up.Asset.Sha256 + "). Riavvio il programma.");
            Process.Start(new ProcessStartInfo(current, UpdatedArg) { UseShellExecute = false });
        }

        /// <summary>At start: removes the previous exe left by an update and the staged copies.</summary>
        public static void Cleanup()
        {
            try
            {
                string? current = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(current) && File.Exists(current + ".old"))
                {
                    File.Delete(current + ".old");
                }
                string dir = Path.Combine(AppPaths.Root, "update");
                if (Directory.Exists(dir))
                {
                    foreach (string f in Directory.GetFiles(dir, "*.exe"))
                    {
                        File.Delete(f);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Log.Write("Pulizia dopo l'aggiornamento non completata: " + ex.Message);
            }
        }
    }
}
