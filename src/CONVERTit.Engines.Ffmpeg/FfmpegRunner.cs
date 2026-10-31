using System.Diagnostics;
using System.Globalization;
using System.Text;
using CONVERTit.Core.Ffmpeg;
using CONVERTit.Core.Jobs;

namespace CONVERTit.Engines.Ffmpeg;

/// <summary>FFmpeg ended with an error; Message holds its last error lines.</summary>
public sealed class FfmpegException(string message, int exitCode) : Exception(message)
{
    public int ExitCode { get; } = exitCode;
}

/// <summary>Runs ffmpeg/ffprobe as child processes with progress, log, priority and cancellation.</summary>
public sealed class FfmpegRunner(FfmpegLocator locator)
{
    public FfmpegLocator Locator { get; } = locator;

    /// <summary>Priority of conversion processes (below normal keeps the PC responsive).</summary>
    public ProcessPriorityClass Priority { get; set; } = ProcessPriorityClass.BelowNormal;

    /// <summary>Hook for the platform layer (e.g. Windows job object: children die with the app).</summary>
    public Action<Process>? ProcessStarted { get; set; }

    /// <summary>Runs one conversion step and maps its progress into [from, to] of the job.</summary>
    public async Task RunAsync(FfmpegCommand command, JobContext context, double from = 0, double to = 1)
    {
        var ffmpeg = Locator.FfmpegPath ?? throw new ConversionException("ErrFfmpegMissing");
        var args = new List<string> { "-hide_banner", "-nostdin", "-y", "-loglevel", "warning", "-progress", "pipe:1", "-nostats" };
        args.AddRange(command.Args);
        context.SetCommand(FfmpegCommands.Display(args));

        var errors = new LinkedList<string>();
        var parser = new ProgressParser();
        using var process = Create(ffmpeg, args);
        process.Start();
        TrySetPriority(process, Priority);
        ProcessStarted?.Invoke(process);

        await using var registration = context.CancellationToken.Register(() => Kill(process));

        var stdout = Task.Run(async () =>
        {
            while (await process.StandardOutput.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                var snapshot = parser.Feed(line);
                if (snapshot is null)
                {
                    continue;
                }

                var fraction = ProgressParser.Fraction(snapshot, command.ExpectedSeconds);
                var status = snapshot.Speed > 0
                    ? string.Create(CultureInfo.InvariantCulture, $"{snapshot.Speed:0.##}× · {Timecode.Format(snapshot.OutSeconds, false)}")
                    : Timecode.Format(snapshot.OutSeconds, false);
                context.Report(from + (to - from) * fraction, status, snapshot.Ended);
            }
        });

        var stderr = Task.Run(async () =>
        {
            while (await process.StandardError.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                if (line.Length == 0)
                {
                    continue;
                }

                context.Log(line);
                lock (errors)
                {
                    errors.AddLast(line);
                    if (errors.Count > 6)
                    {
                        errors.RemoveFirst();
                    }
                }
            }
        });

        await process.WaitForExitAsync().ConfigureAwait(false);
        await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
        context.CancellationToken.ThrowIfCancellationRequested();

        if (process.ExitCode != 0)
        {
            string message;
            lock (errors)
            {
                message = errors.Count > 0 ? string.Join(Environment.NewLine, errors) : $"FFmpeg exit code {process.ExitCode}";
            }

            throw new FfmpegException(message, process.ExitCode);
        }

        context.Report(to, null, force: true);
    }

    /// <summary>Runs a short command and returns stdout + stderr as text (version, encoders, probe).</summary>
    public async Task<(int ExitCode, string Output, string Error)> CaptureAsync(string executable, IEnumerable<string> args,
        TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        using var process = Create(executable, args);
        process.Start();
        ProcessStarted?.Invoke(process);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);
        await using var registration = cts.Token.Register(() => Kill(process));

        var output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var error = process.StandardError.ReadToEndAsync(CancellationToken.None);
        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        var text = await output.ConfigureAwait(false);
        var err = await error.ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return (process.ExitCode, text, err);
    }

    /// <summary>Runs ffmpeg and returns its binary stdout (frame grabs as PNG).</summary>
    public async Task<byte[]?> CaptureBytesAsync(IEnumerable<string> args, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var ffmpeg = Locator.FfmpegPath;
        if (ffmpeg is null)
        {
            return null;
        }

        var all = new List<string> { "-hide_banner", "-nostdin", "-loglevel", "error" };
        all.AddRange(args);
        using var process = Create(ffmpeg, all, binaryOutput: true);
        process.Start();
        ProcessStarted?.Invoke(process);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);
        await using var registration = cts.Token.Register(() => Kill(process));

        using var memory = new MemoryStream();
        var copy = process.StandardOutput.BaseStream.CopyToAsync(memory, CancellationToken.None);
        var drain = process.StandardError.ReadToEndAsync(CancellationToken.None);
        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        await copy.ConfigureAwait(false);
        await drain.ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return process.ExitCode == 0 && memory.Length > 0 ? memory.ToArray() : null;
    }

    /// <summary>First line of "ffmpeg -version", e.g. "ffmpeg version n8.0-…".</summary>
    public async Task<string?> VersionAsync(CancellationToken cancellationToken = default)
    {
        if (Locator.FfmpegPath is null)
        {
            return null;
        }

        try
        {
            var (code, output, _) = await CaptureAsync(Locator.FfmpegPath, ["-hide_banner", "-version"], TimeSpan.FromSeconds(10), cancellationToken)
                .ConfigureAwait(false);
            if (code != 0)
            {
                return null;
            }

            var first = output.Split('\n').FirstOrDefault()?.Trim() ?? "";
            const string prefix = "ffmpeg version ";
            if (first.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                first = first[prefix.Length..];
            }

            var space = first.IndexOf(' ');
            return space > 0 ? first[..space] : first;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    private static Process Create(string executable, IEnumerable<string> args, bool binaryOutput = false)
    {
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            StandardErrorEncoding = Encoding.UTF8
        };
        if (!binaryOutput)
        {
            info.StandardOutputEncoding = Encoding.UTF8;
        }

        foreach (var arg in args)
        {
            info.ArgumentList.Add(arg);
        }

        return new Process { StartInfo = info };
    }

    private static void TrySetPriority(Process process, ProcessPriorityClass priority)
    {
        try
        {
            process.PriorityClass = priority;
        }
        catch
        {
            // Process already gone or not allowed.
        }
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Already exited.
        }
    }
}
