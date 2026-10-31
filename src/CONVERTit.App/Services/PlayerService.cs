using LibVLCSharp.Shared;

namespace CONVERTit.App.Services;

/// <summary>
/// LibVLC for the trim preview. Initialized on first use; when the native libraries cannot be loaded the trim
/// pages fall back to still frames grabbed with FFmpeg.
/// </summary>
public sealed class PlayerService : IDisposable
{
    private readonly object _gate = new();
    private LibVLC? _libVlc;
    private bool _tried;

    /// <summary>Loads LibVLC in the background at start, so opening the first file does not block the window.</summary>
    public void WarmUp() => Task.Run(() => EnsureInitialized());

    public string Error { get; private set; } = "";

    public bool IsAvailable => EnsureInitialized() is not null;

    public string Version => EnsureInitialized()?.Version ?? "";

    public MediaPlayer? CreatePlayer()
    {
        var lib = EnsureInitialized();
        return lib is null ? null : new MediaPlayer(lib) { EnableHardwareDecoding = true };
    }

    /// <param name="audioOnly">Audio pages: no video output (cover art would otherwise open a separate VLC window).</param>
    public Media? CreateMedia(string path, bool audioOnly = false)
    {
        var lib = EnsureInitialized();
        if (lib is null)
        {
            return null;
        }

        return audioOnly ? new Media(lib, path, FromType.FromPath, ":no-video") : new Media(lib, path, FromType.FromPath);
    }

    private LibVLC? EnsureInitialized()
    {
        lock (_gate)
        {
            return InitializeLocked();
        }
    }

    private LibVLC? InitializeLocked()
    {
        if (_tried)
        {
            return _libVlc;
        }

        _tried = true;
        try
        {
            // Fully qualified: inside CONVERTit.* the simple name "Core" resolves to the CONVERTit.Core namespace.
            LibVLCSharp.Shared.Core.Initialize();
            _libVlc = new LibVLC("--no-video-title-show", "--no-snapshot-preview", "--quiet");
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            _libVlc = null;
        }

        return _libVlc;
    }

    public void Dispose()
    {
        _libVlc?.Dispose();
        _libVlc = null;
    }
}
