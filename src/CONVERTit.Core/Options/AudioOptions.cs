using CommunityToolkit.Mvvm.ComponentModel;

namespace CONVERTit.Core.Options;

/// <summary>Audio encoding settings (Audio › Convert, Audio › Trim, Video › Extract audio).</summary>
public sealed partial class AudioOptions : ObservableObject
{
    [ObservableProperty] private AudioFormat _format = AudioFormat.Mp3;

    /// <summary>Constant bitrate in kbps (MP3 CBR, AAC, Opus).</summary>
    [ObservableProperty] private int _bitrateKbps = 320;

    /// <summary>MP3 only: constant or variable bitrate.</summary>
    [ObservableProperty] private BitrateMode _mp3Mode = BitrateMode.Cbr;

    /// <summary>MP3 VBR level V0 (best) … V9 (smallest).</summary>
    [ObservableProperty] private int _mp3VbrLevel = 0;

    /// <summary>Ogg Vorbis quality q0 … q10.</summary>
    [ObservableProperty] private int _vorbisQuality = 6;

    /// <summary>FLAC compression 0 (fast) … 8 (smallest). Lossless at every level.</summary>
    [ObservableProperty] private int _flacLevel = 5;

    [ObservableProperty] private WavDepth _wavDepth = WavDepth.Pcm16;

    /// <summary>Sample rate in Hz, 0 = keep.</summary>
    [ObservableProperty] private int _sampleRate = 0;

    [ObservableProperty] private ChannelMode _channels = ChannelMode.Original;

    /// <summary>Normalize the loudness (EBU R128) to a target level.</summary>
    [ObservableProperty] private LoudnessTarget _loudness = LoudnessTarget.Off;

    /// <summary>Remove silence at the start and the end.</summary>
    [ObservableProperty] private bool _trimSilence;
    [ObservableProperty] private bool _keepMetadata = true;

    /// <summary>Keep embedded cover art (MP3, M4A, FLAC).</summary>
    [ObservableProperty] private bool _keepCover = true;

    /// <summary>Never encode above the bitrate of a lossy source (a 128 kbps MP3 does not get better at 320 kbps).</summary>
    [ObservableProperty] private bool _notAboveSource = true;
}
