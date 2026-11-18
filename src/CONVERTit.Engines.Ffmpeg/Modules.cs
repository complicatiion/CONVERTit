using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using CONVERTit.Core.Ffmpeg;
using CONVERTit.Core.Jobs;

namespace CONVERTit.Engines.Ffmpeg;

/// <summary>Optional modules. None of them is bundled; each is an opt-in download or placed by hand.</summary>
public enum ModuleId { Ffmpeg, RealEsrgan, Waifu2x, Rife }

/// <summary>Where a module was found.</summary>
public enum ModuleSource { Missing, Custom, User, Shared, AppFolder, SystemPath }

/// <summary>
/// Description of a module: required files (relative to the module folder), license and the pinned upstream
/// release (GitHub repository, tag and asset pattern). Upscaler releases are pinned to a fixed tag so the
/// download is always the same, known build.
/// </summary>
public sealed record ModuleDefinition(
    ModuleId Id,
    string Folder,
    string Name,
    string License,
    string LicenseUrl,
    string Homepage,
    IReadOnlyList<string> RequiredFiles,
    string? Repository,
    string? Tag,
    string? AssetPattern,
    long ApproximateSize)
{
    /// <summary>Main executable (first required file).</summary>
    public string Executable => RequiredFiles[0];

    public bool CanDownload => Repository is not null;
}

public static class ModuleCatalog
{
    private static readonly string Exe = OperatingSystem.IsWindows() ? ".exe" : "";

    public static ModuleDefinition Ffmpeg { get; } = new(ModuleId.Ffmpeg, "ffmpeg", "FFmpeg", "GPL-3.0 (BtbN GPL build)",
        "https://ffmpeg.org/legal.html", "https://ffmpeg.org", ["ffmpeg" + Exe, "ffprobe" + Exe],
        "BtbN/FFmpeg-Builds", "latest", null, 0);

    /// <summary>Real-ESRGAN (xinntao), ncnn/Vulkan build with models; BSD-3-Clause.</summary>
    public static ModuleDefinition RealEsrgan { get; } = new(ModuleId.RealEsrgan, "realesrgan", "Real-ESRGAN (ncnn-vulkan)", "BSD-3-Clause",
        "https://github.com/xinntao/Real-ESRGAN/blob/master/LICENSE", "https://github.com/xinntao/Real-ESRGAN",
        ["realesrgan-ncnn-vulkan" + Exe, "models/realesrgan-x4plus.param", "models/realesrgan-x4plus.bin"],
        "xinntao/Real-ESRGAN", "v0.2.5.0", @"^realesrgan-ncnn-vulkan-\d+-windows\.zip$", 45_000_000);

    /// <summary>waifu2x (nihui, ncnn/Vulkan); MIT.</summary>
    public static ModuleDefinition Waifu2x { get; } = new(ModuleId.Waifu2x, "waifu2x", "waifu2x (ncnn-vulkan)", "MIT",
        "https://github.com/nihui/waifu2x-ncnn-vulkan/blob/master/LICENSE", "https://github.com/nihui/waifu2x-ncnn-vulkan",
        ["waifu2x-ncnn-vulkan" + Exe, "models-cunet/noise0_scale2.0x_model.param"],
        "nihui/waifu2x-ncnn-vulkan", "20220728", @"^waifu2x-ncnn-vulkan-\d+-windows\.zip$", 30_000_000);

    /// <summary>RIFE frame interpolation (nihui, ncnn/Vulkan); MIT.</summary>
    public static ModuleDefinition Rife { get; } = new(ModuleId.Rife, "rife", "RIFE (ncnn-vulkan)", "MIT",
        "https://github.com/nihui/rife-ncnn-vulkan/blob/master/LICENSE", "https://github.com/nihui/rife-ncnn-vulkan",
        ["rife-ncnn-vulkan" + Exe, "rife-v4.6/flownet.param"],
        "nihui/rife-ncnn-vulkan", "20221029", @"^rife-ncnn-vulkan-\d+-windows\.zip$", 200_000_000);

    /// <summary>Optional modules shown as cards on the dependencies page (FFmpeg has its own card).</summary>
    public static IReadOnlyList<ModuleDefinition> Optional { get; } = [RealEsrgan, Waifu2x, Rife];

