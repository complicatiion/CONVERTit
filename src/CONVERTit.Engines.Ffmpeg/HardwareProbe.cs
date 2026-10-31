using System.Text.RegularExpressions;
using CONVERTit.Core.Catalog;

namespace CONVERTit.Engines.Ffmpeg;

/// <summary>Encoders of the installed FFmpeg and which hardware encoders actually work on this PC.</summary>
public sealed record EncoderReport(string FfmpegVersion, IReadOnlySet<string> Encoders, IReadOnlySet<string> WorkingHardware)
{
    public static EncoderReport Empty { get; } = new("", new HashSet<string>(), new HashSet<string>());

    public bool Has(string encoder) => Encoders.Contains(encoder);
}

public static partial class HardwareProbe
{
    /// <summary>
    /// Lists encoders ("ffmpeg -encoders") and test-encodes one second with every hardware encoder:
    /// an encoder in the list says nothing about the GPU or driver being present.
    /// </summary>
    public static async Task<EncoderReport> ProbeAsync(FfmpegRunner runner, CancellationToken cancellationToken = default)
    {
        var ffmpeg = runner.Locator.FfmpegPath;
        if (ffmpeg is null)
        {
            return EncoderReport.Empty;
        }

        var version = await runner.VersionAsync(cancellationToken).ConfigureAwait(false) ?? "";
        var (_, output, _) = await runner.CaptureAsync(ffmpeg, ["-hide_banner", "-encoders"], TimeSpan.FromSeconds(15), cancellationToken)
            .ConfigureAwait(false);
        var encoders = ParseEncoders(output);

        var working = new HashSet<string>(StringComparer.Ordinal);
        foreach (var encoder in FormatCatalog.HardwareEncoders.Where(encoders.Contains))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var (code, _, _) = await runner.CaptureAsync(ffmpeg,
                [
                    "-hide_banner", "-nostdin", "-loglevel", "error",
                    "-f", "lavfi", "-i", "color=c=black:s=640x360:r=30:d=1",
                    "-frames:v", "10", "-pix_fmt", encoder.EndsWith("_nvenc", StringComparison.Ordinal) ? "yuv420p" : "nv12",
                    "-c:v", encoder, "-f", "null", "-"
                ], TimeSpan.FromSeconds(20), cancellationToken).ConfigureAwait(false);
                if (code == 0)
                {
                    working.Add(encoder);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Not usable.
            }
        }

        return new EncoderReport(version, encoders, working);
    }

    /// <summary>Encoder names from "ffmpeg -encoders" (lines like " V....D libx264   libx264 H.264 …").</summary>
    public static HashSet<string> ParseEncoders(string text)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in EncoderLine().Matches(text))
        {
            result.Add(m.Groups[1].Value);
        }

        return result;
    }

    [GeneratedRegex(@"^\s*[VAS][F\.][S\.][X\.][B\.][D\.]\s+(\S+)", RegexOptions.Multiline)]
    private static partial Regex EncoderLine();
}
