using System.Security.Cryptography;
using System.Text;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CONVERTit.Core.Ffmpeg;
using CONVERTit.Core.Media;
using CONVERTit.Engines.Ffmpeg;
using CONVERTit.Engines.Image;
using CONVERTit.Engines.Tags;

namespace CONVERTit.App.Services;

/// <summary>
/// Small previews for the file lists. Order: Windows thumbnail cache (fast, the pictures Explorer shows), then the
/// own cache folder, then generating one (image: ImageMagick, video: a frame via FFmpeg, audio: embedded cover).
/// Generated thumbnails are kept as small JPEG files in %LocalAppData%\CONVERTit\thumbs (key: path, size, date);
/// the folder is trimmed to <see cref="CacheLimitBytes"/> (oldest first).
/// </summary>
public sealed class ThumbnailService(FfmpegRunner runner, FfmpegLocator locator)
{
    public const int Size = 96;
    private const long CacheLimitBytes = 200L * 1024 * 1024;

    private readonly SemaphoreSlim _gate = new(2);

    private static string CacheDirectory => Path.Combine(AppEnvironment.DataDirectory, "thumbs");

    /// <summary>Thumbnail of a file, null when none can be made (the row keeps its type icon).</summary>
    public async Task<Bitmap?> GetAsync(string path, MediaKind kind, double durationSeconds, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            // 1. Windows (Explorer's thumbnail cache).
            if (OperatingSystem.IsWindows())
            {
                var shell = await Task.Run(() => Platform.Windows.ShellThumbnail.Get(path, Size), cancellationToken);
                if (shell is not null)
                {
                    return ToBitmap(shell);
                }
            }

            // 2. Own cache.
            var cacheFile = CacheFile(path);
            if (cacheFile is not null && File.Exists(cacheFile))
            {
                return new Bitmap(cacheFile);
            }

            // 3. Generate.
            var bytes = kind switch
            {
                MediaKind.Image => await Task.Run(() => ImageEngine.Thumbnail(path, Size), cancellationToken),
                MediaKind.Video when locator.IsAvailable => await VideoFrameAsync(path, durationSeconds, cancellationToken),
                MediaKind.Audio when TagEngine.Supports(path) => await Task.Run(() => AudioCover(path), cancellationToken),
                _ => null
            };
            if (bytes is not { Length: > 0 })
            {
                return null;
            }

            if (cacheFile is not null)
            {
                try
                {
                    Directory.CreateDirectory(CacheDirectory);
                    await File.WriteAllBytesAsync(cacheFile, bytes, cancellationToken);
                }
                catch (IOException)
                {
                    // Cache is optional.
                }
            }

            return new Bitmap(new MemoryStream(bytes));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<byte[]?> VideoFrameAsync(string path, double duration, CancellationToken cancellationToken)
    {
        var at = duration > 0 ? Math.Min(duration * 0.1, 30) : 1;
        var png = await runner.CaptureBytesAsync(FfmpegCommands.FrameGrab(path, at, Size), TimeSpan.FromSeconds(15), cancellationToken);
        return png is { Length: > 0 } ? ImageEngine.ThumbnailFromBytes(png, Size) : null;
    }

    private static byte[]? AudioCover(string path)
    {
        var cover = TagEngine.Read(path).Cover;
        return cover is { Length: > 0 } ? ImageEngine.ThumbnailFromBytes(cover, Size) : null;
    }

    /// <summary>Cache file name from path, size and modification time (changes when the file changes).</summary>
    private static string? CacheFile(string path)
    {
        try
        {
            var info = new FileInfo(path);
            var key = $"{info.FullName.ToLowerInvariant()}|{info.Length}|{info.LastWriteTimeUtc.Ticks}|{Size}";
            var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(key)));
            return Path.Combine(CacheDirectory, hash + ".jpg");
        }
        catch
        {
            return null;
        }
    }

    private static Bitmap ToBitmap(Platform.Windows.ShellBitmap shell)
    {
        // The pixels are copied by the Bitmap constructor; pinned only for that call.
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(shell.Pixels, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            return new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Premul, handle.AddrOfPinnedObject(),
                new PixelSize(shell.Width, shell.Height), new Vector(96, 96), shell.Stride);
        }
        finally
        {
            handle.Free();
        }
    }

    /// <summary>Keeps the cache below its limit (oldest files first). Runs in the background at start.</summary>
    public static void TrimCache()
    {
        try
        {
            var dir = new DirectoryInfo(CacheDirectory);
            if (!dir.Exists)
            {
                return;
            }

            var files = dir.GetFiles("*.jpg").OrderBy(f => f.LastAccessTimeUtc).ToList();
            var total = files.Sum(f => f.Length);
            foreach (var file in files)
            {
                if (total <= CacheLimitBytes)
                {
                    break;
                }

                total -= file.Length;
                file.Delete();
            }
        }
        catch
        {
            // Not critical.
        }
    }

    /// <summary>Size of the own preview cache in bytes.</summary>
    public static long CacheSize()
    {
        try
        {
            var dir = new DirectoryInfo(CacheDirectory);
            return dir.Exists ? dir.GetFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) : 0;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>Settings › Data: delete all cached thumbnails.</summary>
    public static void ClearCache()
    {
        try
        {
            if (Directory.Exists(CacheDirectory))
            {
                Directory.Delete(CacheDirectory, recursive: true);
            }
        }
        catch
        {
            // Files in use: they are replaced later.
        }
    }
}
