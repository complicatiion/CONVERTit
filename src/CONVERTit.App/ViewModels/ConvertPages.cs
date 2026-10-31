using System.Collections.ObjectModel;
using System.Text.Json;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CONVERTit.App.Localization;
using CONVERTit.Core.Catalog;
using CONVERTit.Core.Estimation;
using CONVERTit.Core.Jobs;
using CONVERTit.Core.Media;
using CONVERTit.Core.Options;
using CONVERTit.Core.Storage;
using CONVERTit.Engines.Image;

namespace CONVERTit.App.ViewModels;

// ==================================================================== Quick Convert

/// <summary>Drop anything: videos, audio and images are converted with the settings of their Convert pages.</summary>
public sealed partial class QuickConvertViewModel : ConvertPageViewModel
{
    public QuickConvertViewModel(MainViewModel main)
        : base(main, new HashSet<MediaKind> { MediaKind.Video, MediaKind.Audio, MediaKind.Image }, MediaKind.Unknown)
    {
        Sources.Changed += () =>
        {
            OnPropertyChanged(nameof(VideoCount));
            OnPropertyChanged(nameof(AudioCount));
            OnPropertyChanged(nameof(ImageCount));
        };
    }

    public override string TitleKey => "NavQuick";

    public override string IconKey => "IconQuick";

    /// <summary>Images work without FFmpeg; video/audio files are checked per job.</summary>
    public override bool NeedsFfmpeg => false;

    [ObservableProperty] private VideoContainer _videoTarget = VideoContainer.Mp4;
    [ObservableProperty] private AudioFormat _audioTarget = AudioFormat.Mp3;
    [ObservableProperty] private ImageFormat _imageTarget = ImageFormat.WebP;

    public VideoContainer[] VideoTargets => Choices.Containers;

    public IReadOnlyList<AudioFormat> AudioTargets => Main.AudioConvert.Audio.Formats;

    public IReadOnlyList<ImageFormat> ImageTargets { get; } = Enum.GetValues<ImageFormat>().Where(f => f != ImageFormat.Keep && ImageEngine.CanWrite(f)).ToList();

    public int VideoCount => Sources.Items.Count(i => i.Kind == MediaKind.Video);

    public int AudioCount => Sources.Items.Count(i => i.Kind == MediaKind.Audio);

    public int ImageCount => Sources.Items.Count(i => i.Kind == MediaKind.Image);

    protected override Task<List<ConversionJob>> PlanAsync()
    {
        var jobs = new List<ConversionJob>();
        var skip = Sources.Folder.SkipSameFormat;

        var videos = Sources.ActiveItems.Where(i => i.Kind == MediaKind.Video).Select(i => i.ToRef()).ToList();
        if (videos.Count > 0)
        {
            var video = JsonFile.Clone(Main.VideoConvert.Video.Options);
            video.Container = VideoTarget;
            if (!FormatCatalog.CodecsFor(VideoTarget).Contains(video.Codec))
            {
                video.Codec = FormatCatalog.CodecsFor(VideoTarget)[0];
                video.Acceleration = Acceleration.Off;
            }

            if (!FormatCatalog.AudioModesFor(VideoTarget).Contains(video.Audio))
            {
                video.Audio = VideoAudioMode.Auto;
            }

            jobs.AddRange(Main.Conversion.VideoConvert(videos, video, Output.Options, skip));
        }

        var audio = Sources.ActiveItems.Where(i => i.Kind == MediaKind.Audio).Select(i => i.ToRef()).ToList();
        if (audio.Count > 0)
        {
            var options = JsonFile.Clone(Main.AudioConvert.Audio.Options);
            if (options.Format != AudioTarget)
            {
                options.Format = AudioTarget;
                var steps = FormatCatalog.BitratesFor(AudioTarget);
                if (steps.Count > 0 && !steps.Contains(options.BitrateKbps))
                {
                    options.BitrateKbps = AudioTarget == AudioFormat.Opus ? 160 : 320;
                }
            }

            jobs.AddRange(Main.Conversion.AudioConvert(audio, options, Output.Options, skip));
        }

        var images = Sources.ActiveItems.Where(i => i.Kind == MediaKind.Image).Select(i => i.ToRef()).ToList();
        if (images.Count > 0)
        {
            var options = JsonFile.Clone(Main.ImageConvert.Image.Options);
            options.Format = ImageTarget;
            jobs.AddRange(Main.Conversion.ImageConvert(images, options, Output.Options, skip, "OpConvertImage"));
        }

        return Task.FromResult(jobs);
    }

