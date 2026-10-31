using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CONVERTit.App.Localization;
using CONVERTit.App.Services;
using CONVERTit.Core.Catalog;
using CONVERTit.Core.Options;
using CONVERTit.Engines.Image;

namespace CONVERTit.App.ViewModels;

/// <summary>
/// Fixed choice lists. They are created once: a ComboBox keeps its selection only while its item list stays
/// the same instance. Labels follow the language through the label templates (LocState).
/// </summary>
public static class Choices
{
    public static readonly VideoContainer[] Containers = Enum.GetValues<VideoContainer>();
    public static readonly VideoQualityMode[] QualityModes = Enum.GetValues<VideoQualityMode>();
    public static readonly QualityPreset[] QualityPresets = Enum.GetValues<QualityPreset>();
    public static readonly EncoderSpeed[] Speeds = Enum.GetValues<EncoderSpeed>();
    public static readonly ResolutionPreset[] Resolutions = Enum.GetValues<ResolutionPreset>();
    public static readonly AspectPreset[] Aspects = Enum.GetValues<AspectPreset>();
    public static readonly ScaleMode[] ScaleModes = Enum.GetValues<ScaleMode>();
    public static readonly FrameRatePreset[] FrameRates = Enum.GetValues<FrameRatePreset>();
    public static readonly FpsConversion[] FpsConversions = Enum.GetValues<FpsConversion>();
    public static readonly BitrateMode[] BitrateModes = Enum.GetValues<BitrateMode>();
    public static readonly WavDepth[] WavDepths = Enum.GetValues<WavDepth>();
    public static readonly ChannelMode[] ChannelModes = Enum.GetValues<ChannelMode>();
    public static readonly ResizeMode[] ResizeModes = Enum.GetValues<ResizeMode>();
    public static readonly ChromaSubsampling[] Chromas = Enum.GetValues<ChromaSubsampling>();
    public static readonly TiffCompression[] TiffCompressions = Enum.GetValues<TiffCompression>();
    public static readonly IconFit[] IconFits = Enum.GetValues<IconFit>();
    public static readonly TrimMode[] TrimModes = Enum.GetValues<TrimMode>();
    public static readonly LoopMode[] LoopModes = Enum.GetValues<LoopMode>();
    public static readonly TrimOutput[] TrimOutputs = Enum.GetValues<TrimOutput>();
    public static readonly AnimationFormat[] AnimationFormats = Enum.GetValues<AnimationFormat>();
    public static readonly GifDither[] GifDithers = Enum.GetValues<GifDither>();
    public static readonly ExtractMode[] ExtractModes = Enum.GetValues<ExtractMode>();
    public static readonly OutputDestination[] Destinations = Enum.GetValues<OutputDestination>();
    public static readonly ConflictPolicy[] Conflicts = Enum.GetValues<ConflictPolicy>();
    public static readonly ThemeOption[] Themes = Enum.GetValues<ThemeOption>();
    public static readonly PriorityOption[] Priorities = Enum.GetValues<PriorityOption>();
    public static readonly DisplayProfile[] DisplayProfiles = Enum.GetValues<DisplayProfile>();
    public static readonly DdsCompression[] DdsCompressions = Enum.GetValues<DdsCompression>();
    public static readonly PowerOfTwo[] PowerOfTwoModes = Enum.GetValues<PowerOfTwo>();
    public static readonly LoudnessTarget[] LoudnessTargets = Enum.GetValues<LoudnessTarget>();
    public static readonly CompressMode[] CompressModes = Enum.GetValues<CompressMode>();
    public static readonly CompressStrength[] CompressStrengths = Enum.GetValues<CompressStrength>();
    public static readonly CompressImageFormat[] CompressImageFormats = Enum.GetValues<CompressImageFormat>();
    public static readonly TexturePreset[] TexturePresets = Enum.GetValues<TexturePreset>();
    public static readonly GameAudioPreset[] GameAudioPresets = Enum.GetValues<GameAudioPreset>();
    public static readonly ResolutionPreset[] MaxResolutions = [ResolutionPreset.Original, ResolutionPreset.P2160, ResolutionPreset.P1440, ResolutionPreset.P1080, ResolutionPreset.P720, ResolutionPreset.P480];
    public static readonly FrameRatePreset[] MaxFrameRates = [FrameRatePreset.Original, FrameRatePreset.Fps60, FrameRatePreset.Fps30, FrameRatePreset.Fps25, FrameRatePreset.Fps24];
    public static readonly int[] TargetSizesMb = [5, 8, 10, 16, 20, 25, 50, 100, 200, 500, 1000, 2000];
    public static readonly int[] Percents = [10, 20, 25, 30, 40, 50, 60, 70, 75, 80, 90];
    public static readonly int[] CompressAudioKbps = [0, 64, 96, 128, 160, 192];
    public static readonly int[] ImageMaxSides = [0, 7680, 3840, 2560, 1920, 1600, 1280, 1024, 800];
    public static readonly int[] TextureSizes = [0, 8192, 4096, 2048, 1024, 512, 256, 128];
    public static readonly int[] AudioBitrates = [.. FormatCatalog.VideoAudioBitrates];
    public static readonly int[] Mp3VbrLevels = [.. FormatCatalog.Mp3VbrLevels];
    public static readonly int[] VorbisQualities = [10, 9, 8, 7, 6, 5, 4, 3, 2, 1, 0];
    public static readonly int[] FlacLevels = [0, 1, 2, 3, 4, 5, 6, 7, 8];
    public static readonly int[] SampleRates = [.. FormatCatalog.SampleRates];
    public static readonly int[] GifFps = [5, 8, 10, 12, 15, 20, 24, 25, 30, 50, 60];
    public static readonly int[] GifWidths = [0, 240, 320, 360, 480, 540, 640, 720, 800, 960, 1080];

