using Avalonia.Threading;
using CONVERTit.App.ViewModels;
using LibVLCSharp.Shared;

namespace CONVERTit.App.Services;

/// <summary>
/// Mini player of the audio file lists (Tags, Audio › Convert, Game audio): one file at a time, play/pause and
/// seeking only. Uses LibVLC without video output. Progress is written to the playing <see cref="SourceItem"/>.
/// </summary>
public sealed class AudioPreview(PlayerService players) : IDisposable
{
    private MediaPlayer? _player;
    private Media? _media;

    /// <summary>The row that is loaded in the player (playing or paused).</summary>
    public SourceItem? Current { get; private set; }

    /// <summary>Play/pause for a row; another row stops the current one first.</summary>
    public void Toggle(SourceItem item)
    {
        if (ReferenceEquals(item, Current) && _player is not null)
        {
            if (_player.IsPlaying)
            {
                _player.SetPause(true);
            }
            else
            {
                _player.SetPause(false);
                if (!_player.IsPlaying && _media is not null)
                {
                    // Ended: start again from the beginning.
                    var media = _media;
                    ThreadPool.QueueUserWorkItem(_ => _player?.Play(media));
                }
            }

            return;
        }

        Stop();
        var player = EnsurePlayer();
        if (player is null)
        {
            return;
        }

        _media = players.CreateMedia(item.Path, audioOnly: true);
        if (_media is null)
        {
            return;
        }

        Current = item;
        item.IsPlayerActive = true;
        item.SetPlayPosition(0, item.Duration);
        item.Preview = this;
        player.Play(_media);
    }

    /// <summary>Seek within the current row (seconds).</summary>
    public void Seek(SourceItem item, double seconds)
    {
        if (!ReferenceEquals(item, Current) || _player is null || _player.Length <= 0)
        {
            return;
        }

        _player.Time = (long)(Math.Clamp(seconds, 0, _player.Length / 1000d) * 1000);
    }

    /// <summary>Stops when this file is affected (removed, tags written, page left).</summary>
    public void StopIf(Func<SourceItem, bool> predicate)
    {
        if (Current is { } item && predicate(item))
        {
            Stop();
        }
    }

    public void Stop()
    {
        if (_player is not null && (_player.IsPlaying || _player.State is VLCState.Paused or VLCState.Opening or VLCState.Buffering))
        {
            _player.Stop();
        }

        _media?.Dispose();
        _media = null;
        if (Current is { } item)
        {
            item.IsPlaying = false;
            item.IsPlayerActive = false;
            item.Preview = null;
        }

        Current = null;
    }

    private MediaPlayer? EnsurePlayer()
    {
        if (_player is not null)
        {
            return _player;
        }

        _player = players.CreatePlayer();
        if (_player is null)
        {
            return null;
        }

        _player.TimeChanged += (_, e) => Post(item =>
        {
            var length = _player?.Length ?? 0;
            item.SetPlayPosition(e.Time / 1000d, length > 0 ? length / 1000d : item.PlayDuration);
        });
        _player.LengthChanged += (_, e) => Post(item => item.SetPlayPosition(item.PlayPosition, e.Length / 1000d));
        _player.Playing += (_, _) => Post(item => item.IsPlaying = true);
        _player.Paused += (_, _) => Post(item => item.IsPlaying = false);
        _player.Stopped += (_, _) => Post(item => item.IsPlaying = false);
        _player.EndReached += (_, _) => Post(item =>
        {
            item.IsPlaying = false;
            item.SetPlayPosition(0, item.PlayDuration);
        });
        return _player;
    }

    /// <summary>LibVLC events arrive on its own threads: update the row on the UI thread.</summary>
    private void Post(Action<SourceItem> update)
    {
        var item = Current;
        if (item is null)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (ReferenceEquals(item, Current))
            {
                update(item);
            }
        });
    }

    public void Dispose()
    {
        Stop();
        _player?.Dispose();
        _player = null;
    }
}