    public override void SaveState(Dictionary<string, JsonElement> pages) =>
        Save(pages, StateKey, Output.Options, Sources.Folder, new QuickTargets(VideoTarget, AudioTarget, ImageTarget));

    public override void LoadState(Dictionary<string, JsonElement> pages)
    {
        var targets = new QuickTargets(VideoTarget, AudioTarget, ImageTarget);
        Load(pages, StateKey, Output.Options, Sources.Folder, targets);
        VideoTarget = targets.Video;
        AudioTarget = targets.Audio;
        ImageTarget = targets.Image == ImageFormat.Keep ? ImageFormat.WebP : targets.Image;
    }

    /// <summary>Saved target formats (mutable for the state loader).</summary>
    public sealed class QuickTargets
    {
        public QuickTargets()
        {
        }

        public QuickTargets(VideoContainer video, AudioFormat audio, ImageFormat image)
        {
            Video = video;
            Audio = audio;
            Image = image;
        }

        public VideoContainer Video { get; set; }

        public AudioFormat Audio { get; set; }

        public ImageFormat Image { get; set; }
    }
}

// ==================================================================== Video › Convert

public sealed class VideoConvertViewModel : ConvertPageViewModel
{
    public VideoConvertViewModel(MainViewModel main) : base(main, new HashSet<MediaKind> { MediaKind.Video }, MediaKind.Video)
    {
        Video = new VideoOptionsViewModel(main);
        Video.Changed += UpdateEstimate;
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(EstimateText))
            {
                Video.SizeEstimateText = EstimateText;
            }
        };
    }

    public override string TitleKey => "NavVideoConvert";

    public override string IconKey => "IconConvert";

    public VideoOptionsViewModel Video { get; }

    protected override Task<List<ConversionJob>> PlanAsync() =>
        Task.FromResult(Main.Conversion.VideoConvert(Sources.Refs.ToList(), Video.Options, Output.Options, Sources.Folder.SkipSameFormat));

    protected override long? Estimate(SourceItem item) => item.Info is { } info ? SizeEstimator.Video(info, Video.Options) : null;

    public override void SaveState(Dictionary<string, JsonElement> pages) => Save(pages, StateKey, Video.Options, Output.Options, Sources.Folder);

    public override void LoadState(Dictionary<string, JsonElement> pages)
    {
        Load(pages, StateKey, Video.Options, Output.Options, Sources.Folder);
        Video.Rebuild();
    }

    public override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        Video.RefreshTexts();
    }
}

// ==================================================================== Video › GIF / WebP

public sealed class AnimationViewModel : ConvertPageViewModel
{
    public AnimationViewModel(MainViewModel main) : base(main, new HashSet<MediaKind> { MediaKind.Video }, MediaKind.Video)
    {
        Gif.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(GifOptions.Format))
            {
                OnPropertyChanged(nameof(IsWebp));
                OnPropertyChanged(nameof(IsGif));
            }
        };
    }

    public override string TitleKey => "NavGif";

    public override string IconKey => "IconGif";

    public GifOptions Gif { get; } = new();

    public AnimationFormat[] Formats => Choices.AnimationFormats;

    public GifDither[] Dithers => Choices.GifDithers;

    public int[] FpsChoices => Choices.GifFps;

    public int[] WidthChoices => Choices.GifWidths;

    public bool IsWebp => Gif.Format == AnimationFormat.WebP;

    public bool IsGif => !IsWebp;

    protected override Task<List<ConversionJob>> PlanAsync() =>
        Task.FromResult(Main.Conversion.Animation(Sources.Refs.ToList(), Gif, Output.Options));

    public override void SaveState(Dictionary<string, JsonElement> pages) => Save(pages, StateKey, Gif, Output.Options, Sources.Folder);

    public override void LoadState(Dictionary<string, JsonElement> pages) => Load(pages, StateKey, Gif, Output.Options, Sources.Folder);
}

// ==================================================================== Video › Remux

public sealed class RemuxViewModel : ConvertPageViewModel
{
    public RemuxViewModel(MainViewModel main) : base(main, new HashSet<MediaKind> { MediaKind.Video }, MediaKind.Video)
    {
        Remux.PropertyChanged += (_, _) => OnPropertyChanged(nameof(ShowFastStart));
    }