    /// <summary>Changes a list in place (remove/insert), so a selected item that stays in the list stays selected.</summary>
    public static void Sync<T>(ObservableCollection<T> target, IReadOnlyList<T> desired)
    {
        for (var i = target.Count - 1; i >= 0; i--)
        {
            if (!desired.Contains(target[i]))
            {
                target.RemoveAt(i);
            }
        }

        for (var i = 0; i < desired.Count; i++)
        {
            if (i < target.Count && EqualityComparer<T>.Default.Equals(target[i], desired[i]))
            {
                continue;
            }

            var existing = target.IndexOf(desired[i]);
            if (existing >= 0)
            {
                target.Move(existing, i);
            }
            else
            {
                target.Insert(i, desired[i]);
            }
        }
    }
}

/// <summary>
/// Video options with dependent choice lists: codecs per container, tested hardware encoders,
/// audio codecs per container, CRF range per codec and visibility of custom fields.
/// </summary>
public sealed class VideoOptionsViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private bool _adjusting;

    public VideoOptionsViewModel(MainViewModel main, VideoOptions? options = null)
    {
        _main = main;
        Options = options ?? new VideoOptions();
        Options.PropertyChanged += OnOptionChanged;
        _main.EncodersChanged += Rebuild;
        Rebuild();
    }

    public VideoOptions Options { get; }

    public event Action? Changed;

    public VideoContainer[] Containers => Choices.Containers;
    public ObservableCollection<VideoCodec> Codecs { get; } = [];
    public ObservableCollection<Acceleration> Accelerations { get; } = [];
    public ObservableCollection<VideoAudioMode> AudioModes { get; } = [];
    public VideoQualityMode[] QualityModes => Choices.QualityModes;
    public QualityPreset[] QualityPresets => Choices.QualityPresets;
    public EncoderSpeed[] Speeds => Choices.Speeds;
    public ResolutionPreset[] Resolutions => Choices.Resolutions;
    public AspectPreset[] Aspects => Choices.Aspects;
    public ScaleMode[] ScaleModes => Choices.ScaleModes;
    public FrameRatePreset[] FrameRates => Choices.FrameRates;
    public FpsConversion[] FpsConversions => Choices.FpsConversions;
    public int[] AudioBitrates => Choices.AudioBitrates;

    public bool HasHardware => Accelerations.Count > 1;

    public int CrfMax => FormatCatalog.CrfMax(Options.Codec);

    public string CrfHint => Loc.F(Options.Codec is VideoCodec.Av1 or VideoCodec.Vp9 ? "HintCrf63" : "HintCrf51",
        FormatCatalog.CrfFor(Options.Codec, QualityPreset.High), FormatCatalog.CrfFor(Options.Codec, QualityPreset.Balanced));

    public bool IsPresetMode => Options.QualityMode == VideoQualityMode.Preset;
    public bool IsCrfMode => Options.QualityMode == VideoQualityMode.Crf;
    public bool IsBitrateMode => Options.QualityMode == VideoQualityMode.Bitrate;
    public bool IsTargetSizeMode => Options.QualityMode == VideoQualityMode.TargetSize;

    private string _sizeEstimateText = "";

    /// <summary>Expected size of the page's files ("≈ 1.2 GB of 2.4 GB"), set by the page.</summary>
    public string SizeEstimateText
    {
        get => _sizeEstimateText;
        set => SetProperty(ref _sizeEstimateText, value);
    }
    public bool IsCustomResolution => Options.Resolution == ResolutionPreset.Custom;
    public bool IsCustomAspect => Options.Aspect == AspectPreset.Custom && Options.Resolution != ResolutionPreset.Custom;
    public bool ShowAspect => Options.Resolution != ResolutionPreset.Custom;
    public bool ShowScaleMode => Options.Aspect != AspectPreset.Original || Options.Resolution == ResolutionPreset.Custom;
    public bool ShowPadColor => ShowScaleMode && Options.ScaleMode == ScaleMode.Fit;
    public bool IsCustomFps => Options.FrameRate == FrameRatePreset.Custom;
    public bool ShowFpsConversion => Options.FrameRate != FrameRatePreset.Original;
    public bool ShowAudioBitrate => Options.Audio is not (VideoAudioMode.Copy or VideoAudioMode.Remove);
    public bool ShowSubtitles => Options.Container != VideoContainer.Avi;
    public bool ShowFastStart => Options.Container is VideoContainer.Mp4 or VideoContainer.Mov;
    public bool ShowNoUpscale => Options.Resolution != ResolutionPreset.Original;

    private void OnOptionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_adjusting)
        {
            return;
        }

        if (e.PropertyName is nameof(VideoOptions.Container) or nameof(VideoOptions.Codec))
        {
            Rebuild();
        }

        if (e.PropertyName == nameof(VideoOptions.QualityMode) && Options.QualityMode == VideoQualityMode.Crf)
        {
            _adjusting = true;
            Options.Crf = FormatCatalog.CrfFor(Options.Codec, Options.QualityPreset);
            _adjusting = false;
        }

        RaiseFlags();
        Changed?.Invoke();
    }

    /// <summary>Rebuilds the dependent lists and keeps the options valid (also after loading saved options).</summary>
    public void Rebuild()
    {
        var codecs = FormatCatalog.CodecsFor(Options.Container)
            .Where(c => _main.HasEncoder(FormatCatalog.VideoEncoder(c, Acceleration.Off)!)).ToList();
        if (codecs.Count == 0)
        {
            codecs = [.. FormatCatalog.CodecsFor(Options.Container)];
        }

        Choices.Sync(Codecs, codecs);
        _adjusting = true;
        try
        {
            if (!Codecs.Contains(Options.Codec))
            {
                Options.Codec = Codecs[0];
            }

            var accelerations = Enum.GetValues<Acceleration>()
                .Where(a => a == Acceleration.Off || (_main.Settings.UseHardwareEncoders
                    && FormatCatalog.VideoEncoder(Options.Codec, a) is { } enc && _main.HardwareWorks(enc)))
                .ToList();
            Choices.Sync(Accelerations, accelerations);
            if (!Accelerations.Contains(Options.Acceleration))
            {
                Options.Acceleration = Acceleration.Off;
            }

            Choices.Sync(AudioModes, FormatCatalog.AudioModesFor(Options.Container));
            if (!AudioModes.Contains(Options.Audio))
            {
                Options.Audio = VideoAudioMode.Auto;
            }

            Options.Crf = Math.Clamp(Options.Crf, 0, CrfMax);
        }
        finally
        {
            _adjusting = false;
        }

        OnPropertyChanged(nameof(HasHardware));
        OnPropertyChanged(nameof(CrfMax));
        OnPropertyChanged(nameof(CrfHint));
        RaiseFlags();
    }

    private void RaiseFlags()
    {
        foreach (var name in new[]
                 {
                     nameof(IsPresetMode), nameof(IsCrfMode), nameof(IsBitrateMode), nameof(IsTargetSizeMode), nameof(IsCustomResolution), nameof(IsCustomAspect),
                     nameof(ShowAspect), nameof(ShowScaleMode), nameof(ShowPadColor), nameof(IsCustomFps), nameof(ShowFpsConversion),
                     nameof(ShowAudioBitrate), nameof(ShowSubtitles), nameof(ShowFastStart), nameof(ShowNoUpscale)
                 })
        {
            OnPropertyChanged(name);
        }
    }

    public void RefreshTexts() => OnPropertyChanged(nameof(CrfHint));
}

