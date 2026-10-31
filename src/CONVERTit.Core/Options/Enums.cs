namespace CONVERTit.Core.Options;

// Every enum value has a UI label "<EnumName>.<Value>" in Localization/Strings.cs.

public enum VideoContainer { Mp4, Mkv, WebM, Mov, Avi }

public enum VideoCodec { H264, H265, Av1, Vp9 }

/// <summary>Hardware encoder family. Off = software encoder (libx264, libx265, SVT-AV1, libvpx).</summary>
public enum Acceleration { Off, Nvidia, Intel, Amd }

/// <summary>TargetSize = share of the source file size (bitrate derived per file).</summary>
public enum VideoQualityMode { Preset, Crf, Bitrate, TargetSize }

public enum QualityPreset { Best, High, Balanced, Small, Tiny }

public enum EncoderSpeed { Fastest, Fast, Balanced, Slow, Slowest }

/// <summary>Short side of the output (1080p = 1920×1080 landscape or 1080×1920 portrait).</summary>
public enum ResolutionPreset { Original, P2160, P1440, P1080, P720, P576, P480, P360, Custom }

public enum AspectPreset { Original, R16x9, R4x3, R1x1, R9x16, R21x9, R4x5, R3x2, Custom }

/// <summary>How the picture is fitted when the aspect ratio changes.</summary>
public enum ScaleMode { Fit, Fill, Stretch }

public enum FrameRatePreset { Original, Fps23976, Fps24, Fps25, Fps2997, Fps30, Fps48, Fps50, Fps5994, Fps60, Fps120, Custom }

/// <summary>Frame rate conversion: drop/duplicate frames, blend neighbours or motion interpolation (slow).</summary>
public enum FpsConversion { Drop, Blend, Motion }

public enum VideoAudioMode { Auto, Copy, Aac, Opus, Mp3, Remove }

public enum AudioFormat { Mp3, Aac, Opus, Vorbis, Flac, Alac, Wav }

public enum BitrateMode { Cbr, Vbr }

public enum WavDepth { Pcm16, Pcm24, Float32 }

public enum ChannelMode { Original, Stereo, Mono }

/// <summary>Loudness normalization (EBU R128, FFmpeg loudnorm): integrated loudness target in LUFS.</summary>
public enum LoudnessTarget { Off, Lufs14, Lufs16, Lufs23 }

/// <summary>Image targets. TGA and DDS are game texture formats (appended: saved settings store names, not numbers).</summary>
public enum ImageFormat { Keep, Png, Jpeg, WebP, Avif, Tiff, Bmp, Gif, Ico, Tga, Dds }

/// <summary>DDS block compression: BC3/DXT5 (with alpha), BC1/DXT1 (no or 1-bit alpha), uncompressed RGBA.</summary>
public enum DdsCompression { Bc3, Bc1, None }

/// <summary>Game textures: scale each side to a power of two (nearest, next lower or next higher).</summary>
public enum PowerOfTwo { Off, Nearest, Down, Up }

public enum ChromaSubsampling { S420, S444 }

public enum TiffCompression { Lzw, Zip, None }

public enum ResizeMode { Off, MaxSize, Percent, Exact }

public enum IconFit { Pad, Crop }

public enum TrimMode { Exact, Fast }

public enum LoopMode { Off, Count, Duration, Boomerang }

public enum AnimationFormat { Gif, WebP }

public enum GifDither { Bayer, FloydSteinberg, None }

public enum ExtractMode { Copy, Convert }

public enum OutputDestination { Subfolder, SameFolder, Custom }

public enum ConflictPolicy { Rename, Skip, Overwrite }

/// <summary>Output of the trim page: keep the format (fast copy or exact re-encode) or convert.</summary>
public enum TrimOutput { SameFormat, Convert }

/// <summary>Compress: hit a file size, a share of the original size, or a quality level.</summary>
public enum CompressMode { TargetSize, Percent, Quality }

/// <summary>Compress › quality mode: how strongly the file is compressed (CRF steps).</summary>
public enum CompressStrength { Light, Medium, Strong }

/// <summary>Image target of the compress page (Keep = same format as the source).</summary>
public enum CompressImageFormat { Keep, Jpeg, WebP, Avif }

/// <summary>Presets of Image › Game textures.</summary>
public enum TexturePreset { Unity, Unreal, Godot, DirectX, Legacy, Custom }

/// <summary>Presets of Audio › Game audio.</summary>
public enum GameAudioPreset { UnitySfx, UnityMusic, UnrealSfx, UnrealMusic, GodotSfx, GodotMusic, Custom }