    public override string TitleKey => "NavRemux";

    public override string IconKey => "IconRemux";

    public RemuxOptions Remux { get; } = new();

    public VideoContainer[] Containers => Choices.Containers;

    public bool ShowFastStart => Remux.Container is VideoContainer.Mp4 or VideoContainer.Mov;

    protected override Task<List<ConversionJob>> PlanAsync() =>
        Task.FromResult(Main.Conversion.Remux(Sources.Refs.ToList(), Remux, Output.Options, Sources.Folder.SkipSameFormat));

    protected override long? Estimate(SourceItem item) => item.Size;

    public override void SaveState(Dictionary<string, JsonElement> pages) => Save(pages, StateKey, Remux, Output.Options, Sources.Folder);

    public override void LoadState(Dictionary<string, JsonElement> pages) => Load(pages, StateKey, Remux, Output.Options, Sources.Folder);
}

// ==================================================================== Video › Extract audio

public sealed class ExtractAudioViewModel : ConvertPageViewModel
{
    public ExtractAudioViewModel(MainViewModel main) : base(main, new HashSet<MediaKind> { MediaKind.Video }, MediaKind.Video)
    {
        Audio = new AudioOptionsViewModel(main);
        Audio.Changed += UpdateEstimate;
        Extract.PropertyChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(IsConvert));
            UpdateEstimate();
        };
    }

    public override string TitleKey => "NavExtract";

    public override string IconKey => "IconExtract";

    public ExtractAudioOptions Extract { get; } = new();

    public AudioOptionsViewModel Audio { get; }

    public ExtractMode[] Modes => Choices.ExtractModes;

    public bool IsConvert => Extract.Mode == ExtractMode.Convert;

    protected override Task<List<ConversionJob>> PlanAsync() =>
        Main.Conversion.ExtractAudioAsync(Sources.Refs.ToList(), Extract, Audio.Options, Output.Options, CancellationToken.None);

    protected override long? Estimate(SourceItem item)
    {
        if (item.Info is not { } info || info.MainAudio is not { } track)
        {
            return null;
        }

        return IsConvert
            ? SizeEstimator.Audio(info, Audio.Options)
            : track.Bitrate > 0 && info.DurationSeconds > 0 ? (long)(track.Bitrate * info.DurationSeconds / 8) : null;
    }

    public override void SaveState(Dictionary<string, JsonElement> pages) => Save(pages, StateKey, Extract, Audio.Options, Output.Options, Sources.Folder);

    public override void LoadState(Dictionary<string, JsonElement> pages)
    {
        Load(pages, StateKey, Extract, Audio.Options, Output.Options, Sources.Folder);
        Audio.Rebuild();
    }

    public override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        Audio.RefreshTexts();
    }
}

// ==================================================================== Audio › Convert

public sealed class AudioConvertViewModel : ConvertPageViewModel
{
    public AudioConvertViewModel(MainViewModel main)
        : base(main, new HashSet<MediaKind> { MediaKind.Audio, MediaKind.Video }, MediaKind.Audio, miniPlayerKey: "AudioConvert")
    {
        Audio = new AudioOptionsViewModel(main);
        Audio.Changed += UpdateEstimate;
    }

    public override string TitleKey => "NavAudioConvert";

    public override string IconKey => "IconConvert";

    public AudioOptionsViewModel Audio { get; }

    protected override Task<List<ConversionJob>> PlanAsync() =>
        Task.FromResult(Main.Conversion.AudioConvert(Sources.Refs.ToList(), Audio.Options, Output.Options, Sources.Folder.SkipSameFormat));

    protected override long? Estimate(SourceItem item) => item.Info is { } info ? SizeEstimator.Audio(info, Audio.Options) : null;

    public override void SaveState(Dictionary<string, JsonElement> pages) => Save(pages, StateKey, Audio.Options, Output.Options, Sources.Folder);

    public override void LoadState(Dictionary<string, JsonElement> pages)
    {
        Load(pages, StateKey, Audio.Options, Output.Options, Sources.Folder);
        Audio.Rebuild();
    }

    public override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        Audio.RefreshTexts();
    }
}

// ==================================================================== Image › Convert / Resize

public sealed class ImageConvertViewModel : ConvertPageViewModel
{
    private readonly bool _resizeOnly;

