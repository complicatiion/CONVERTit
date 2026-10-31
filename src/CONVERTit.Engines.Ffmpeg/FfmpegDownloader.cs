using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CONVERTit.Engines.Ffmpeg;

/// <summary>Release asset chosen for download.</summary>
public sealed record FfmpegRelease(string AssetName, string Url, long Size, string? ChecksumsUrl, string Version);

/// <summary>Installed download (data\tools\ffmpeg\download.json).</summary>
public sealed record FfmpegInstallInfo(string AssetName, string Version, DateTime InstalledAt, string Sha256);

/// <summary>
/// Downloads FFmpeg for Windows x64 from the official BtbN/FFmpeg-Builds GitHub releases
/// (GPL build: includes x264, x265, SVT-AV1, LAME, Opus, Vorbis, libwebp). The SHA-256 is checked against the
/// release's checksums.sha256. Only ffmpeg.exe, ffprobe.exe and the license are kept.
/// </summary>
public static partial class FfmpegDownloader
{
    public const string ReleaseApi = "https://api.github.com/repos/BtbN/FFmpeg-Builds/releases/tags/latest";
    public const string ReleasePage = "https://github.com/BtbN/FFmpeg-Builds/releases";

    public static HttpClient CreateClient(string version)
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("CONVERTit", version));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    /// <summary>Newest release branch build (e.g. ffmpeg-n8.0-latest-win64-gpl-8.0.zip), master as fallback.</summary>
    public static async Task<FfmpegRelease> FindLatestAsync(HttpClient client, CancellationToken cancellationToken = default)
    {
        using var response = await client.GetAsync(ReleaseApi, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        var assets = doc.RootElement.GetProperty("assets").EnumerateArray()
            .Select(a => (Name: a.GetProperty("name").GetString() ?? "",
                Url: a.GetProperty("browser_download_url").GetString() ?? "",
                Size: a.TryGetProperty("size", out var s) ? s.GetInt64() : 0))
            .ToList();

        var checksums = assets.FirstOrDefault(a => a.Name.Equals("checksums.sha256", StringComparison.OrdinalIgnoreCase)).Url;

        var best = assets
            .Select(a => (Asset: a, Match: ReleaseAsset().Match(a.Name)))
            .Where(x => x.Match.Success)
            .OrderByDescending(x => int.Parse(x.Match.Groups[1].Value))
            .ThenByDescending(x => int.Parse(x.Match.Groups[2].Value))
            .Select(x => new FfmpegRelease(x.Asset.Name, x.Asset.Url, x.Asset.Size, checksums,
                $"{x.Match.Groups[1].Value}.{x.Match.Groups[2].Value}"))
            .FirstOrDefault();
        if (best is not null)
        {
            return best;
        }

        var master = assets.FirstOrDefault(a => a.Name.Equals("ffmpeg-master-latest-win64-gpl.zip", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(master.Url))
        {
            return new FfmpegRelease(master.Name, master.Url, master.Size, checksums, "master");
        }

        throw new InvalidOperationException("No Windows x64 FFmpeg build found in the release.");
    }

    /// <summary>Downloads, verifies and installs into <paramref name="targetDirectory"/>.</summary>
    public static async Task<FfmpegInstallInfo> InstallAsync(HttpClient client, FfmpegRelease release, string targetDirectory,
        IProgress<double>? progress, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(targetDirectory);
        var temp = Path.Combine(Path.GetTempPath(), "convertit-" + Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            using (var response = await client.SendAsync(Download(release.Url), HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength ?? release.Size;
                await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using var output = File.Create(temp);
                var buffer = new byte[1 << 16];
                long read = 0;
                int n;
                while ((n = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, n), cancellationToken).ConfigureAwait(false);
                    read += n;
                    if (total > 0)
                    {
                        progress?.Report(Math.Min(0.95, read * 0.95 / total));
                    }
                }
            }

            var hash = await Sha256Async(temp, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(release.ChecksumsUrl))
            {
                using var sumsResponse = await client.SendAsync(Download(release.ChecksumsUrl), cancellationToken).ConfigureAwait(false);
                sumsResponse.EnsureSuccessStatusCode();
                var sums = await sumsResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var expected = sums.Split('\n')
                    .Select(l => l.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries))
                    .Where(p => p.Length == 2 && p[1].Trim().TrimStart('*').Equals(release.AssetName, StringComparison.OrdinalIgnoreCase))
                    .Select(p => p[0].ToLowerInvariant())
                    .FirstOrDefault();
                if (expected is null)
                {
                    throw new InvalidOperationException("Checksum for " + release.AssetName + " not found.");
                }

                if (!string.Equals(expected, hash, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("Checksum mismatch – download discarded.");
                }
            }

            // Extract into a staging folder first, then swap (running conversions keep their old files until done).
            var staging = targetDirectory + ".new";
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, true);
            }

            Directory.CreateDirectory(staging);
            using (var zip = ZipFile.OpenRead(temp))
            {
                foreach (var entry in zip.Entries)
                {
                    var name = entry.Name;
                    var keep = (entry.FullName.Contains("/bin/", StringComparison.OrdinalIgnoreCase) && name is "ffmpeg.exe" or "ffprobe.exe")
                        || name.Equals("LICENSE.txt", StringComparison.OrdinalIgnoreCase);
                    if (keep)
                    {
                        entry.ExtractToFile(Path.Combine(staging, name), overwrite: true);
                    }
                }
            }

            if (!File.Exists(Path.Combine(staging, "ffmpeg.exe")) || !File.Exists(Path.Combine(staging, "ffprobe.exe")))
            {
                throw new InvalidOperationException("ffmpeg.exe/ffprobe.exe not found in the archive.");
            }

            var info = new FfmpegInstallInfo(release.AssetName, release.Version, DateTime.UtcNow, hash);
            File.WriteAllText(Path.Combine(staging, "download.json"), JsonSerializer.Serialize(info));

            if (Directory.Exists(targetDirectory))
            {
                Directory.Delete(targetDirectory, true);
            }

            Directory.Move(staging, targetDirectory);
            progress?.Report(1);
            return info;
        }
        finally
        {
            try
            {
                File.Delete(temp);
            }
            catch
            {
                // Temp folder is cleaned by Windows.
            }
        }
    }

    public static FfmpegInstallInfo? ReadInstallInfo(string directory)
    {
        try
        {
            var path = Path.Combine(directory, "download.json");
            return File.Exists(path) ? JsonSerializer.Deserialize<FfmpegInstallInfo>(File.ReadAllText(path)) : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Removes the downloaded copy.</summary>
    public static void Remove(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>Release files are binary downloads (the client's default Accept header is for the API).</summary>
    private static HttpRequestMessage Download(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
        return request;
    }

    private static async Task<string> Sha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    [GeneratedRegex(@"^ffmpeg-n(\d+)\.(\d+)(?:\.\d+)?-latest-win64-gpl-\d+\.\d+\.zip$", RegexOptions.IgnoreCase)]
    private static partial Regex ReleaseAsset();
}
