using System.Diagnostics;

namespace CONVERTit.Platform.Windows;

/// <summary>Kinds of Windows caches CONVERTit can reset.</summary>
public enum ShellCacheKind
{
    /// <summary>Explorer thumbnails (thumbcache_*.db).</summary>
    Thumbnails,

    /// <summary>Icon cache (iconcache_*.db and IconCache.db).</summary>
    Icons
}

/// <summary>Result of a cleaning run.</summary>
public sealed record ShellCacheResult(int Deleted, long FreedBytes, int Locked);

/// <summary>
/// Resets the Explorer thumbnail and icon caches of the current user (no administrator rights). Windows creates the
/// files again when it needs them. Explorer keeps them open, so they can optionally be deleted while Explorer is
/// briefly stopped: only the explorer.exe processes of this session are ended and Explorer is started again.
/// </summary>
public static class ShellCacheCleaner
{
    public static string ExplorerCacheFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Explorer");

    /// <summary>Cache files of a kind (only existing files).</summary>
    public static IReadOnlyList<string> Files(ShellCacheKind kind)
    {
        var result = new List<string>();
        try
        {
            if (Directory.Exists(ExplorerCacheFolder))
            {
                result.AddRange(Directory.GetFiles(ExplorerCacheFolder, kind == ShellCacheKind.Thumbnails ? "thumbcache_*.db" : "iconcache_*.db"));
            }

            if (kind == ShellCacheKind.Icons)
            {
                var legacy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IconCache.db");
                if (File.Exists(legacy))
                {
                    result.Add(legacy);
                }
            }
        }
        catch
        {
            // Folder not readable: nothing to show.
        }

        return result;
    }

    public static (int Count, long Bytes) Measure(ShellCacheKind kind)
    {
        long bytes = 0;
        var files = Files(kind);
        foreach (var file in files)
        {
            try
            {
                bytes += new FileInfo(file).Length;
            }
            catch
            {
                // Vanished in between.
            }
        }

        return (files.Count, bytes);
    }

    /// <summary>
    /// Deletes the cache files. With <paramref name="restartExplorer"/> Explorer is stopped first (taskbar and desktop
    /// disappear for a moment, open Explorer windows close) and started again afterwards; files that are still in use
    /// are counted as locked.
    /// </summary>
    public static ShellCacheResult Clean(IReadOnlyCollection<ShellCacheKind> kinds, bool restartExplorer)
    {
        var files = kinds.SelectMany(Files).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var stopped = restartExplorer && OperatingSystem.IsWindows() && StopExplorer();
        try
        {
            int deleted = 0, locked = 0;
            long freed = 0;
            foreach (var file in files)
            {
                long size = 0;
                try
                {
                    size = new FileInfo(file).Length;
                }
                catch
                {
                    // Size unknown.
                }

                if (TryDelete(file, stopped ? 10 : 1))
                {
                    deleted++;
                    freed += size;
                }
                else
                {
                    locked++;
                }
            }

            return new ShellCacheResult(deleted, freed, locked);
        }
        finally
        {
            if (stopped)
            {
                StartExplorer();
            }
        }
    }

    /// <summary>Retries a few times: other processes (thumbnail providers) release their handles shortly after Explorer.</summary>
    private static bool TryDelete(string file, int attempts)
    {
        for (var i = 0; i < attempts; i++)
        {
            try
            {
                if (!File.Exists(file))
                {
                    return true;
                }

                File.SetAttributes(file, FileAttributes.Normal);
                File.Delete(file);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (i + 1 < attempts)
                {
                    Thread.Sleep(200);
                }
            }
        }

        return false;
    }

    /// <summary>Ends explorer.exe of the current session only (other users keep theirs).</summary>
    private static bool StopExplorer()
    {
        var session = Process.GetCurrentProcess().SessionId;
        var any = false;
        foreach (var process in Process.GetProcessesByName("explorer"))
        {
            try
            {
                if (process.SessionId != session)
                {
                    continue;
                }

                process.Kill();
                process.WaitForExit(5000);
                any = true;
            }
            catch
            {
                // Already gone or not ours.
            }
            finally
            {
                process.Dispose();
            }
        }

        return any;
    }

    private static void StartExplorer()
    {
        try
        {
            var session = Process.GetCurrentProcess().SessionId;
            // Windows may have restarted the shell by itself already (AutoRestartShell).
            Thread.Sleep(500);
            if (Process.GetProcessesByName("explorer").Any(p => p.SessionId == session))
            {
                return;
            }

            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch
        {
            // The user can start it with Ctrl+Shift+Esc → Run new task → explorer.
        }
    }
}