    public ImageConvertViewModel(MainViewModel main, bool resizeOnly) : base(main, new HashSet<MediaKind> { MediaKind.Image }, MediaKind.Image)
    {
        _resizeOnly = resizeOnly;
        Image = new ImageOptionsViewModel(allowKeep: true);
        if (resizeOnly)
        {
            Image.Options.Format = ImageFormat.Keep;
            Image.Options.Resize = ResizeMode.MaxSize;
        }
    }

    public override string TitleKey => _resizeOnly ? "NavResize" : "NavImageConvert";

    public override string IconKey => _resizeOnly ? "IconResize" : "IconConvert";

    public override string StateKey => _resizeOnly ? "ImageResize" : "ImageConvert";

    public override bool NeedsFfmpeg => false;

    public bool IsResizePage => _resizeOnly;

    public ImageOptionsViewModel Image { get; }

    protected override Task<List<ConversionJob>> PlanAsync() =>
        Task.FromResult(Main.Conversion.ImageConvert(Sources.Refs.ToList(), Image.Options, Output.Options,
            !_resizeOnly && Sources.Folder.SkipSameFormat, _resizeOnly ? "OpResize" : "OpConvertImage"));

    public override void SaveState(Dictionary<string, JsonElement> pages) => Save(pages, StateKey, Image.Options, Output.Options, Sources.Folder);

    public override void LoadState(Dictionary<string, JsonElement> pages) => Load(pages, StateKey, Image.Options, Output.Options, Sources.Folder);

    public override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        Image.RefreshTexts();
    }
}

// ==================================================================== Image › Icon

public sealed partial class IconViewModel : ConvertPageViewModel
{
    private CancellationTokenSource? _previewCts;

    public IconViewModel(MainViewModel main) : base(main, new HashSet<MediaKind> { MediaKind.Image }, MediaKind.Image)
    {
        IconSettings.PropertyChanged += (_, _) => _ = RenderPreviewAsync();
        Sources.SelectionChanged += item => { _ = RenderPreviewAsync(); };
    }

    public override string TitleKey => "NavIcon";

    public override string IconKey => "IconIcon";

    public override bool NeedsFfmpeg => false;

    public IconOptions IconSettings { get; } = new();

    public IconFit[] Fits => Choices.IconFits;

    /// <summary>Preview tiles (16, 32, 48, 256 px) on light and dark backgrounds.</summary>
    public ObservableCollection<IconPreview> Previews { get; } = [];

    [ObservableProperty] private bool _isRendering;

    protected override bool ExtraCanStart() => IconSettings.SelectedSizes().Length > 0 || IconSettings.FaviconPackage;

    protected override Task<List<ConversionJob>> PlanAsync() =>
        Task.FromResult(Main.Conversion.Icon(Sources.Refs.ToList(), IconSettings, Output.Options));

    private async Task RenderPreviewAsync()
    {
        _previewCts?.Cancel();
        var cts = _previewCts = new CancellationTokenSource();
        var item = Sources.SelectedItem;
        StartCommand.NotifyCanExecuteChanged();
        if (item is null)
        {
            Previews.Clear();
            return;
        }

        IsRendering = true;
        try
        {
            var options = JsonFile.Clone(IconSettings);
            var sizes = new[] { 16, 32, 48, 256 };
            var images = await Task.Run(() => sizes.Select(s => (Size: s, Bytes: ImageEngine.IconPreview(item.Path, s, options))).ToList(), cts.Token);
            if (cts.IsCancellationRequested)
            {
                return;
            }

            Previews.Clear();
            foreach (var (size, bytes) in images)
            {
                Previews.Add(new IconPreview(size, new Bitmap(new MemoryStream(bytes))));
            }
        }
        catch
        {
            if (!cts.IsCancellationRequested)
            {
                Previews.Clear();
            }
        }
        finally
        {
            if (!cts.IsCancellationRequested)
            {
                IsRendering = false;
            }
        }
    }

    public override void SaveState(Dictionary<string, JsonElement> pages) => Save(pages, StateKey, IconSettings, Output.Options, Sources.Folder);

    public override void LoadState(Dictionary<string, JsonElement> pages) => Load(pages, StateKey, IconSettings, Output.Options, Sources.Folder);
}

/// <summary>One preview tile; shown at its real size (256 scaled to 96 px).</summary>
public sealed record IconPreview(int Size, Bitmap Image)
{
    public string Label => $"{Size} px";

    public double DisplaySize => Size > 96 ? 96 : Size;
}
