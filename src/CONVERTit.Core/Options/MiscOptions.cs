using CommunityToolkit.Mvvm.ComponentModel;

namespace CONVERTit.Core.Options;

/// <summary>Image › Icon: multi-size ICO and optional favicon package.</summary>
public sealed partial class IconOptions : ObservableObject
{
    [ObservableProperty] private bool _size16 = true;
    [ObservableProperty] private bool _size24 = true;
    [ObservableProperty] private bool _size32 = true;
    [ObservableProperty] private bool _size48 = true;
    [ObservableProperty] private bool _size64 = true;
    [ObservableProperty] private bool _size128 = true;
    [ObservableProperty] private bool _size256 = true;

    /// <summary>Non-square images: add transparent borders (Pad) or cut the center (Crop).</summary>
    [ObservableProperty] private IconFit _fit = IconFit.Pad;

    /// <summary>Empty border around the motif in percent of the icon size.</summary>
    [ObservableProperty] private int _paddingPercent;

    /// <summary>Slightly sharpen sizes up to 48 px after downscaling.</summary>
    [ObservableProperty] private bool _sharpenSmall = true;

    /// <summary>Also write favicon.ico, apple-touch-icon.png, 192/512 PNGs and site.webmanifest.</summary>
    [ObservableProperty] private bool _faviconPackage;

    public int[] SelectedSizes()
    {
        var sizes = new List<int>();
        if (Size256) sizes.Add(256);
        if (Size128) sizes.Add(128);
        if (Size64) sizes.Add(64);
        if (Size48) sizes.Add(48);
        if (Size32) sizes.Add(32);
        if (Size24) sizes.Add(24);
        if (Size16) sizes.Add(16);
        return [.. sizes];
    }
}

/// <summary>Trim, fades and loop (Video › Trim &amp; Loop, Audio › Trim &amp; Loop).</summary>
public sealed partial class TrimOptions : ObservableObject
{
    [ObservableProperty] private bool _trimEnabled = true;

    /// <summary>Start in seconds.</summary>
    [ObservableProperty] private double _start;

    /// <summary>End in seconds (0 = end of file).</summary>
    [ObservableProperty] private double _end;

    [ObservableProperty] private TrimMode _mode = TrimMode.Exact;

    /// <summary>Fade in/out in seconds (audio; video fades from/to black).</summary>
    [ObservableProperty] private double _fadeIn;

    [ObservableProperty] private double _fadeOut;

    [ObservableProperty] private LoopMode _loop = LoopMode.Off;
    [ObservableProperty] private int _loopCount = 2;
    [ObservableProperty] private double _loopTargetSeconds = 60;

    [ObservableProperty] private TrimOutput _output = TrimOutput.SameFormat;
}

/// <summary>Video › GIF / Animated WebP.</summary>
public sealed partial class GifOptions : ObservableObject
{
    [ObservableProperty] private AnimationFormat _format = AnimationFormat.Gif;
    [ObservableProperty] private int _fps = 15;

    /// <summary>Output width in pixels, 0 = original.</summary>
    [ObservableProperty] private int _width = 480;

    [ObservableProperty] private GifDither _dither = GifDither.Bayer;

    /// <summary>Animated WebP quality 0–100.</summary>
    [ObservableProperty] private int _quality = 75;

    /// <summary>0 = endless.</summary>
    [ObservableProperty] private int _loopCount;

    [ObservableProperty] private bool _boomerang;

    /// <summary>Part of the video in seconds; duration 0 = to the end.</summary>
    [ObservableProperty] private double _start;

    [ObservableProperty] private double _duration = 5;
}

/// <summary>Video › Remux: change the container without re-encoding.</summary>
public sealed partial class RemuxOptions : ObservableObject
{
    [ObservableProperty] private VideoContainer _container = VideoContainer.Mp4;
    [ObservableProperty] private bool _keepSubtitles = true;
    [ObservableProperty] private bool _fastStart = true;
}

/// <summary>Video › Extract audio.</summary>
public sealed partial class ExtractAudioOptions : ObservableObject
{
    [ObservableProperty] private ExtractMode _mode = ExtractMode.Copy;

    /// <summary>All audio tracks (one file each) instead of only the first.</summary>
    [ObservableProperty] private bool _allTracks;
}

/// <summary>Where results are written and how files are named.</summary>
public sealed partial class OutputOptions : ObservableObject
{
    [ObservableProperty] private OutputDestination _destination = OutputDestination.Subfolder;
    [ObservableProperty] private string _subfolderName = "converted";
    [ObservableProperty] private string _customFolder = "";

    /// <summary>Folder batches: rebuild the source folder structure below the target.</summary>
    [ObservableProperty] private bool _mirrorStructure = true;

    /// <summary>Appended to the file name, e.g. "_web".</summary>
    [ObservableProperty] private string _suffix = "";

    [ObservableProperty] private ConflictPolicy _conflict = ConflictPolicy.Rename;

    /// <summary>Copy the modification date of the source file.</summary>
    [ObservableProperty] private bool _preserveDates = true;
}

