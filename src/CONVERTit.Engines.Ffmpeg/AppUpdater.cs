using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CONVERTit.Engines.Ffmpeg;

/// <summary>Newest release of CONVERTit on GitHub.</summary>
public sealed record AppRelease(Version Version, string Tag, string PageUrl, string? SetupUrl, string? SetupName, string? PortableUrl,
    string? PortableName, string? Sha256SetupUrl, string? SetupDigest, string? PortableDigest, string Notes, string? Sha256PortableUrl = null);

/// <summary>
/// Update check against the releases of github.com/complicatiion/CONVERTit. Tags are plain versions ("0.0.9",
/// "v0.0.9" is accepted as well). Nothing is sent except the HTTP request; it only runs when the user switched it on.
/// </summary>
public static partial class AppUpdater
{
    public const string Repository = "complicatiion/CONVERTit";

    /// <summary>"0.0.9", "v0.0.9", "0.0.9-beta" → 0.0.9; null for anything else.</summary>
    public static Version? ParseVersion(string? tag)
    {
        var match = VersionTag().Match(tag?.Trim() ?? "");
        return match.Success && Version.TryParse(match.Groups[1].Value, out var v) ? Normalize(v) : null;
    }

    /// <summary>Missing parts count as 0 (0.0.9 == 0.0.9.0).</summary>
    public static Version Normalize(Version v) => new(v.Major, Math.Max(0, v.Minor), Math.Max(0, v.Build), Math.Max(0, v.Revision));

    public static bool IsNewer(Version latest, string currentVersion) =>
        ParseVersion(currentVersion) is { } current && Normalize(latest) > current;

    /// <summary>Reads releases/latest (drafts and pre-releases are skipped by GitHub).</summary>
    public static async Task<AppRelease?> FindLatestAsync(HttpClient client, CancellationToken cancellationToken = default)
    {
        using var response = await client.GetAsync($"https://api.github.com/repos/{Repository}/releases/latest", cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        return Parse(doc.RootElement);
    }

    /// <summary>Release JSON → release (pure, unit-tested).</summary>
    public static AppRelease? Parse(JsonElement release)
    {
        var tag = release.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
        if (ParseVersion(tag) is not { } version)
        {
            return null;
        }

        string? setupUrl = null, setupName = null, portableUrl = null, portableName = null, shaUrl = null, portableSha = null, setupDigest = null, portableDigest = null;
        if (release.TryGetProperty("assets", out var assets))
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? "";
                var url = asset.GetProperty("browser_download_url").GetString() ?? "";
                var digest = asset.TryGetProperty("digest", out var d) && d.ValueKind == JsonValueKind.String ? ModuleInstaller.ParseDigest(d.GetString()) : null;
                if (name.EndsWith("_setup.exe", StringComparison.OrdinalIgnoreCase))
                {
                    (setupUrl, setupName, setupDigest) = (url, name, digest);
                }
                else if (name.EndsWith("_setup.exe.sha256", StringComparison.OrdinalIgnoreCase))
                {
                    shaUrl = url;
                }
                else if (name.EndsWith("_portable.zip.sha256", StringComparison.OrdinalIgnoreCase))
                {
                    portableSha = url;
                }
                else if (name.EndsWith("_portable.zip", StringComparison.OrdinalIgnoreCase))
                {
                    (portableUrl, portableName, portableDigest) = (url, name, digest);
                }
            }
        }

        var page = release.TryGetProperty("html_url", out var h) ? h.GetString() ?? "" : "";
        var notes = release.TryGetProperty("body", out var b) && b.ValueKind == JsonValueKind.String ? b.GetString() ?? "" : "";
        return new AppRelease(version, tag, page, setupUrl, setupName, portableUrl, portableName, shaUrl, setupDigest, portableDigest, notes, portableSha);
    }

    /// <summary>
    /// Downloads a release file into <paramref name="folder"/> and checks it: against the digest GitHub publishes,
    /// otherwise against the published .sha256 file. Without either the file is rejected.
    /// </summary>
    public static async Task<string> DownloadAsync(HttpClient client, string url, string name, string? digest, string? shaFileUrl, string folder,
        IProgress<double>? progress, CancellationToken cancellationToken = default)
    {
        if (digest is null && shaFileUrl is not null)
        {
            var text = await client.GetStringAsync(shaFileUrl, cancellationToken).ConfigureAwait(false);
            digest = ParseShaFile(text);
        }

        if (digest is null)
        {
            throw new InvalidOperationException("No checksum published for " + name + ".");
        }

        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, name);
        var temp = target + ".part";
        using (var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? 0;
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
                    progress?.Report((double)read / total);
                }
            }
        }

        string hash;
        await using (var check = File.OpenRead(temp))
        {
            hash = Convert.ToHexString(await SHA256.HashDataAsync(check, cancellationToken).ConfigureAwait(false)).ToLowerInvariant();
        }

        if (!string.Equals(hash, digest, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(temp);
            throw new InvalidOperationException("Checksum mismatch – download discarded.");
        }

        File.Move(temp, target, overwrite: true);
        return target;
    }

    /// <summary>"abc…  convertit_v0.0.9_setup.exe" or just the hash → hash.</summary>
    public static string? ParseShaFile(string text)
    {
        var first = text.Trim().Split([' ', '\t', '\r', '\n', '*'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return first is { Length: 64 } && first.All(Uri.IsHexDigit) ? first.ToLowerInvariant() : null;
    }

    /// <summary>Starts the downloaded setup (it closes CONVERTit itself and keeps the install mode).</summary>
    public static void RunSetup(string path) =>
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Arguments = "/SP- /CLOSEAPPLICATIONS" });

    [GeneratedRegex(@"^[vV]?(\d+(?:\.\d+){1,3})(?:[-+].*)?$")]
    private static partial Regex VersionTag();
}