/// <summary>Audio options with format-dependent quality controls.</summary>
public sealed class AudioOptionsViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private bool _adjusting;

    public AudioOptionsViewModel(MainViewModel main, AudioOptions? options = null)
    {
        _main = main;
        Options = options ?? new AudioOptions();
        Options.PropertyChanged += OnOptionChanged;
        _main.EncodersChanged += Rebuild;
        Rebuild();
    }

    public AudioOptions Options { get; }

    public event Action? Changed;

    public ObservableCollection<AudioFormat> Formats { get; } = [];
    public ObservableCollection<int> Bitrates { get; } = [];
    public BitrateMode[] Mp3Modes => Choices.BitrateModes;
    public int[] VbrLevels => Choices.Mp3VbrLevels;
    public int[] VorbisQualities => Choices.VorbisQualities;
    public int[] FlacLevels => Choices.FlacLevels;
    public WavDepth[] WavDepths => Choices.WavDepths;
    public int[] SampleRates => Choices.SampleRates;
    public ChannelMode[] ChannelModes => Choices.ChannelModes;
    public LoudnessTarget[] LoudnessTargets => Choices.LoudnessTargets;

    public bool IsMp3 => Options.Format == AudioFormat.Mp3;
    public bool IsMp3Vbr => IsMp3 && Options.Mp3Mode == BitrateMode.Vbr;
    public bool ShowBitrate => Options.Format is AudioFormat.Aac or AudioFormat.Opus || (IsMp3 && Options.Mp3Mode == BitrateMode.Cbr);
    public bool IsVorbis => Options.Format == AudioFormat.Vorbis;
    public bool IsFlac => Options.Format == AudioFormat.Flac;
    public bool IsWav => Options.Format == AudioFormat.Wav;
    public bool IsLossless => FormatCatalog.IsLossless(Options.Format);
    public bool IsLossy => !IsLossless;
    public bool SupportsCover => FormatCatalog.SupportsCover(Options.Format);
    public string FormatHint => Loc.T("HintAudio" + Options.Format);

    private void OnOptionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_adjusting)
        {
            return;
        }

        if (e.PropertyName == nameof(AudioOptions.Format))
        {
            Rebuild();
        }

        RaiseFlags();
        Changed?.Invoke();
    }

    public void Rebuild()
    {
        var formats = Enum.GetValues<AudioFormat>().Where(f => _main.HasEncoder(FormatCatalog.AudioEncoder(f))).ToList();
        if (formats.Count == 0)
        {
            formats = [.. Enum.GetValues<AudioFormat>()];
        }

        Choices.Sync(Formats, formats);
        _adjusting = true;
        try
        {
            if (!Formats.Contains(Options.Format))
            {
                Options.Format = Formats[0];
            }

            var steps = FormatCatalog.BitratesFor(Options.Format);
            var keep = Options.BitrateKbps;
            Choices.Sync(Bitrates, steps);
            if (steps.Count > 0)
            {
                Options.BitrateKbps = steps.Contains(keep) ? keep : Options.Format switch
                {
                    AudioFormat.Opus => 160,
                    AudioFormat.Aac => 256,
                    _ => 320
                };
            }
        }
        finally
        {
            _adjusting = false;
        }

        RaiseFlags();
    }

    private void RaiseFlags()
    {
        foreach (var name in new[]
                 {
                     nameof(IsMp3), nameof(IsMp3Vbr), nameof(ShowBitrate), nameof(IsVorbis), nameof(IsFlac), nameof(IsWav),
                     nameof(IsLossless), nameof(IsLossy), nameof(SupportsCover), nameof(FormatHint)
                 })
        {
            OnPropertyChanged(name);
        }
    }

    public void RefreshTexts() => OnPropertyChanged(nameof(FormatHint));
}

