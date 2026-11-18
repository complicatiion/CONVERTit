using CONVERTit.App.Localization;
using CONVERTit.Core.Media;
using CONVERTit.Platform.Windows;

namespace CONVERTit.App.Services;

/// <summary>Which file types CONVERTit offers itself for (Settings › Windows integration › File associations).</summary>
public enum AssociationMode { Off, Recommended, All, Custom }

/// <summary>
/// Registers CONVERTit for file types: "Open with" entry and Settings › Default apps. Windows decides the default
/// app – the user confirms it there; nothing is taken away from other programs.
/// </summary>
public static class FileAssociationService
{
    /// <summary>
    /// Recommended: formats Windows has no good built-in program for, or that are mostly converted anyway.
    /// Common formats (MP4, MP3, JPG, PNG) stay with the programs the user already has.
    /// </summary>
    public static readonly IReadOnlySet<string> Recommended = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "mkv", "webm", "flv", "vob", "mxf", "m2ts", "ts", "ogv", "3gp",
        "flac", "ogg", "oga", "opus", "ape", "wv", "aiff", "aif", "mka",
        "webp", "avif", "heic", "heif", "tga", "dds", "psd", "jxl", "qoi", "exr", "hdr", "pcx"
    };

    /// <summary>Extensions for the mode (custom: the user's list, only supported types).</summary>
    public static IReadOnlyList<string> ExtensionsFor(AssociationMode mode, string custom)
    {
        var all = MediaTypes.VideoExtensions.Concat(MediaTypes.AudioExtensions).Concat(MediaTypes.ImageExtensions).ToList();
        return mode switch
        {
            AssociationMode.All => all,
            AssociationMode.Recommended => all.Where(Recommended.Contains).ToList(),
            AssociationMode.Custom => custom.Split([',', ';', ' ', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(e => e.TrimStart('*', '.').ToLowerInvariant()).Where(all.Contains).Distinct().ToList(),
            _ => []
        };
    }

    /// <summary>Writes or removes the registration for the current settings. Returns the number of file types.</summary>
    public static int Apply(AppSettings settings)
    {
        if (!OperatingSystem.IsWindows())
        {
            return 0;
        }

        var extensions = ExtensionsFor(settings.Associations, settings.CustomAssociations);
        if (extensions.Count == 0)
        {
            FileAssociations.Unregister();
            return 0;
        }

        var groups = new[]
        {
            new AssociationGroup("CONVERTit.Video", Loc.T("AssocVideo"), extensions.Where(MediaTypes.VideoExtensions.Contains).ToList()),
            new AssociationGroup("CONVERTit.Audio", Loc.T("AssocAudio"), extensions.Where(MediaTypes.AudioExtensions.Contains).ToList()),
            new AssociationGroup("CONVERTit.Image", Loc.T("AssocImage"), extensions.Where(MediaTypes.ImageExtensions.Contains).ToList())
        };
        FileAssociations.Register(AppEnvironment.ExecutablePath, Loc.T("AssocDescription"), groups.Where(g => g.Extensions.Count > 0));
        return extensions.Count;
    }
}
