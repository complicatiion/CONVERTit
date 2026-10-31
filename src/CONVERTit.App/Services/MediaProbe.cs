using System.Collections.Concurrent;
using CONVERTit.Core.Media;
using CONVERTit.Engines.Ffmpeg;
using CONVERTit.Engines.Image;

namespace CONVERTit.App.Services;

/// <summary>Cached media details: ffprobe for video/audio, ImageMagick for images (at most 3 probes at a time).</summary>
public sealed class MediaProbe(FfprobeService ffprobe)
{
    private readonly ConcurrentDictionary<string, (DateTime Stamp, long Size, MediaInfo Info)> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _gate = new(3);

    public async Task<MediaInfo> GetAsync(string path, CancellationToken cancellationToken = default)
    {
        var file = new FileInfo(path);
        if (!file.Exists)
        {
            throw new FileNotFoundException(path);
        }

        if (_cache.TryGetValue(path, out var hit) && hit.Stamp == file.LastWriteTimeUtc && hit.Size == file.Length)
        {
            return hit.Info;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var info = MediaTypes.Detect(path) == MediaKind.Image
                ? await Task.Run(() => ImageEngine.Info(path), cancellationToken).ConfigureAwait(false)
                : await ffprobe.ProbeAsync(path, cancellationToken).ConfigureAwait(false);
            _cache[path] = (file.LastWriteTimeUtc, file.Length, info);
            return info;
        }
        finally
        {
            _gate.Release();
        }
    }

    public MediaInfo? Cached(string path) => _cache.TryGetValue(path, out var hit) ? hit.Info : null;
}
