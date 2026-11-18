using System.Collections.ObjectModel;
using System.Text.Json;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CONVERTit.App.Localization;
using CONVERTit.Core.Estimation;
using CONVERTit.Core.Ffmpeg;
using CONVERTit.Core.Jobs;
using CONVERTit.Core.Media;
using CONVERTit.Core.Options;
using CONVERTit.Core.Storage;
using LibVLCSharp.Shared;

namespace CONVERTit.App.ViewModels;

/// <summary>
/// Video › Trim &amp; Loop and Audio › Trim &amp; Loop: player (LibVLC) with timeline, start/end marks, fades,
/// loop (count, target length, boomerang) and output in the same format or with the Convert page's settings.
/// </summary>
public sealed partial class TrimViewModel : ConvertPageViewModel, IDisposable
{
    private readonly MediaKind _kind;
    private MediaPlayer? _player;
    private Media? _media;
    private bool _fromPlayer;
    private bool _pauseOnPlaying;
    private bool _previewing;
    private CancellationTokenSource? _frameCts;
    private CancellationTokenSource? _thumbsCts;
    private DispatcherTimer? _frameTimer;
    private BusyActivity? _loadActivity;
    private bool _waitPlaying;
    private bool _waitThumbs;

    public TrimViewModel(MainViewModel main, MediaKind kind)
        : base(main, new HashSet<MediaKind> { kind }, kind, singleFile: true)
    {
        _kind = kind;
        Sources.SelectionChanged += item => Load(item);
        Trim.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(TrimOptions.Start) or nameof(TrimOptions.End))
            {
                OnPropertyChanged(nameof(StartText));
                OnPropertyChanged(nameof(EndText));
            }

