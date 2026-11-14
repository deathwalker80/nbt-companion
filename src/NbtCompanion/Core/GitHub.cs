using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;

namespace NbtCompanion.Core
{
    public sealed class GhAsset
    {
        public string Name { get; set; } = "";
        public long Size { get; set; }
        public string Sha256 { get; set; } = "";
        public string Url { get; set; } = "";
    }

    public sealed class GhRelease
    {
        public string Tag { get; set; } = "";
        public string Name { get; set; } = "";
        public bool Prerelease { get; set; }
        public DateTime Published { get; set; }
        public string Body { get; set; } = "";
        public List<GhAsset> Assets { get; set; } = new List<GhAsset>();

        public GhAsset? Asset(string name)
        {
            return Assets.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        public override string ToString()
        {
            return Name;
        }
    }

    public sealed class GhCommit
    {
        public string Sha { get; set; } = "";
        public DateTime Date { get; set; }
        public string Message { get; set; } = "";
    }

    /// <summary>Public GitHub API (no account, no token). Files are downloaded on this PC only.</summary>
    public static class GitHub
    {
        private static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
            var h = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            h.DefaultRequestHeaders.UserAgent.ParseAdd(AppInfo.UserAgent);
            h.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            return h;
        }

        /// <summary>Network errors become a message in clear (offline PC, GitHub not reachable).</summary>
        private static HttpResponseMessage Fetch(string url)
        {
            try
            {
                return Http.GetAsync(url).GetAwaiter().GetResult();
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException)
            {
                throw new StepException(L.T("GitHub non raggiungibile (", "GitHub not reachable (") + ex.Message + "): " + url);
            }
        }

        private static string Get(string url)
        {
            using HttpResponseMessage resp = Fetch(url);
            string body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (!resp.IsSuccessStatusCode)
            {
                throw new StepException("GitHub " + (int)resp.StatusCode + ": " + url + "\n" + body);
            }
            return body;
        }

        /// <summary>All releases and pre-releases, newest first by publication date (the API does not sort them).</summary>
        public static List<GhRelease> Releases(string ownerRepo)
        {
            var list = new List<GhRelease>();
            for (int page = 1; page <= 5; page++)
            {
                string json = Get("https://api.github.com/repos/" + ownerRepo + "/releases?per_page=100&page=" + page);
                List<GhRelease> chunk = ParseReleases(json);
                list.AddRange(chunk);
                if (chunk.Count < 100)
                {
                    break;
                }
            }
            return list.OrderByDescending(r => r.Published).ToList();
        }

        public static List<GhRelease> ParseReleases(string json)
        {
            var list = new List<GhRelease>();
            using JsonDocument doc = JsonDocument.Parse(json);
            foreach (JsonElement e in doc.RootElement.EnumerateArray())
            {
                if (e.TryGetProperty("draft", out JsonElement d) && d.ValueKind == JsonValueKind.True)
                {
                    continue;
                }
                var r = new GhRelease
                {
                    Tag = Str(e, "tag_name"),
                    Name = Str(e, "name"),
                    Prerelease = e.TryGetProperty("prerelease", out JsonElement p) && p.ValueKind == JsonValueKind.True,
                    Body = Str(e, "body"),
                };
                if (r.Name.Trim().Length == 0)
                {
                    r.Name = r.Tag;
                }
                r.Name = r.Name.Trim();
                string pub = Str(e, "published_at");
                if (pub.Length == 0)
                {
                    pub = Str(e, "created_at");
                }
                if (DateTime.TryParse(pub, null, System.Globalization.DateTimeStyles.AdjustToUniversal, out DateTime dt))
                {
                    r.Published = dt;
                }
                if (e.TryGetProperty("assets", out JsonElement assets))
                {
                    foreach (JsonElement a in assets.EnumerateArray())
                    {
                        string digest = Str(a, "digest");
                        r.Assets.Add(new GhAsset
                        {
                            Name = Str(a, "name"),
                            Size = a.TryGetProperty("size", out JsonElement s) && s.TryGetInt64(out long sz) ? sz : 0,
                            Sha256 = digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? digest.Substring(7).ToLowerInvariant() : "",
                            Url = Str(a, "browser_download_url"),
                        });
                    }
                }
                list.Add(r);
            }
            return list;
        }

        public static GhCommit LatestCommit(string ownerRepo)
        {
            string json = Get("https://api.github.com/repos/" + ownerRepo + "/commits?per_page=1");
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement c = doc.RootElement[0];
            var r = new GhCommit { Sha = Str(c, "sha") };
            if (c.TryGetProperty("commit", out JsonElement inner))
            {
                r.Message = Str(inner, "message");
                if (inner.TryGetProperty("committer", out JsonElement who))
                {
                    DateTime.TryParse(Str(who, "date"), out DateTime dt);
                    r.Date = dt;
                }
            }
            return r;
        }

        private static string Str(JsonElement e, string name)
        {
            return e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        }

        public static byte[] Download(string url)
        {
            Log.Write("download " + url);
            using HttpResponseMessage resp = Fetch(url);
            if (!resp.IsSuccessStatusCode)
            {
                throw new StepException(L.T("Download non riuscito (", "Download failed (") + (int)resp.StatusCode + "): " + url);
            }
            return resp.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Downloads a release file, checks size and the SHA-256 published by GitHub, keeps a copy on the PC.
        /// A file without a published SHA-256 is refused unless allowNoDigest is true.
        /// </summary>
        public static byte[] DownloadAsset(GhRelease rel, GhAsset asset, bool allowNoDigest)
        {
            byte[] data = Download(asset.Url);
            if (asset.Size > 0 && data.Length != asset.Size)
            {
                throw new StepException(asset.Name + ": " + data.Length + L.T(" byte scaricati, GitHub ne dichiara ", " bytes downloaded, GitHub declares ") + asset.Size);
            }
            string sha = Hash.Sha256(data);
            if (asset.Sha256.Length > 0)
            {
                if (sha != asset.Sha256)
                {
                    throw new StepException(asset.Name + ": SHA-256 " + sha + L.T(" diverso da quello pubblicato da GitHub ", " differs from the one published by GitHub ") + asset.Sha256);
                }
                Log.Write(asset.Name + ": SHA-256 verificato con GitHub (" + sha + ")");
            }
            else if (!allowNoDigest)
            {
                throw new StepException(asset.Name + L.T(": GitHub non pubblica lo SHA-256 di questo file.", ": GitHub publishes no SHA-256 for this file."));
            }
            else
            {
                Log.Write(asset.Name + ": GitHub non pubblica lo SHA-256; scaricato " + sha);
            }
            string dir = Path.Combine(AppPaths.Downloads, Safe(rel.Tag));
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, asset.Name), data);
            return data;
        }

        private static string Safe(string s)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                s = s.Replace(c, '_');
            }
            return s;
        }
    }
}
