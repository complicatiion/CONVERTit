using CommunityToolkit.Mvvm.ComponentModel;

namespace CONVERTit.Core.Options;

/// <summary>Video encoding settings (Video › Convert, Trim with re-encode, Quick Convert).</summary>
public sealed partial class VideoOptions : ObservableObject
{
    // ---------------------------------------------------------------- Format
    [ObservableProperty] private VideoContainer _container = VideoContainer.Mp4;
    [ObservableProperty] private VideoCodec _codec = VideoCodec.H264;
    [ObservableProperty] private Acceleration _acceleration = Acceleration.Off;

    // ---------------------------------------------------------------- Quality
    [ObservableProperty] private VideoQualityMode _qualityMode = VideoQualityMode.Preset;
    [ObservableProperty] private QualityPreset _qualityPreset = QualityPreset.Balanced;

    /// <summary>CRF / CQ value. Range depends on the codec (x264/x265 0–51, AV1/VP9 0–63).</summary>
    [ObservableProperty] private int _crf = 23;

    [ObservableProperty] private int _bitrateKbps = 6000;

    /// <summary>Target size mode: output size in percent of the source file (5–100).</summary>
    [ObservableProperty] private int _targetPercent = 50;
    [ObservableProperty] private EncoderSpeed _speed = EncoderSpeed.Balanced;

    /// <summary>8-bit 4:2:0 output (plays everywhere). Off keeps 10-bit sources 10-bit.</summary>
    [ObservableProperty] private bool _compatible8Bit = true;

    // ---------------------------------------------------------------- Picture
    [ObservableProperty] private ResolutionPreset _resolution = ResolutionPreset.Original;
    [ObservableProperty] private int _customWidth = 1920;
    [ObservableProperty] private int _customHeight = 1080;
    [ObservableProperty] private AspectPreset _aspect = AspectPreset.Original;
    [ObservableProperty] private int _customAspectWidth = 16;
    [ObservableProperty] private int _customAspectHeight = 9;
    [ObservableProperty] private ScaleMode _scaleMode = ScaleMode.Fit;

    /// <summary>Bar color for <see cref="ScaleMode.Fit"/> (#RRGGBB).</summary>
    [ObservableProperty] private string _padColor = "#000000";

    [ObservableProperty] private bool _noUpscale = true;

    // ---------------------------------------------------------------- Frame rate
    [ObservableProperty] private FrameRatePreset _frameRate = FrameRatePreset.Original;
    [ObservableProperty] private double _customFps = 30;
    [ObservableProperty] private FpsConversion _fpsConversion = FpsConversion.Drop;

    // ---------------------------------------------------------------- Audio & streams
    [ObservableProperty] private VideoAudioMode _audio = VideoAudioMode.Auto;
    [ObservableProperty] private int _audioBitrateKbps = 192;
    [ObservableProperty] private bool _keepSubtitles = true;
    [ObservableProperty] private bool _keepMetadata = true;

    /// <summary>MP4/MOV: move the index to the front so playback starts while downloading.</summary>
    [ObservableProperty] private bool _fastStart = true;
}