    public static IReadOnlyList<ModuleDefinition> All { get; } = [Ffmpeg, RealEsrgan, Waifu2x, Rife];

    public static ModuleDefinition Get(ModuleId id) => All.First(m => m.Id == id);
}

/// <summary>
/// Finds modules. Search order: custom folder from the settings, the user's module folder
/// (%LocalAppData%\CONVERTit\modules\&lt;m&gt; or data\modules\&lt;m&gt; when portable), the shared folder for all users
/// (C:\ProgramData\CONVERTit\modules\&lt;m&gt;, written by the installer), a folder next to the app.
/// </summary>
public sealed class ModuleLocator(string userModulesDirectory, string? sharedModulesDirectory, Func<ModuleId, string?> customFolder)
{
    private readonly Dictionary<ModuleId, (string? Folder, ModuleSource Source)> _cache = [];

    public string UserModulesDirectory { get; } = userModulesDirectory;

    public string? SharedModulesDirectory { get; } = sharedModulesDirectory;

    public event Action? Changed;

    /// <summary>Folder the download and "Install from ZIP" write to.</summary>
    public string InstallFolder(ModuleDefinition module) => Path.Combine(UserModulesDirectory, module.Folder);

    public string? SharedFolder(ModuleDefinition module) =>
        SharedModulesDirectory is null ? null : Path.Combine(SharedModulesDirectory, module.Folder);

    public (string? Folder, ModuleSource Source) Find(ModuleDefinition module)
    {
        lock (_cache)
        {
            if (!_cache.TryGetValue(module.Id, out var found))
            {
                found = Search(module);
                _cache[module.Id] = found;
            }

            return found;
        }
    }

    public string? FolderOf(ModuleId id) => Find(ModuleCatalog.Get(id)).Folder;

    public bool IsAvailable(ModuleId id) => FolderOf(id) is not null;

    /// <summary>Full path of the module's executable, or null.</summary>
    public string? ExecutableOf(ModuleId id)
    {
        var module = ModuleCatalog.Get(id);
        return Find(module).Folder is { } folder ? Path.Combine(folder, module.Executable) : null;
    }

    public void Refresh()
    {
        lock (_cache)
        {
            _cache.Clear();
        }

        Changed?.Invoke();
    }

    private (string?, ModuleSource) Search(ModuleDefinition module)
    {
        var custom = customFolder(module.Id)?.Trim().Trim('"');
        if (!string.IsNullOrEmpty(custom) && Complete(module, custom))
        {
            return (custom, ModuleSource.Custom);
        }

        if (Complete(module, InstallFolder(module)))
        {
            return (InstallFolder(module), ModuleSource.User);
        }

        if (SharedFolder(module) is { } shared && Complete(module, shared))
        {
            return (shared, ModuleSource.Shared);
        }

        var appFolder = Path.Combine(AppContext.BaseDirectory, "modules", module.Folder);
        return Complete(module, appFolder) ? (appFolder, ModuleSource.AppFolder) : (null, ModuleSource.Missing);
    }

