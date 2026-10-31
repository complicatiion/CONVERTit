using CONVERTit.Core.Media;
using CONVERTit.Core.Options;

namespace CONVERTit.Core.Files;

/// <summary>Collects the files of a folder batch.</summary>
public static class FileScanner
{
    /// <summary>
    /// Files of the given kind (or the given extensions) below <paramref name="root"/>,
    /// skipping hidden/system files, temp files of CONVERTit and the excluded output folders.
    /// </summary>
    public static List<string> Scan(string root, MediaKind kind, SourceOptions options, IEnumerable<string>? excludedFolders = null,
        CancellationToken cancellationToken = default)
    {
        var result = new List<string>();
        if (!Directory.Exists(root))
        {
            return result;
        }

        var extensions = options.AllOfKind || string.IsNullOrWhiteSpace(options.Extensions)
            ? (kind == MediaKind.Unknown
                ? MediaTypes.VideoExtensions.Concat(MediaTypes.AudioExtensions).Concat(MediaTypes.ImageExtensions).ToHashSet(StringComparer.OrdinalIgnoreCase)
                : MediaTypes.ExtensionsOf(kind).ToHashSet(StringComparer.OrdinalIgnoreCase))
            : MediaTypes.ParseExtensionList(options.Extensions);

        var excluded = (excludedFolders ?? [])
            .Select(f => Path.TrimEndingDirectorySeparator(Path.GetFullPath(f)) + Path.DirectorySeparatorChar)
            .ToList();

        var enumeration = new EnumerationOptions
        {
            RecurseSubdirectories = options.Recursive,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
            ReturnSpecialDirectories = false
        };

        foreach (var file in Directory.EnumerateFiles(root, "*", enumeration))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!extensions.Contains(MediaTypes.Extension(file)) || IsTempFile(file))
            {
                continue;
            }

            var full = Path.GetFullPath(file);
            if (excluded.Any(e => full.StartsWith(e, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            result.Add(full);
        }

        result.Sort(StringComparer.OrdinalIgnoreCase);
        return result;
    }

    /// <summary>Unfinished outputs of CONVERTit (name.cvt-xxxxxx.ext).</summary>
    public static bool IsTempFile(string path) =>
        Path.GetFileNameWithoutExtension(path).Contains(".cvt", StringComparison.OrdinalIgnoreCase)
        && System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileNameWithoutExtension(path), @"\.cvt[a-z0-9]*-[0-9a-f]{6}$");
}
