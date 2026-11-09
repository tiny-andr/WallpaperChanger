using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using Microsoft.Win32;

namespace WallpaperChanger
{
    // One GitHub release, reduced to what the update flow needs.
    internal sealed class UpdateInfo
    {
        public string Version = "";      // "2.0.12" - tag with the leading v stripped
        public string Tag = "";
        public string PageUrl = "";
        public string AssetName = "";    // installer file name, "" if the release has none
        public string AssetUrl = "";     // browser_download_url
        public string Digest = "";       // "sha256:<hex>", "" when GitHub did not publish one
        public long AssetSize;
    }

    internal sealed class UpdateException : Exception
    {
        public UpdateException(string message) : base(message)
        {
        }
    }

    // Auto-update backend: ask GitHub for the latest release, download the
    // installer through a China-friendly proxy when GitHub is unreachable,
    // verify the SHA256 digest GitHub publishes for every asset, and start the
    // Inno Setup installer silently.
    //
    // No third-party dependency: HttpClient, System.Text.Json and SHA256 are
    // part of the shared framework, so the project's zero-NuGet rule holds.
    internal static class Updater
    {
        public const string OwnerRepo = "tiny-andr/WallpaperChanger";
        public const string ApiUrl = "https://api.github.com/repos/" + OwnerRepo + "/releases/latest";
        public const string ReleasesUrl = "https://github.com/" + OwnerRepo + "/releases/latest";

        // Public GitHub proxies, tried in this order after the direct URL.
        // They are free services and can disappear at any time, which is why
        // the whole list is tried in sequence instead of pinning one.
        public static readonly string[] Mirrors = {
            "https://ghfast.top/",
            "https://gh-proxy.com/",
            "https://ghproxy.net/",
            "https://ghproxy.site/"
        };

        // "": direct GitHub. Otherwise the host of the mirror that served the
        // last successful download - surfaced in the log and the diagnostics.
        public static string LastMirror { get; private set; } = "";

        // Six seconds to first byte, per mirror, exactly like the reference
        // implementation this was modelled on.
        private const int FirstByteTimeoutMs = 6000;
        private const int ApiTimeoutMs = 15000;
        private const int IdleReadTimeoutMs = 30000;

        private static readonly HttpClient apiClient = CreateApiClient();
        private static readonly HttpClient downloadClient = CreateDownloadClient();

        private static HttpClient CreateApiClient()
        {
            SocketsHttpHandler handler = new SocketsHttpHandler();
            handler.ConnectTimeout = TimeSpan.FromMilliseconds(FirstByteTimeoutMs);
            handler.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            HttpClient c = new HttpClient(handler);
            c.Timeout = TimeSpan.FromMilliseconds(ApiTimeoutMs);
            c.DefaultRequestHeaders.UserAgent.ParseAdd("WallpaperChanger-Updater");
            c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return c;
        }

        private static HttpClient CreateDownloadClient()
        {
            SocketsHttpHandler handler = new SocketsHttpHandler();
            handler.ConnectTimeout = TimeSpan.FromMilliseconds(FirstByteTimeoutMs);
            // No decompression: the installer is already compressed, and a
            // transparently decompressed body would report a Content-Length
            // that no longer matches the bytes written to disk (progress would
            // run past 100%).
            HttpClient c = new HttpClient(handler);
            // A 66 MB installer legitimately takes minutes; the per-attempt
            // first-byte probe above plus the idle-read timeout inside the copy
            // loop are what keep a dead mirror from hanging the download.
            c.Timeout = Timeout.InfiniteTimeSpan;
            c.DefaultRequestHeaders.UserAgent.ParseAdd("WallpaperChanger-Updater");
            return c;
        }

        // ---- release lookup ------------------------------------------------