/// <summary>Folder batch: which files of a folder are taken.</summary>
public sealed partial class SourceOptions : ObservableObject
{
    [ObservableProperty] private bool _recursive = true;

    /// <summary>All files of the page's kind (all audio files, all images …).</summary>
    [ObservableProperty] private bool _allOfKind = true;

    /// <summary>Extensions when <see cref="AllOfKind"/> is off, e.g. "mp3, ogg, m4a".</summary>
    [ObservableProperty] private string _extensions = "";

    /// <summary>Skip files that already have the target format.</summary>
    [ObservableProperty] private bool _skipSameFormat = true;
}

/// <summary>
/// Compress page: makes videos, audio files and images smaller. Video and audio follow the mode (target size,
/// share of the original or quality level); images use quality and maximum size.
/// </summary>
public sealed partial class CompressOptions : ObservableObject
{
    [ObservableProperty] private CompressMode _mode = CompressMode.TargetSize;

    /// <summary>Target size per file in MB (target size mode).</summary>
    [ObservableProperty] private int _targetMb = 25;

    /// <summary>Target size in percent of the original (percent mode).</summary>
    [ObservableProperty] private int _percent = 50;

    [ObservableProperty] private CompressStrength _strength = CompressStrength.Medium;

    // ---------------------------------------------------------------- Video
    [ObservableProperty] private VideoCodec _codec = VideoCodec.H264;
    [ObservableProperty] private Acceleration _acceleration = Acceleration.Off;

    /// <summary>Largest output resolution (short side); Original keeps the size.</summary>
    [ObservableProperty] private ResolutionPreset _maxResolution = ResolutionPreset.Original;

    /// <summary>Frame rate limit; Original keeps it.</summary>
    [ObservableProperty] private FrameRatePreset _maxFrameRate = FrameRatePreset.Original;

    /// <summary>Audio bitrate inside videos in kbps; 0 removes the audio.</summary>
    [ObservableProperty] private int _videoAudioKbps = 128;

    // ---------------------------------------------------------------- Audio files
    [ObservableProperty] private AudioFormat _audioFormat = AudioFormat.Mp3;

    /// <summary>Bitrate for audio files in quality mode (kbps).</summary>
    [ObservableProperty] private int _audioKbps = 128;

    // ---------------------------------------------------------------- Images
    [ObservableProperty] private CompressImageFormat _imageFormat = CompressImageFormat.Keep;
    [ObservableProperty] private int _imageQuality = 80;

    /// <summary>Longest side in pixels, 0 = keep.</summary>
    [ObservableProperty] private int _imageMaxSide;

    [ObservableProperty] private bool _stripMetadata = true;

    // ---------------------------------------------------------------- General
    /// <summary>A result that is not smaller than the source is discarded (the job is reported as skipped).</summary>
    [ObservableProperty] private bool _onlyIfSmaller = true;
}

/// <summary>A named compress profile (built-in or saved by the user).</summary>
public sealed class CompressProfile
{
    public string Name { get; set; } = "";

    /// <summary>Built-in profiles have a text key and cannot be deleted.</summary>
    public string? NameKey { get; set; }

    public CompressOptions Options { get; set; } = new();

    public bool IsBuiltIn => NameKey is not null;

    /// <summary>Profiles that ship with CONVERTit.</summary>
    public static IReadOnlyList<CompressProfile> BuiltIn { get; } =
    [
        Make("ProfileEmail", o => { o.Mode = CompressMode.TargetSize; o.TargetMb = 25; o.MaxResolution = ResolutionPreset.P1080; }),
        Make("ProfileMessenger", o => { o.Mode = CompressMode.TargetSize; o.TargetMb = 16; o.MaxResolution = ResolutionPreset.P720; o.VideoAudioKbps = 96; }),
        Make("ProfileChat", o => { o.Mode = CompressMode.TargetSize; o.TargetMb = 10; o.MaxResolution = ResolutionPreset.P720; o.MaxFrameRate = FrameRatePreset.Fps30; o.VideoAudioKbps = 96; }),
        Make("ProfileHalf", o => { o.Mode = CompressMode.Percent; o.Percent = 50; }),
        Make("ProfileQuarter", o => { o.Mode = CompressMode.Percent; o.Percent = 25; o.MaxResolution = ResolutionPreset.P1080; }),
        Make("ProfileHevc", o => { o.Mode = CompressMode.Quality; o.Strength = CompressStrength.Light; o.Codec = VideoCodec.H265; o.VideoAudioKbps = 160; }),
        Make("ProfileStrong", o => { o.Mode = CompressMode.Quality; o.Strength = CompressStrength.Strong; o.MaxResolution = ResolutionPreset.P720; o.VideoAudioKbps = 96; o.AudioKbps = 96; o.ImageQuality = 70; o.ImageMaxSide = 1920; })
    ];

    private static CompressProfile Make(string key, Action<CompressOptions> setup)
    {
        var options = new CompressOptions();
        setup(options);
        return new CompressProfile { NameKey = key, Name = key, Options = options };
    }
}
