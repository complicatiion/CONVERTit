namespace CONVERTit.Core.Ffmpeg;

/// <summary>
/// Machine-dependent settings for video encodes (not part of the page options): hardware decoding, the full GPU
/// pipeline ("GPU turbo"), CPU threads of the performance profile and the automatic picture corrections.
/// They change speed and load, never the target format or quality.
/// </summary>
public sealed record EncodeEnvironment
{
    public static EncodeEnvironment Default { get; } = new();

    /// <summary>Decode the source on the GPU ("-hwaccel auto"); frames go back to the CPU filters.</summary>
    public bool HardwareDecode { get; init; }

    /// <summary>
    /// NVIDIA/Intel: decode, scale and encode on the GPU when the job allows it (no fades, frame rate change,
    /// tone mapping …); otherwise the normal path is used. A failed run is repeated without it.
    /// </summary>
    public bool GpuTurbo { get; init; }

    /// <summary>CPU threads per video encode (0 = all, FFmpeg decides).</summary>
    public int Threads { get; init; }

    /// <summary>HDR sources become SDR (tone mapping) unless the target keeps 10-bit HDR.</summary>
    public bool HdrToSdr { get; init; } = true;

    /// <summary>Interlaced sources are deinterlaced (bwdif, only frames marked as interlaced).</summary>
    public bool AutoDeinterlace { get; init; } = true;
}