    /// <summary>All required files present.</summary>
    public static bool Complete(ModuleDefinition module, string? folder)
    {
        if (string.IsNullOrEmpty(folder))
        {
            return false;
        }

        try
        {
            return Directory.Exists(folder) && module.RequiredFiles.All(f => File.Exists(Path.Combine(folder, f)));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Required files missing in a folder (for the "expected files" hint).</summary>
    public static IReadOnlyList<string> Missing(ModuleDefinition module, string folder) =>
        module.RequiredFiles.Where(f => !File.Exists(Path.Combine(folder, f))).ToList();
}

/// <summary>Release asset of a pinned module release.</summary>
public sealed record ModuleRelease(string AssetName, string Url, long Size, string? Sha256, string Tag);

/// <summary>Installed module (module.json in the module folder).</summary>
public sealed record ModuleInstallInfo(string AssetName, string Tag, DateTime InstalledAt, string Sha256, bool Verified);

/// <summary>
/// Download and installation of modules. Downloads come only from the pinned upstream GitHub release; the SHA-256
/// is compared with the digest GitHub publishes for the asset. Archives are extracted into a staging folder,
/// checked for the required files and swapped in.
/// </summary>
public static partial class ModuleInstaller
{
    public static async Task<ModuleRelease> FindReleaseAsync(HttpClient client, ModuleDefinition module, CancellationToken cancellationToken = default)
    {
        if (module.Repository is null || module.Tag is null || module.AssetPattern is null)
        {
            throw new InvalidOperationException(module.Name + " has no download.");
        }

        var api = $"https://api.github.com/repos/{module.Repository}/releases/tags/{module.Tag}";
        using var response = await client.GetAsync(api, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var pattern = new Regex(module.AssetPattern, RegexOptions.IgnoreCase);
        foreach (var asset in doc.RootElement.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? "";
            if (!pattern.IsMatch(name))
            {
                continue;
            }

            var digest = asset.TryGetProperty("digest", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
            return new ModuleRelease(name, asset.GetProperty("browser_download_url").GetString() ?? "",
                asset.TryGetProperty("size", out var s) ? s.GetInt64() : module.ApproximateSize, ParseDigest(digest), module.Tag);
        }

        throw new InvalidOperationException($"No Windows build of {module.Name} found in release {module.Tag}.");
    }

    /// <summary>"sha256:abc…" → "abc…" (other algorithms are ignored).</summary>
    public static string? ParseDigest(string? digest) =>
        digest is { Length: > 7 } && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? digest[7..].Trim().ToLowerInvariant() : null;

    /// <summary>Downloads, verifies and installs a module release into <paramref name="targetDirectory"/>.</summary>
    public static async Task<ModuleInstallInfo> InstallAsync(HttpClient client, ModuleDefinition module, ModuleRelease release,
        string targetDirectory, IProgress<double>? progress, CancellationToken cancellationToken = default)
    {
        var temp = Path.Combine(Path.GetTempPath(), "convertit-" + Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, release.Url);
            request.Headers.Accept.Clear();
            request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/octet-stream"));
            using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
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
                        progress?.Report(Math.Min(0.9, read * 0.9 / total));
                    }
                }
            }

            var hash = await Sha256Async(temp, cancellationToken).ConfigureAwait(false);
            if (release.Sha256 is not null && !string.Equals(release.Sha256, hash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Checksum mismatch – download discarded.");
            }

            var info = new ModuleInstallInfo(release.AssetName, release.Tag, DateTime.UtcNow, hash, release.Sha256 is not null);
            Extract(module, temp, targetDirectory, info);
            progress?.Report(1);
            return info;
        }
        finally
        {
            TryDeleteFile(temp);
        }
    }

    /// <summary>
    /// "Install from ZIP": an archive the user downloaded themselves (same upstream release or a compatible build).
    /// Not verified against a digest; the hash is recorded so it can be compared by hand.
    /// </summary>
    public static ModuleInstallInfo InstallFromZip(ModuleDefinition module, string zipPath, string targetDirectory)
    {
        using var stream = File.OpenRead(zipPath);
        var hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        var info = new ModuleInstallInfo(Path.GetFileName(zipPath), "zip", DateTime.UtcNow, hash, false);
        Extract(module, zipPath, targetDirectory, info);
        return info;
    }

    /// <summary>
    /// Extracts into a staging folder and swaps it in. The archive's single top-level folder is stripped
    /// ("realesrgan-ncnn-vulkan-20220424-windows/…" → "…"). FFmpeg archives keep only bin\ffmpeg/ffprobe and the license.
    /// </summary>
    public static void Extract(ModuleDefinition module, string zipPath, string targetDirectory, ModuleInstallInfo info)
    {
        var staging = targetDirectory + ".new";
        TryDeleteDirectory(staging);
        Directory.CreateDirectory(staging);
        try
        {
            using (var zip = ZipFile.OpenRead(zipPath))
            {
                var files = zip.Entries.Where(e => e.Name.Length > 0).ToList();
                var prefix = CommonRoot(files.Select(e => e.FullName));
                var root = Path.GetFullPath(staging) + Path.DirectorySeparatorChar;
                foreach (var entry in files)
                {
                    var relative = entry.FullName[prefix.Length..].Replace('\\', '/');
                    if (module.Id == ModuleId.Ffmpeg)
                    {
                        // Only the two tools and the license; the archive also has docs, headers and DLL builds.
                        var keep = (relative.StartsWith("bin/", StringComparison.OrdinalIgnoreCase) && entry.Name is "ffmpeg.exe" or "ffprobe.exe")
                            || relative.Equals(entry.Name, StringComparison.OrdinalIgnoreCase) && entry.Name is "ffmpeg.exe" or "ffprobe.exe"
                            || entry.Name.Equals("LICENSE.txt", StringComparison.OrdinalIgnoreCase);
                        if (!keep)
                        {
                            continue;
                        }

                        relative = entry.Name;
                    }

                    var destination = Path.GetFullPath(Path.Combine(staging, relative));
                    if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    {
                        continue; // Zip slip: entries outside the folder are ignored.
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    entry.ExtractToFile(destination, overwrite: true);
                }
            }

            var missing = ModuleLocator.Missing(module, staging);
            if (missing.Count > 0)
            {
                throw new InvalidOperationException("Archive incomplete, missing: " + string.Join(", ", missing));
            }

            File.WriteAllText(Path.Combine(staging, "module.json"), JsonSerializer.Serialize(info));
            TryDeleteDirectory(targetDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(targetDirectory))!);
            Directory.Move(staging, targetDirectory);
        }
        catch
        {
            TryDeleteDirectory(staging);
            throw;
        }
    }

    /// <summary>Common top-level folder of all entries ("" when files lie in the root or in several folders).</summary>
    public static string CommonRoot(IEnumerable<string> entries)
    {
        string? root = null;
        foreach (var entry in entries)
        {
            var slash = entry.Replace('\\', '/').IndexOf('/');
            if (slash < 0)
            {
                return "";
            }

            var first = entry[..(slash + 1)];
            if (root is null)
            {
                root = first;
            }
            else if (!string.Equals(root, first, StringComparison.Ordinal))
            {
                return "";
            }
        }

        return root ?? "";
    }

    public static ModuleInstallInfo? ReadInstallInfo(string directory)
    {
        try
        {
            var path = Path.Combine(directory, "module.json");
            return File.Exists(path) ? JsonSerializer.Deserialize<ModuleInstallInfo>(File.ReadAllText(path)) : null;
        }
        catch
        {
            return null;
        }
    }

    public static void Remove(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }

    private static async Task<string> Sha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // Temp folder is cleaned by Windows.
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch
        {
            // Left behind; replaced on the next install.
        }
    }
}

