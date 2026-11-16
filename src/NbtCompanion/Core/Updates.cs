using System.Collections.Generic;
using System.Linq;

namespace NbtCompanion.Core
{
    /// <summary>Comparison of the installed NBT-AAD with the releases on GitHub, for the update page.</summary>
    public static class AadUpdate
    {
        public enum State
        {
            NotInstalled,
            ListUnreadable,
            Unknown,
            Older,
            NewPreRelease,
            NewerPreRelease,
            UpToDate,
        }

        /// <summary>
        /// Only a known build older than the latest release is an update (Older). A pre-release newer than the latest
        /// release is never taken back to the release, a newer pre-release is only signalled (it is installed from
        /// page 3), an unknown build (not in any release) is left alone.
        /// </summary>
        public static State Evaluate(IReadOnlyList<GhRelease> releases, bool present, string daemonSha)
        {
            if (!present)
            {
                return State.NotInstalled;
            }
            GhRelease? rel = releases.Where(r => !r.Prerelease).OrderByDescending(r => r.Published).FirstOrDefault();
            GhRelease? newest = releases.OrderByDescending(r => r.Published).FirstOrDefault();
            if (rel == null)
            {
                return State.ListUnreadable;
            }
            GhRelease? installed = releases.FirstOrDefault(r => r.Asset("nbt-aad")?.Sha256 == daemonSha);
            if (installed == null)
            {
                return State.Unknown;
            }
            if (installed.Published < rel.Published)
            {
                return State.Older;
            }
            if (newest != null && newest.Prerelease && newest.Published > installed.Published)
            {
                return State.NewPreRelease;
            }
            if (installed.Prerelease && installed.Published > rel.Published)
            {
                return State.NewerPreRelease;
            }
            return State.UpToDate;
        }
    }
}