/// <summary>Image options with format-dependent controls.</summary>
public sealed class ImageOptionsViewModel : ObservableObject
{
    public ImageOptionsViewModel(bool allowKeep, ImageOptions? options = null)
    {
        AllowKeep = allowKeep;
        Options = options ?? new ImageOptions();
        Formats = Enum.GetValues<ImageFormat>().Where(f => (AllowKeep || f != ImageFormat.Keep) && ImageEngine.CanWrite(f)).ToArray();
        Options.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ImageOptions.Format) or nameof(ImageOptions.KeepTransparency) or nameof(ImageOptions.WebpLossless)
                or nameof(ImageOptions.PngPalette) or nameof(ImageOptions.Resize))
            {
                RaiseFlags();
            }

            Changed?.Invoke();
        };
    }

    public ImageOptions Options { get; }

    public bool AllowKeep { get; }

    public event Action? Changed;

    public ImageFormat[] Formats { get; }
    public ResizeMode[] ResizeModes => Choices.ResizeModes;
    public ScaleMode[] ScaleModes => Choices.ScaleModes;
    public ChromaSubsampling[] Chromas => Choices.Chromas;
    public TiffCompression[] TiffCompressions => Choices.TiffCompressions;
    public DdsCompression[] DdsCompressions => Choices.DdsCompressions;
    public PowerOfTwo[] PowerOfTwoModes => Choices.PowerOfTwoModes;

    private ImageFormat F => Options.Format;

    public bool IsPng => F == ImageFormat.Png;
    public bool IsJpeg => F == ImageFormat.Jpeg;
    public bool IsWebp => F == ImageFormat.WebP;
    public bool IsAvif => F == ImageFormat.Avif;
    public bool IsTiff => F == ImageFormat.Tiff;
    public bool IsGif => F == ImageFormat.Gif;
    public bool IsTga => F == ImageFormat.Tga;
    public bool IsDds => F == ImageFormat.Dds;
    public bool ShowQuality => F is ImageFormat.Jpeg or ImageFormat.Avif or ImageFormat.Keep || (F == ImageFormat.WebP && !Options.WebpLossless);
    public bool ShowPaletteColors => IsPng && Options.PngPalette;
    public bool SupportsAlpha => F == ImageFormat.Keep || FormatCatalog.SupportsAlpha(F);
    public bool ShowBackground => !SupportsAlpha || !Options.KeepTransparency;
    public bool ShowAnimation => F is ImageFormat.Gif or ImageFormat.WebP or ImageFormat.Keep;
    public bool IsResizeMax => Options.Resize == ResizeMode.MaxSize;
    public bool IsResizePercent => Options.Resize == ResizeMode.Percent;
    public bool IsResizeExact => Options.Resize == ResizeMode.Exact;
    public bool ShowSize => IsResizeMax || IsResizeExact;
    public bool ShowNoUpscale => Options.Resize != ResizeMode.Off;
    public string FormatHint => Loc.T("HintImage" + F);

    private void RaiseFlags()
    {
        foreach (var name in new[]
                 {
                     nameof(IsPng), nameof(IsJpeg), nameof(IsWebp), nameof(IsAvif), nameof(IsTiff), nameof(IsGif), nameof(IsTga), nameof(IsDds), nameof(ShowQuality),
                     nameof(ShowPaletteColors), nameof(SupportsAlpha), nameof(ShowBackground), nameof(ShowAnimation), nameof(IsResizeMax),
                     nameof(IsResizePercent), nameof(IsResizeExact), nameof(ShowSize), nameof(ShowNoUpscale), nameof(FormatHint)
                 })
        {
            OnPropertyChanged(name);
        }
    }

    public void RefreshTexts() => OnPropertyChanged(nameof(FormatHint));
}