            OnPropertyChanged(nameof(ResultText));
            OnPropertyChanged(nameof(CanUseFast));
            OnPropertyChanged(nameof(ShowFastHint));
            OnPropertyChanged(nameof(ShowLoopCount));
            OnPropertyChanged(nameof(ShowLoopTarget));
            OnPropertyChanged(nameof(ShowBoomerangHint));
            OnPropertyChanged(nameof(IsConvertOutput));
            OnPropertyChanged(nameof(IsAnimationOutput));
            OnPropertyChanged(nameof(AnimationEstimate));
        };
    }

    public override string TitleKey => "NavTrim";

    public override string IconKey => "IconTrim";

    public override string StateKey => _kind == MediaKind.Video ? "VideoTrim" : "AudioTrim";

    public bool IsVideo => _kind == MediaKind.Video;

    public bool IsAudio => _kind == MediaKind.Audio;

    public TrimOptions Trim { get; } = new();

    public TrimMode[] Modes => Choices.TrimModes;

    public LoopMode[] LoopModes => Choices.LoopModes;

    /// <summary>GIF and animated WebP only for videos.</summary>
    public TrimOutput[] Outputs => IsVideo ? Choices.TrimOutputs : [TrimOutput.SameFormat, TrimOutput.Convert];

    public bool IsAnimationOutput => Trim.Output is TrimOutput.Gif or TrimOutput.AnimatedWebP;

    public int[] AnimationWidths { get; } = [240, 320, 400, 480, 640, 800, 0];

    public int[] AnimationRates { get; } = [8, 10, 12, 15, 20, 24, 30];

    /// <summary>"≈ 6.4 MB" for GIF/WebP; warns when the file gets large.</summary>
    public string AnimationEstimate
    {
        get
        {
            var info = Sources.SelectedItem?.Info;
            var video = info?.MainVideo;
            if (!IsAnimationOutput || video is null || Duration <= 0)
            {
                return "";
            }

            var (sw, sh) = video.DisplaySize;
            var width = Trim.AnimationWidth > 0 ? Math.Min(Trim.AnimationWidth, sw) : sw;
            var height = sw > 0 ? width * sh / (double)sw : sh;
            var seconds = ResultSeconds * (Trim.Loop == LoopMode.Boomerang ? 2 : 1);
            var bytes = SizeEstimator.Animation(width, height, Trim.AnimationFps, seconds, Trim.Output == TrimOutput.Gif);
            return Loc.F(bytes > 15_000_000 ? "AnimationEstimateLarge" : "AnimationEstimate", Units.Bytes(bytes));
        }
    }

    // ---------------------------------------------------------------- Multi-cut

    /// <summary>Segments of the multi-cut (empty = start/end marks apply).</summary>
    public IReadOnlyList<TrimSegment> Segments => Trim.Segments;

    public bool HasCuts => Trim.Segments.Count > 0;

    /// <summary>Redraw trigger for the timeline (the list instance stays the same).</summary>
    [ObservableProperty] private int _segmentsVersion;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedSegment), nameof(SelectedSegmentRemoved))]
    private int _selectedSegment = -1;

    public bool HasSelectedSegment => SelectedSegment >= 0 && SelectedSegment < Trim.Segments.Count;

    public bool SelectedSegmentRemoved => HasSelectedSegment && Trim.Segments[SelectedSegment].Removed;

    /// <summary>"3 segments · 2 kept · 00:45.200".</summary>
    public string CutsText => HasCuts
        ? Loc.F("CutsSummary", Trim.Segments.Count, Trim.Segments.Count(s => !s.Removed), Timecode.Format(CutList.KeptLength(Trim.Segments, Duration)))
        : "";

    /// <summary>Length of the result before loops (multi-cut, range or whole file).</summary>
    private double ResultSeconds
    {
        get
        {
            if (HasCuts)
            {
                return CutList.KeptLength(Trim.Segments, Duration);
            }

            var start = Trim.TrimEnabled ? Trim.Start : 0;
            var end = Trim.TrimEnabled && Trim.End > start ? Trim.End : Duration;
            return Math.Max(0, end - start);
        }
    }

    private void SegmentsChanged()
    {
        SegmentsVersion++;
        if (SelectedSegment >= Trim.Segments.Count)
        {
            SelectedSegment = Trim.Segments.Count - 1;
        }

        foreach (var name in new[] { nameof(HasCuts), nameof(CutsText), nameof(ResultText), nameof(AnimationEstimate), nameof(HasSelectedSegment), nameof(SelectedSegmentRemoved) })
        {
            OnPropertyChanged(name);
        }
    }

    /// <summary>Cut at the playback position (S / C, scissors button, right-click).</summary>
    [RelayCommand]
    private void CutHere() => CutAt(Position);

    public void CutAt(double time)
    {
        if (Duration <= 0 || !CutList.Split(Trim.Segments, time, Duration))
        {
            return;
        }

        SelectedSegment = CutList.IndexAt(Trim.Segments, time);
        SegmentsChanged();
    }

    /// <summary>Removes (or restores) the selected segment (Del, trash button).</summary>
    [RelayCommand]
    private void ToggleSegment()
    {
        if (!HasSelectedSegment)
        {
            return;
        }

        var segment = Trim.Segments[SelectedSegment];
        segment.Removed = !segment.Removed;
        SegmentsChanged();
    }

    [RelayCommand]
    private void KeepOnlySegment()
    {
        if (HasSelectedSegment)
        {
            CutList.KeepOnly(Trim.Segments, SelectedSegment);
            SegmentsChanged();
        }
    }

    /// <summary>Removes the cut line closest to the time (joins its two segments).</summary>
    public void RemoveCutNear(double time)
    {
        if (Trim.Segments.Count < 2)
        {
            return;
        }

        var cut = Enumerable.Range(1, Trim.Segments.Count - 1).MinBy(i => Math.Abs(Trim.Segments[i].Start - time));
        CutList.Join(Trim.Segments, cut);
        SegmentsChanged();
    }

    [RelayCommand]
    private void ClearCuts()
    {
        Trim.Segments.Clear();
        SelectedSegment = -1;
        SegmentsChanged();
    }

    /// <summary>A cut line was dragged on the timeline.</summary>
    public void MoveCut(int cut, double time)
    {
        Position = CutList.MoveCut(Trim.Segments, cut, time);
        SegmentsChanged();
    }

    /// <summary>Selects the segment under the playback position (for Del after seeking).</summary>
    partial void OnSegmentsVersionChanged(int value) => OnPropertyChanged(nameof(Segments));

    /// <summary>LibVLC player (null when the native libraries are not available).</summary>
    public MediaPlayer? Player => _player;

    public bool HasPlayer => _player is not null;

    /// <summary>Video surface only with an open file: without one the area shows the card background, not black.</summary>
    public bool ShowVideoSurface => IsVideo && HasPlayer && HasSource;

    /// <summary>Display aspect ratio of the open video (sizes the surface so no black bars appear around it).</summary>
    public double VideoAspect =>
        Sources.SelectedItem?.Info?.MainVideo is { } v && v.DisplaySize is var (w, h) && w > 0 && h > 0 ? w / (double)h : 16 / 9.0;

    /// <summary>Start/end fields and "Play range" folded under the player controls.</summary>
    [ObservableProperty] private bool _showRangeOptions;

    /// <summary>Short play/pause symbol over the video after a click on it.</summary>
    [ObservableProperty] private bool _showClickIndicator;

    /// <summary>Still frame when no player is available (FFmpeg frame grab).</summary>
    public bool ShowStillFrame => IsVideo && !HasPlayer;

    [ObservableProperty] private double _position;
    [ObservableProperty] private double _duration;
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private Bitmap? _frame;

    /// <summary>Opening a file: probing, first frame and thumbnails (large files take a few seconds).</summary>
    [ObservableProperty] private bool _isLoadingMedia;

    public ObservableCollection<Bitmap> Thumbnails { get; } = [];

    public bool HasSource => Sources.SelectedItem is not null;

    /// <summary>Audio: big position display, only with an open file.</summary>
    public bool ShowAudioPosition => IsAudio && HasSource;

    public string PositionText => Timecode.Format(Position);

    public string DurationText => Timecode.Format(Duration);

    public string PlayerError => Main.Player.Error;

    public string StartText
    {
        get => Timecode.Format(Trim.Start);
        set
        {
            if (Timecode.TryParse(value, out var s))
            {
                Trim.Start = Math.Clamp(s, 0, Duration > 0 ? Duration : s);
            }
        }
    }

    public string EndText
    {
        get => Trim.End > 0 ? Timecode.Format(Trim.End) : "";
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                Trim.End = 0;
            }
            else if (Timecode.TryParse(value, out var s))
            {
                Trim.End = Duration > 0 ? Math.Min(s, Duration) : s;
            }
        }
    }

    /// <summary>Stream copy is only possible without fades/boomerang and in the same format.</summary>
    public bool CanUseFast => Trim.Output == TrimOutput.SameFormat && Trim.FadeIn <= 0 && Trim.FadeOut <= 0 && Trim.Loop != LoopMode.Boomerang;

    public bool ShowFastHint => Trim.Mode == TrimMode.Fast && !CanUseFast;

    public bool ShowLoopCount => Trim.Loop == LoopMode.Count;

    public bool ShowLoopTarget => Trim.Loop == LoopMode.Duration;

    public bool ShowBoomerangHint => Trim.Loop == LoopMode.Boomerang;

    public bool IsConvertOutput => Trim.Output == TrimOutput.Convert;

    public string ConvertHint => Loc.T(IsVideo ? "HintTrimConvertVideo" : "HintTrimConvertAudio");

    /// <summary>"Result: 00:12.500 × 3 = 00:37.500".</summary>
    public string ResultText
    {
        get
        {
            if (Duration <= 0)
            {
                return "";
            }

            var segment = ResultSeconds;
            return Trim.Loop switch
            {
                LoopMode.Count => Loc.F("TrimResultLoop", Timecode.Format(segment), Math.Max(1, Trim.LoopCount), Timecode.Format(segment * Math.Max(1, Trim.LoopCount))),
                LoopMode.Duration => Loc.F("TrimResult", Timecode.Format(Trim.LoopTargetSeconds)),
                LoopMode.Boomerang => Loc.F("TrimResultLoop", Timecode.Format(segment), 2, Timecode.Format(segment * 2)),
                _ => Loc.F("TrimResult", Timecode.Format(segment))
            };
        }
    }

    partial void OnPositionChanged(double value)
    {
        OnPropertyChanged(nameof(PositionText));
        if (_fromPlayer)
        {
            return;
        }

        // Set by the timeline: seek the player or grab a still frame.
        if (_player is not null && _player.IsSeekable)
        {
            _player.Time = (long)(value * 1000);
        }
        else if (IsVideo)
        {
            ScheduleFrame();
        }
    }

    partial void OnDurationChanged(double value)
    {
        OnPropertyChanged(nameof(DurationText));
        OnPropertyChanged(nameof(ResultText));
    }

    [RelayCommand]
    private void PlayPause()
    {
        if (_player is null || _media is null)
        {
            return;
        }

        _previewing = false;
        if (_player.IsPlaying)
        {
            _player.SetPause(true);
        }
        else
        {
            _player.Play();
        }
    }

    [RelayCommand]
    private void SetStart()
    {
        Trim.TrimEnabled = true;
        Trim.Start = Position;
        if (Trim.End > 0 && Trim.End <= Trim.Start)
        {
            Trim.End = 0;
        }
    }

    [RelayCommand]
    private void SetEnd()
    {
        Trim.TrimEnabled = true;
        Trim.End = Position > Trim.Start ? Position : 0;
    }

    [RelayCommand]
    private void GoToStart() => Position = Trim.Start;

    [RelayCommand]
    private void GoToEnd() => Position = Trim.End > 0 ? Trim.End : Duration;

    [RelayCommand]
    private void Step(string? seconds)
    {
        if (double.TryParse(seconds, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var s))
        {
            // Max must not drop below 0 (unknown duration + step back at 0 would throw in Math.Clamp).
            Position = Math.Clamp(Position + s, 0, Duration > 0 ? Duration : Math.Max(0, Position + s));
        }
    }

    /// <summary>Plays the selected range once.</summary>
    [RelayCommand]
    private void PreviewRange()
    {
        if (_player is null || _media is null)
        {
            return;
        }

        Position = Trim.TrimEnabled ? Trim.Start : 0;
        _previewing = true;
        _player.Play();
    }

    [RelayCommand]
    private void ResetTrim()
    {
        Trim.Segments.Clear();
        SelectedSegment = -1;
        SegmentsChanged();
        Trim.Start = 0;
        Trim.End = 0;
        Trim.FadeIn = 0;
        Trim.FadeOut = 0;
        Trim.Loop = LoopMode.Off;
    }

    protected override bool ExtraCanStart() => Sources.SelectedItem?.Info is not null;

    protected override Task<List<ConversionJob>> PlanAsync()
    {
        var item = Sources.SelectedItem;
        if (item?.Info is null)
        {
            return Task.FromResult(new List<ConversionJob>());
        }

        if (Trim.Mode == TrimMode.Fast && !CanUseFast)
        {
            Trim.Mode = TrimMode.Exact;
        }

        return Task.FromResult(Main.Conversion.Trim(item.ToRef(), _kind, item.Info, Trim, Main.VideoConvert.Video.Options,
            Main.AudioConvert.Audio.Options, Output.Options));
    }

    // ---------------------------------------------------------------- Player

    private void Load(SourceItem? item)
    {
        StopPlayer();
        EndLoading();
        Thumbnails.Clear();
        Frame = null;
        // Cuts belong to one file.
        Trim.Segments.Clear();
        SelectedSegment = -1;
        SegmentsChanged();
        OnPropertyChanged(nameof(HasSource));
        OnPropertyChanged(nameof(ShowAudioPosition));
        OnPropertyChanged(nameof(ShowVideoSurface));
        OnPropertyChanged(nameof(VideoAspect));
        StartCommand.NotifyCanExecuteChanged();
        if (item is null)
        {
            Duration = 0;
            return;
        }

        IsLoadingMedia = true;
        _loadActivity = Main.BeginActivity(Loc.F("ActivityOpeningMedia", item.Name));
        _waitThumbs = IsVideo && item.Info is not null && Main.Locator.IsAvailable;

        // The heavy part (player, media) runs after the indicator has been drawn.
        Dispatcher.UIThread.Post(() =>
        {
            if (ReferenceEquals(item, Sources.SelectedItem))
            {
                OpenMedia(item);
            }
        }, DispatcherPriority.Background);
    }

    private void OpenMedia(SourceItem item)
    {
        _fromPlayer = true;
        Position = 0;
        _fromPlayer = false;
        Duration = item.Info?.DurationSeconds ?? 0;
        if (Trim.End > Duration && Duration > 0)
        {
            Trim.End = 0;
        }

        if (Trim.Start >= Duration && Duration > 0)
        {
            Trim.Start = 0;
        }

        EnsurePlayer();
        if (_player is not null)
        {
            _media = Main.Player.CreateMedia(item.Path, audioOnly: IsAudio);
            if (_media is not null)
            {
                _waitPlaying = true;
                _pauseOnPlaying = true;
                var media = _media;
                // After the next layout pass: the video surface must have its native window first,
                // otherwise LibVLC opens a window of its own.
                Dispatcher.UIThread.Post(() =>
                {
                    if (ReferenceEquals(media, _media))
                    {
                        _player?.Play(media);
                    }
                }, DispatcherPriority.Background);
            }
        }
        else if (IsVideo)
        {
            ScheduleFrame();
        }

        if (_waitThumbs)
        {
            _ = LoadThumbnailsAsync(item);
        }

        CheckLoaded();
    }

    /// <summary>Unload: removes the open file from the page (player stops, marks are reset).</summary>
    [RelayCommand]
    private void Unload()
    {
        Sources.ClearCommand.Execute(null);
        Trim.Start = 0;
        Trim.End = 0;
    }

    /// <summary>Loading ends when the file is probed, the player shows its first frame and the thumbnails are there.</summary>
    private void CheckLoaded()
    {
        OnPropertyChanged(nameof(VideoAspect));
        var item = Sources.SelectedItem;
        if (item is null || (!item.IsProbing && !_waitPlaying && !_waitThumbs))
        {
            EndLoading();
        }
    }

    private void EndLoading()
    {
        _waitPlaying = false;
        _waitThumbs = false;
        _loadActivity?.Dispose();
        _loadActivity = null;
        IsLoadingMedia = false;
    }

    private void EnsurePlayer()
    {
        if (_player is not null)
        {
            return;
        }

        _player = Main.Player.CreatePlayer();
        if (_player is null)
        {
            OnPropertyChanged(nameof(PlayerError));
            return;
        }

        _player.TimeChanged += (_, e) => Dispatcher.UIThread.Post(() =>
        {
            _fromPlayer = true;
            Position = e.Time / 1000d;
            _fromPlayer = false;
            if (_previewing && Trim.End > 0 && Position >= Trim.End)
            {
                _previewing = false;
                _player?.SetPause(true);
            }
        });
        _player.LengthChanged += (_, e) => Dispatcher.UIThread.Post(() =>
        {
            if (e.Length > 0 && Duration <= 0)
            {
                Duration = e.Length / 1000d;
            }
        });
        _player.Playing += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            IsPlaying = true;
            if (_waitPlaying)
            {
                _waitPlaying = false;
                CheckLoaded();
            }

            if (_pauseOnPlaying)
            {
                // First frame visible, then wait for the user.
                _pauseOnPlaying = false;
                _player?.SetPause(true);
            }
        });
        _player.EncounteredError += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            _waitPlaying = false;
            CheckLoaded();
        });
        _player.Paused += (_, _) => Dispatcher.UIThread.Post(() => IsPlaying = false);
        _player.Stopped += (_, _) => Dispatcher.UIThread.Post(() => IsPlaying = false);
        _player.EndReached += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            IsPlaying = false;
            _previewing = false;
            // Reload so the player can start again (LibVLC ends in the Ended state).
            var media = _media;
            if (_player is not null && media is not null)
            {
                _pauseOnPlaying = true;
                ThreadPool.QueueUserWorkItem(_ => _player?.Play(media));
            }
        });

        OnPropertyChanged(nameof(Player));
        OnPropertyChanged(nameof(HasPlayer));
        OnPropertyChanged(nameof(ShowVideoSurface));
        OnPropertyChanged(nameof(ShowStillFrame));
    }

    private void StopPlayer()
    {
        _previewing = false;
        if (_player is not null)
        {
            var player = _player;
            // Stop must not run on a LibVLC event thread; from the UI thread it is safe.
            player.Stop();
        }

        _media?.Dispose();
        _media = null;
        IsPlaying = false;
    }

    private void ScheduleFrame()
    {
        _frameTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) =>
        {
            _frameTimer?.Stop();
            _ = GrabFrameAsync();
        });
        _frameTimer.Stop();
        _frameTimer.Start();
    }

    private async Task GrabFrameAsync()
    {
        var item = Sources.SelectedItem;
        if (item is null || !Main.Locator.IsAvailable)
        {
            return;
        }

        _frameCts?.Cancel();
        var cts = _frameCts = new CancellationTokenSource();
        try
        {
            var bytes = await Main.Runner.CaptureBytesAsync(FfmpegCommands.FrameGrab(item.Path, Position, 360), TimeSpan.FromSeconds(15), cts.Token);
            if (!cts.IsCancellationRequested && bytes is { Length: > 0 })
            {
                Frame = new Bitmap(new MemoryStream(bytes));
            }
        }
        catch
        {
            // Frame not available at this position.
        }
    }

    private async Task LoadThumbnailsAsync(SourceItem item)
    {
        _thumbsCts?.Cancel();
        var cts = _thumbsCts = new CancellationTokenSource();
        var duration = item.Info?.DurationSeconds ?? 0;
        if (duration <= 0 || !Main.Locator.IsAvailable)
        {
            _waitThumbs = false;
            CheckLoaded();
            return;
        }

        const int count = 10;
        for (var i = 0; i < count && !cts.IsCancellationRequested; i++)
        {
            var at = duration * (i + 0.5) / count;
            try
            {
                var bytes = await Main.Runner.CaptureBytesAsync(FfmpegCommands.FrameGrab(item.Path, at, 64), TimeSpan.FromSeconds(10), cts.Token);
                if (!cts.IsCancellationRequested && bytes is { Length: > 0 } && ReferenceEquals(item, Sources.SelectedItem))
                {
                    Thumbnails.Add(new Bitmap(new MemoryStream(bytes)));
                }
            }
            catch
            {
                break;
            }
        }

        if (!cts.IsCancellationRequested && ReferenceEquals(item, Sources.SelectedItem))
        {
            _waitThumbs = false;
            CheckLoaded();
        }
    }

    public override void SaveState(Dictionary<string, JsonElement> pages) => Save(pages, StateKey, Trim, Output.Options);

    public override void LoadState(Dictionary<string, JsonElement> pages)
    {
        Load(pages, StateKey, Trim, Output.Options);
        // Positions belong to a file; only the settings are restored.
        Trim.Start = 0;
        Trim.End = 0;
    }

    public void Dispose()
    {
        EndLoading();
        StopPlayer();
        _player?.Dispose();
        _player = null;
    }
}