        public static UpdateInfo QueryLatest(CancellationToken ct)
        {
            using (HttpResponseMessage resp = apiClient.GetAsync(ApiUrl,
                HttpCompletionOption.ResponseHeadersRead, ct).GetAwaiter().GetResult())
            {
                if (!resp.IsSuccessStatusCode)
                {
                    throw new UpdateException("GitHub API HTTP " + (int)resp.StatusCode);
                }
                string json = resp.Content.ReadAsStringAsync(ct).GetAwaiter().GetResult();
                return ParseReleaseJson(json);
            }
        }

        // Parse the JSON the releases API returns. Kept separate from the HTTP
        // call so /updatetest can exercise it against a fixed fixture without
        // touching the network.
        public static UpdateInfo ParseReleaseJson(string json)
        {
            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                JsonElement root = doc.RootElement;
                UpdateInfo info = new UpdateInfo();
                info.Tag = Str(root, "tag_name");
                info.Version = NormalizeVersion(info.Tag);
                if (info.Version.Length == 0) info.Version = NormalizeVersion(Str(root, "name"));
                info.PageUrl = Str(root, "html_url");
                if (info.PageUrl.Length == 0) info.PageUrl = ReleasesUrl;

                JsonElement assets;
                if (root.TryGetProperty("assets", out assets) &&
                    assets.ValueKind == JsonValueKind.Array)
                {
                    int best = -1;
                    foreach (JsonElement a in assets.EnumerateArray())
                    {
                        string name = Str(a, "name");
                        // Only the installer is useful: a portable copy of the
                        // single-file exe cannot be installed over itself, and
                        // the release never publishes one anyway.
                        if (name.IndexOf(".exe", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        if (name.IndexOf("setup", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        int score = 1;
                        if (info.Version.Length > 0 &&
                            name.IndexOf(info.Version, StringComparison.OrdinalIgnoreCase) >= 0) score += 2;
                        if (score <= best) continue;
                        best = score;
                        info.AssetName = name;
                        info.AssetUrl = Str(a, "browser_download_url");
                        info.Digest = Str(a, "digest");
                        info.AssetSize = Num(a, "size");
                    }
                }
                return info;
            }
        }

        private static string Str(JsonElement e, string name)
        {
            JsonElement v;
            if (e.ValueKind == JsonValueKind.Object &&
                e.TryGetProperty(name, out v) &&
                v.ValueKind == JsonValueKind.String)
            {
                return v.GetString() ?? "";
            }
            return "";
        }

        private static long Num(JsonElement e, string name)
        {
            JsonElement v;
            if (e.ValueKind == JsonValueKind.Object &&
                e.TryGetProperty(name, out v) &&
                v.ValueKind == JsonValueKind.Number)
            {
                long n;
                if (v.TryGetInt64(out n)) return n;
                double d;
                if (v.TryGetDouble(out d)) return (long)d;
            }
            return 0;
        }

        // ---- versions ------------------------------------------------------

        public static bool IsNewer(UpdateInfo info, string currentVersion)
        {
            return info != null && CompareVersions(info.Version, currentVersion) > 0;
        }

        // Compare dotted versions ("v2.0.12" vs "2.0.11"). A pre-release suffix
        // ("2.1.0-beta") sorts below the matching release; build metadata after
        // "+" is ignored. Missing components count as zero, so "2.1" == "2.1.0".
        public static int CompareVersions(string a, string b)
        {
            string[] pa = NumericParts(a);
            string[] pb = NumericParts(b);
            int n = Math.Max(pa.Length, pb.Length);
            for (int i = 0; i < n; i++)
            {
                int x = i < pa.Length ? ParsePart(pa[i]) : 0;
                int y = i < pb.Length ? ParsePart(pb[i]) : 0;
                if (x != y) return x < y ? -1 : 1;
            }
            bool preA = HasPrerelease(a);
            bool preB = HasPrerelease(b);
            if (preA == preB) return 0;
            return preA ? -1 : 1;
        }

        public static string NormalizeVersion(string s)
        {
            if (s == null) return "";
            s = s.Trim();
            if (s.StartsWith("v", StringComparison.OrdinalIgnoreCase)) s = s.Substring(1);
            int cut = s.IndexOfAny(new char[] { '-', '+', ' ' });
            if (cut >= 0) s = s.Substring(0, cut);
            return s.Trim();
        }

        private static bool HasPrerelease(string s)
        {
            if (s == null) return false;
            s = s.Trim();
            if (s.StartsWith("v", StringComparison.OrdinalIgnoreCase)) s = s.Substring(1);
            int plus = s.IndexOf('+');
            if (plus >= 0) s = s.Substring(0, plus);
            return s.IndexOf('-') >= 0;
        }

        private static string[] NumericParts(string s)
        {
            string v = NormalizeVersion(s);
            if (v.Length == 0) return new string[0];
            return v.Split('.');
        }

        private static int ParsePart(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            int i = 0;
            int value = 0;
            while (i < s.Length && s[i] >= '0' && s[i] <= '9')
            {
                // Clamp instead of overflowing on a nonsense "999999999999".
                if (value < 100000000) value = value * 10 + (s[i] - '0');
                i++;
            }
            return value;
        }

        // ---- downloading ---------------------------------------------------

        public static string UpdatesDir()
        {
            return Path.Combine(AppPaths.DataDir, "updates");
        }

        // Every URL that might serve this asset, direct GitHub first.
        public static string[] CandidateUrls(string downloadUrl)
        {
            List<string> urls = new List<string>();
            urls.Add(downloadUrl);
            foreach (string m in Mirrors) urls.Add(m + downloadUrl);
            return urls.ToArray();
        }

        // Download and verify the installer. Returns the full path of the
        // verified file; throws UpdateException once every mirror has failed.
        // progress receives (bytesDone, bytesTotal) with -1 total when the
        // server sends no Content-Length.
        public static string Download(UpdateInfo info, string destDir,
            Action<long, long> progress, CancellationToken ct)
        {
            return Download(info, destDir, progress, ct, 0);
        }

        // startIndex 0 = direct GitHub, 1 = first mirror. Only the diagnostics
        // pass anything else: on a machine where direct GitHub works, the
        // failover chain would never be exercised otherwise.
        public static string Download(UpdateInfo info, string destDir,
            Action<long, long> progress, CancellationToken ct, int startIndex)
        {
            if (info == null) throw new UpdateException("no release information");
            if (string.IsNullOrEmpty(info.AssetUrl)) throw new UpdateException("release has no installer asset");
            string expected = DigestHex(info.Digest);
            if (expected.Length != 64)
            {
                // Refuse to install something we cannot verify: the whole point
                // of going through a third-party mirror is that the digest from
                // the API, not the mirror, decides what lands on the disk.
                throw new UpdateException("release asset has no sha256 digest");
            }

            Directory.CreateDirectory(destDir);
            string name = string.IsNullOrEmpty(info.AssetName)
                ? "WallpaperChanger-Setup.exe" : Path.GetFileName(info.AssetName);
            string final = Path.Combine(destDir, name);
            string part = final + ".part";
            TryDelete(part);
            DeleteOtherInstallers(destDir, name);

            List<string> errors = new List<string>();
            string[] candidates = CandidateUrls(info.AssetUrl);
            if (startIndex < 0) startIndex = 0;
            for (int i = startIndex; i < candidates.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                string label = LabelFor(i);
                string url = candidates[i];
                Log.Write("update: try " + label + " " + url);
                TryDelete(part);
                try
                {
                    if (!Reachable(url, ct))
                    {
                        errors.Add(label + ": no answer in " + (FirstByteTimeoutMs / 1000) + "s");
                        continue;
                    }
                    DownloadOne(url, part, progress, ct);
                    string actual = Sha256File(part);
                    if (!DigestMatches(info.Digest, actual))
                    {
                        TryDelete(part);
                        errors.Add(label + ": sha256 mismatch");
                        Log.Write("update: digest mismatch from " + label
                            + " expected=" + expected + " actual=" + actual);
                        continue;
                    }
                    TryDelete(final);
                    File.Move(part, final);
                    LastMirror = i == 0 ? "" : label;
                    Log.Write("update: downloaded " + final + " via " + (i == 0 ? "GitHub" : label)
                        + " (" + new FileInfo(final).Length + " bytes, sha256 ok)");
                    return final;
                }
                catch (Exception ex)
                {
                    TryDelete(part);
                    // A per-mirror timeout arrives here as TaskCanceledException,
                    // which is an OperationCanceledException too. Only the
                    // caller's own token means "the user cancelled"; anything
                    // else is this mirror failing and the next one gets a turn.
                    if (ct.IsCancellationRequested) throw;
                    errors.Add(label + ": " + ex.Message);
                    Log.Write("update: " + label + " failed: " + ex.Message);
                }
            }
            throw new UpdateException(string.Join("; ", errors.ToArray()));
        }

        private static string LabelFor(int index)
        {
            if (index == 0) return "GitHub";
            try
            {
                return new Uri(Mirrors[index - 1]).Host;
            }
            catch
            {
                return Mirrors[index - 1];
            }
        }

        // Six-second handshake: ask for the first byte only. A mirror that has
        // not answered by then is skipped, which is what makes the failover
        // quick instead of waiting out a full connect+read timeout on each.
        private static bool Reachable(string url, CancellationToken ct)
        {
            using (CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                cts.CancelAfter(FirstByteTimeoutMs);
                using (HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    req.Headers.Range = new RangeHeaderValue(0, 0);
                    using (HttpResponseMessage resp = downloadClient.SendAsync(req,
                        HttpCompletionOption.ResponseHeadersRead, cts.Token).GetAwaiter().GetResult())
                    {
                        return resp.IsSuccessStatusCode || (int)resp.StatusCode == 416;
                    }
                }
            }
        }

        private static void DownloadOne(string url, string partPath,
            Action<long, long> progress, CancellationToken ct)
        {
            using (HttpResponseMessage resp = downloadClient.GetAsync(url,
                HttpCompletionOption.ResponseHeadersRead, ct).GetAwaiter().GetResult())
            {
                if (!resp.IsSuccessStatusCode)
                {
                    throw new UpdateException("HTTP " + (int)resp.StatusCode);
                }
                long total = resp.Content.Headers.ContentLength.HasValue
                    ? resp.Content.Headers.ContentLength.Value : -1;
                using (Stream stream = resp.Content.ReadAsStreamAsync(ct).GetAwaiter().GetResult())
                using (FileStream fs = new FileStream(partPath, FileMode.Create,
                    FileAccess.Write, FileShare.None, 65536))
                {
                    byte[] buffer = new byte[65536];
                    long done = 0;
                    if (progress != null) progress(0, total);
                    while (true)
                    {
                        int n;
                        // Idle timeout per read: a mirror that stalls halfway
                        // through is abandoned instead of pinning the task.
                        using (CancellationTokenSource idle =
                            CancellationTokenSource.CreateLinkedTokenSource(ct))
                        {
                            idle.CancelAfter(IdleReadTimeoutMs);
                            n = stream.ReadAsync(buffer, 0, buffer.Length,
                                idle.Token).GetAwaiter().GetResult();
                        }
                        if (n <= 0) break;
                        fs.Write(buffer, 0, n);
                        done += n;
                        if (progress != null) progress(done, total);
                    }
                    fs.Flush(true);
                }
            }
        }

        private static void DeleteOtherInstallers(string dir, string keepName)
        {
            try
            {
                foreach (string f in Directory.GetFiles(dir, "WallpaperChanger-Setup*.exe"))
                {
                    if (!string.Equals(Path.GetFileName(f), keepName, StringComparison.OrdinalIgnoreCase))
                    {
                        TryDelete(f);
                    }
                }
            }
            catch
            {
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
            }
        }

        // ---- digests -------------------------------------------------------

        // "sha256:<hex>" -> lowercase hex, or "" when the algorithm is not
        // SHA-256 (a future digest format must not be accepted silently).
        public static string DigestHex(string digest)
        {
            if (string.IsNullOrEmpty(digest)) return "";
            string d = digest.Trim();
            int colon = d.IndexOf(':');
            if (colon >= 0)
            {
                string alg = d.Substring(0, colon).Trim();
                if (!alg.Equals("sha256", StringComparison.OrdinalIgnoreCase)) return "";
                d = d.Substring(colon + 1).Trim();
            }
            return d.ToLowerInvariant();
        }

        public static bool DigestMatches(string expectedDigest, string actualHex)
        {
            string want = DigestHex(expectedDigest);
            if (want.Length != 64 || string.IsNullOrEmpty(actualHex)) return false;
            return FixedTimeEquals(want, actualHex.Trim().ToLowerInvariant());
        }

        private static bool FixedTimeEquals(string a, string b)
        {
            if (a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        public static string Sha256Hex(byte[] data)
        {
            using (SHA256 sha = SHA256.Create())
            {
                return ToHex(sha.ComputeHash(data));
            }
        }

        public static string Sha256File(string path)
        {
            using (FileStream fs = File.OpenRead(path))
            using (SHA256 sha = SHA256.Create())
            {
                return ToHex(sha.ComputeHash(fs));
            }
        }

        private static string ToHex(byte[] hash)
        {
            StringBuilder sb = new StringBuilder(hash.Length * 2);
            for (int i = 0; i < hash.Length; i++) sb.Append(hash[i].ToString("x2"));
            return sb.ToString();
        }

        // ---- install -------------------------------------------------------

        // True when this exe was installed by WallpaperChanger.iss. A portable
        // copy (the single-file exe dropped in a folder) cannot be updated by
        // the installer without turning it into a Program Files install, so it
        // falls back to the download page instead of silently relocating.
        public static bool IsInstalledCopy()
        {
            string exeDir = "";
            try
            {
                exeDir = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory)
                    .TrimEnd('\\', '/');
            }
            catch
            {
            }
            string[] keys = {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{B3F5A8E2-7C4D-4A9E-9B2F-1D5E8C7A0F3B}_is1"
            };
            try
            {
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,
                    RegistryView.Registry64))
                {
                    foreach (string k in keys)
                    {
                        using (RegistryKey sub = baseKey.OpenSubKey(k))
                        {
                            if (sub == null) continue;
                            string loc = sub.GetValue("InstallLocation") as string;
                            if (string.IsNullOrEmpty(loc)) loc = sub.GetValue("Inno Setup: App Path") as string;
                            if (string.IsNullOrEmpty(loc)) continue;
                            string target = Path.GetFullPath(loc.TrimEnd('\\', '/'))
                                .TrimEnd('\\', '/');
                            if (string.Equals(exeDir, target, StringComparison.OrdinalIgnoreCase)) return true;
                        }
                    }
                }
            }
            catch
            {
            }

            // Fallback for a missing/renamed uninstall key: anything living
            // under Program Files is an installed copy, never portable.
            try
            {
                foreach (Environment.SpecialFolder folder in new Environment.SpecialFolder[] {
                    Environment.SpecialFolder.ProgramFiles,
                    Environment.SpecialFolder.ProgramFilesX86 })
                {
                    string pf = Environment.GetFolderPath(folder);
                    if (string.IsNullOrEmpty(pf)) continue;
                    string root = Path.GetFullPath(pf).TrimEnd('\\', '/') + "\\";
                    if (exeDir.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            catch
            {
            }
            return false;
        }

        // Start the Inno Setup installer silently and return. It shows its own
        // UAC prompt; the caller exits the app so Setup can replace the exe, and
        // the installer's [Run] entry starts the new version when it is done.
        public static void LaunchInstaller(string installerPath)
        {
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = installerPath;
            psi.Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS";
            psi.UseShellExecute = true;
            Process p = Process.Start(psi);
            if (p == null) throw new UpdateException("installer did not start");
            Log.Write("update: installer started (pid " + p.Id + ")");
        }

        public static void OpenReleasesPage()
        {
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = ReleasesUrl;
            psi.UseShellExecute = true;
            Process.Start(psi);
        }
    }
}
