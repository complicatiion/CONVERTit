using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using CONVERTit.App.Localization;
using CONVERTit.App.ViewModels;
using CONVERTit.Core.Catalog;
using CONVERTit.Core.Ffmpeg;
using CONVERTit.Core.Jobs;
using CONVERTit.Core.Media;
using CONVERTit.Core.Storage;

namespace CONVERTit.App.Converters;

/// <summary>Base for one-way converters.</summary>
public abstract class OneWay : IValueConverter
{
    public abstract object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Enum value → UI label ("VideoCodec.H264" → "H.264 (AVC)").</summary>
public sealed class LabelConverter : OneWay
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null ? "" : value is Enum ? Loc.Enum(value) : value.ToString();
}

public sealed class BytesConverter : OneWay
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        long l when l > 0 => Units.Bytes(l),
        int i when i > 0 => Units.Bytes(i),
        _ => "–"
    };
}

public sealed class TimecodeConverter : OneWay
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is double d ? Timecode.Format(d, parameter as string != "short") : "";
}

public sealed class KbpsConverter : OneWay
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int i ? $"{i} kbps" : "";
}

public sealed class SampleRateConverter : OneWay
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int i ? i <= 0 ? Loc.T("Original") : $"{i / 1000d:0.###} kHz" : "";
}

public sealed class Mp3VbrConverter : OneWay
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int i ? $"V{i} · ≈ {FormatCatalog.Mp3VbrKbps(i)} kbps" : "";
}

/// <summary>Savings in percent: "−63 %" smaller, "+12 %" larger.</summary>
public sealed class PercentConverter : OneWay
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        double d when d >= 0 => $"−{d:0} %",
        double d => $"+{-d:0} %",
        _ => ""
    };
}

/// <summary>Category color: one accent for video, audio and images ("soft" = tile background).</summary>
public sealed class KindBrushConverter : OneWay
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var soft = parameter as string == "soft";
        return value is MediaKind.Video or MediaKind.Audio or MediaKind.Image
            ? Res.Brush(soft ? "BrushChip" : "BrushAccentIcon")
            : Res.Brush(soft ? "BrushNeutralSoft" : "BrushTextSecondary");
    }
}

public sealed class KindIconConverter : OneWay
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        MediaKind.Video => Res.Icon("IconVideo"),
        MediaKind.Audio => Res.Icon("IconAudio"),
        MediaKind.Image => Res.Icon("IconImage"),
        _ => Res.Icon("IconFile")
    };
}

/// <summary>
/// Queue status colors: waiting orange, running accent, done neutral, failed red. Parameter "soft" = pill background.
/// </summary>
public sealed class JobStateBrushConverter : OneWay
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var soft = parameter as string == "soft";
        return value switch
        {
            JobState.Running => Res.Brush(soft ? "BrushChip" : "BrushAccentIcon"),
            JobState.Queued => Res.Brush(soft ? "BrushOrangeSoft" : "BrushOrange"),
            JobState.Done => Res.Brush(soft ? "BrushNeutralSoft" : "BrushTextPrimary"),
            JobState.Failed => Res.Brush(soft ? "BrushDangerSoft" : "BrushDanger"),
            JobState.Skipped => Res.Brush(soft ? "BrushWarningSoft" : "BrushWarning"),
            _ => Res.Brush(soft ? "BrushNeutralSoft" : "BrushTextSecondary")
        };
    }
}

public sealed class JobStateIconConverter : OneWay
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        JobState.Running => Res.Icon("IconPlayCircle"),
        JobState.Done => Res.Icon("IconCheckCircle"),
        JobState.Failed => Res.Icon("IconAlertCircle"),
        JobState.Skipped => Res.Icon("IconChevronRight"),
        JobState.Canceled => Res.Icon("IconCancel"),
        _ => Res.Icon("IconTimer")
    };
}

/// <summary>
/// Choice label with language refresh: values[0] = item, values[1] = LocState.Version (only a trigger).
/// Parameter selects the format: kbps, rate, vbr, vorbis, flac, width, fps; default = enum label.
/// </summary>
public sealed class LabelMultiConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture) =>
        Format(values.Count > 0 ? values[0] : null, parameter as string);

    public static string Format(object? value, string? kind) => (kind, value) switch
    {
        ("kbps", int i) => $"{i} kbps",
        ("audiokbps", int i) => i <= 0 ? Loc.T("RemoveAudio") : $"{i} kbps",
        ("rate", int i) => i <= 0 ? Loc.T("Original") : $"{i / 1000d:0.###} kHz",
        ("vbr", int i) => $"V{i} · ≈ {FormatCatalog.Mp3VbrKbps(i)} kbps",
        ("vorbis", int i) => $"q{i} · ≈ {FormatCatalog.VorbisKbps(i)} kbps",
        ("flac", int i) => i == 0 ? $"0 · {Loc.T("Fastest")}" : i == 8 ? $"8 · {Loc.T("Smallest")}" : i.ToString(CultureInfo.InvariantCulture),
        ("width", int i) => i <= 0 ? Loc.T("Original") : $"{i} px",
        ("fps", int i) => $"{i} fps",
        (_, Enum e) => Loc.Enum(e),
        _ => value?.ToString() ?? ""
    };
}

/// <summary>UI text key → text (operation names, skip reasons).</summary>
public sealed class LocKeyConverter : OneWay
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string { Length: > 0 } key ? Loc.T(key) : "";
}

/// <summary>Error of a job ("Key|arg") → text.</summary>
public sealed class ErrorTextConverter : OneWay
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string s ? Loc.Error(s) : "";
}