/// <summary>
/// Runs an ncnn/Vulkan tool (Real-ESRGAN, waifu2x, RIFE). They print "12.34%" lines on stderr for single images;
/// for folders progress comes from counting the output files.
/// </summary>
public static partial class ModuleRunner
{
    /// <summary>Process start hook (job object, EcoQoS) – set by the app.</summary>
    public static Action<Process>? ProcessStarted { get; set; }

    public static async Task RunAsync(string executable, IReadOnlyList<string> args, JobContext context, double from, double to,
        Func<double>? folderProgress = null)
    {
        var psi = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(executable) ?? ""
        };
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        context.SetCommand(Path.GetFileName(executable) + " " + FfmpegCommands.Display(args));
        using var process = new Process { StartInfo = psi };
        process.Start();
        try
        {
            process.PriorityClass = ProcessPriorityClass.BelowNormal;
        }
        catch
        {
            // Already exited.
        }

        ProcessStarted?.Invoke(process);
        await using var registration = context.CancellationToken.Register(() =>
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Gone.
            }
        });

        var errors = new LinkedList<string>();
        async Task Pump(StreamReader reader)
        {
            while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                if (line.Length == 0)
                {
                    continue;
                }

                if (folderProgress is null && Percent(line) is { } percent)
                {
                    context.Report(from + (to - from) * percent / 100.0);
                    continue;
                }

                context.Log(line);
                lock (errors)
                {
                    errors.AddLast(line);
                    if (errors.Count > 6)
                    {
                        errors.RemoveFirst();
                    }
                }
            }
        }

        var pumps = Task.WhenAll(Pump(process.StandardError), Pump(process.StandardOutput));
        if (folderProgress is not null)
        {
            while (!process.HasExited)
            {
                context.Report(from + (to - from) * Math.Clamp(folderProgress(), 0, 1));
                await Task.WhenAny(process.WaitForExitAsync(), Task.Delay(500)).ConfigureAwait(false);
            }
        }

        await process.WaitForExitAsync().ConfigureAwait(false);
        await pumps.ConfigureAwait(false);
        context.CancellationToken.ThrowIfCancellationRequested();
        if (process.ExitCode != 0)
        {
            string message;
            lock (errors)
            {
                message = errors.Count > 0 ? string.Join(Environment.NewLine, errors) : $"{Path.GetFileName(executable)} exit code {process.ExitCode}";
            }

            throw new FfmpegException(message, process.ExitCode);
        }

        context.Report(to, null, force: true);
    }

    /// <summary>"12.50%" → 12.5.</summary>
    public static double? Percent(string line)
    {
        var match = PercentLine().Match(line);
        return match.Success && double.TryParse(match.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    [GeneratedRegex(@"^\s*(\d{1,3}(?:[.,]\d+)?)%\s*$")]
    private static partial Regex PercentLine();
}

/// <summary>Command lines of the ncnn tools (pure, unit-tested).</summary>
public static class ModuleArgs
{
    /// <summary>
    /// Module, arguments and the factor the tool really applies. Real-ESRGAN x4plus models only scale ×4 (a smaller
    /// factor is reached by scaling the result down); waifu2x has no ×3 (×4 and down).
    /// </summary>
    public static (ModuleId Module, List<string> Args, int NativeScale) Upscale(Core.Options.UpscaleOptions o, string input, string output, bool folder)
    {
        var scale = Math.Clamp(o.Scale, 2, 4);
        List<string> a;
        ModuleId module;
        int native;
        if (o.Engine == Core.Options.UpscaleEngine.Waifu2x)
        {
            module = ModuleId.Waifu2x;
            native = scale == 3 ? 4 : scale;
            a = ["-i", input, "-o", output, "-n", Math.Clamp(o.Noise, -1, 3).ToString(CultureInfo.InvariantCulture),
                "-s", native.ToString(CultureInfo.InvariantCulture), "-m", "models-cunet"];
        }
        else
        {
            module = ModuleId.RealEsrgan;
            var model = o.Engine switch
            {
                Core.Options.UpscaleEngine.RealEsrganAnime => "realesrgan-x4plus-anime",
                Core.Options.UpscaleEngine.RealEsrganVideo => "realesr-animevideov3",
                _ => "realesrgan-x4plus"
            };
            native = o.Engine == Core.Options.UpscaleEngine.RealEsrganVideo ? scale : 4;
            a = ["-i", input, "-o", output, "-n", model, "-s", native.ToString(CultureInfo.InvariantCulture)];
        }

        if (o.TileSize > 0)
        {
            a.AddRange(["-t", o.TileSize.ToString(CultureInfo.InvariantCulture)]);
        }

        if (o.Gpu >= 0)
        {
            a.AddRange(["-g", o.Gpu.ToString(CultureInfo.InvariantCulture)]);
        }

        a.AddRange(["-f", "png"]);
        if (folder)
        {
            // Folder mode: several frames in flight (load:process:save threads).
            a.AddRange(["-j", "2:2:2"]);
        }

        return (module, a, native);
    }

    /// <summary>RIFE: folder of frames → folder with <paramref name="targetCount"/> frames (v4 models allow any count).</summary>
    public static List<string> Rife(string inputFolder, string outputFolder, int targetCount, int gpu = -1)
    {
        var a = new List<string> { "-i", inputFolder, "-o", outputFolder, "-m", "rife-v4.6", "-n", targetCount.ToString(CultureInfo.InvariantCulture),
            "-f", "%08d.png", "-j", "2:2:2" };
        if (gpu >= 0)
        {
            a.AddRange(["-g", gpu.ToString(CultureInfo.InvariantCulture)]);
        }

        return a;
    }
}
