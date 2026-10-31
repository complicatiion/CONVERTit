using CONVERTit.Core.Options;

namespace CONVERTit.Core.Files;

/// <summary>Planned output file; Skip when the conflict policy says so.</summary>
public sealed record OutputPlan(string Path, bool Skip);

/// <summary>
/// Turns a source file into its output path (destination, mirrored folders, suffix, conflicts).
/// Paths planned in one batch are reserved, so two jobs never write the same file.
/// The source file itself is never a valid output.
/// </summary>
public sealed class OutputPathResolver(OutputOptions options)
{
    private readonly HashSet<string> _reserved = new(StringComparer.OrdinalIgnoreCase);

    public OutputOptions Options { get; } = options;

    /// <param name="source">Source file.</param>
    /// <param name="sourceRoot">Root folder of a folder batch (for mirrored structure), otherwise null.</param>
    /// <param name="extension">Target extension without dot.</param>
    /// <param name="extraSuffix">Additional suffix, e.g. "_track2" or "_trim".</param>
    public OutputPlan Plan(string source, string? sourceRoot, string extension, string extraSuffix = "")
    {
        var directory = Directory(source, sourceRoot);
        var stem = System.IO.Path.GetFileNameWithoutExtension(source);
        var name = SafeName(stem + Options.Suffix + extraSuffix);
        var candidate = System.IO.Path.Combine(directory, name + "." + extension.TrimStart('.'));

        // Never overwrite the source.
        if (string.Equals(System.IO.Path.GetFullPath(candidate), System.IO.Path.GetFullPath(source), StringComparison.OrdinalIgnoreCase))
        {
            name += "_converted";
            candidate = System.IO.Path.Combine(directory, name + "." + extension.TrimStart('.'));
        }

        var exists = File.Exists(candidate) || _reserved.Contains(candidate);
        if (exists)
        {
            switch (Options.Conflict)
            {
                case ConflictPolicy.Skip:
                    return new OutputPlan(candidate, true);
                case ConflictPolicy.Overwrite when !_reserved.Contains(candidate):
                    break;
                default:
                    candidate = NextFree(directory, name, extension);
                    break;
            }
        }

        _reserved.Add(candidate);
        return new OutputPlan(candidate, false);
    }

    /// <summary>Target folder for a source file.</summary>
    public string Directory(string source, string? sourceRoot)
    {
        var sourceDir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(source)) ?? "";
        var relative = "";
        if (Options.MirrorStructure && !string.IsNullOrEmpty(sourceRoot))
        {
            var rel = System.IO.Path.GetRelativePath(System.IO.Path.GetFullPath(sourceRoot), sourceDir);
            if (rel != "." && !rel.StartsWith("..", StringComparison.Ordinal))
            {
                relative = rel;
            }
        }

        return Options.Destination switch
        {
            OutputDestination.SameFolder => sourceDir,
            OutputDestination.Custom when !string.IsNullOrWhiteSpace(Options.CustomFolder) =>
                System.IO.Path.Combine(Options.CustomFolder.Trim(), relative),
            _ => !string.IsNullOrEmpty(sourceRoot) && Options.MirrorStructure
                // Folder batch: one "converted" folder at the root, mirrored structure below.
                ? System.IO.Path.Combine(System.IO.Path.GetFullPath(sourceRoot), SubfolderName, relative)
                : !string.IsNullOrEmpty(sourceRoot)
                    ? System.IO.Path.Combine(System.IO.Path.GetFullPath(sourceRoot), SubfolderName)
                    : System.IO.Path.Combine(sourceDir, SubfolderName)
        };
    }

    /// <summary>Folders that must not be scanned again (the batch's own output).</summary>
    public IEnumerable<string> ExcludedFolders(string sourceRoot)
    {
        switch (Options.Destination)
        {
            case OutputDestination.Subfolder:
                yield return System.IO.Path.Combine(System.IO.Path.GetFullPath(sourceRoot), SubfolderName);
                break;
            case OutputDestination.Custom when !string.IsNullOrWhiteSpace(Options.CustomFolder):
                yield return System.IO.Path.GetFullPath(Options.CustomFolder.Trim());
                break;
        }
    }

    private string SubfolderName =>
        string.IsNullOrWhiteSpace(Options.SubfolderName) ? "converted" : SafeName(Options.SubfolderName.Trim());

    private string NextFree(string directory, string name, string extension)
    {
        for (var i = 2; i < 10_000; i++)
        {
            var path = System.IO.Path.Combine(directory, $"{name} ({i}).{extension.TrimStart('.')}");
            if (!File.Exists(path) && !_reserved.Contains(path))
            {
                return path;
            }
        }

        return System.IO.Path.Combine(directory, $"{name} ({Guid.NewGuid():N}).{extension.TrimStart('.')}");
    }

    private static string SafeName(string name)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars().Concat(['<', '>', ':', '"', '/', '\\', '|', '?', '*']).ToHashSet();
        var chars = name.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        var result = new string(chars).Trim().TrimEnd('.');
        return result.Length == 0 ? "output" : result;
    }

    /// <summary>Temporary file next to the final output (same extension, so FFmpeg picks the right muxer).</summary>
    public static string TempPathFor(string output, string tag = "")
    {
        var dir = System.IO.Path.GetDirectoryName(output) ?? "";
        var stem = System.IO.Path.GetFileNameWithoutExtension(output);
        var ext = System.IO.Path.GetExtension(output);
        return System.IO.Path.Combine(dir, $"{stem}.cvt{tag}-{Guid.NewGuid().ToString("N")[..6]}{ext}");
    }
}
