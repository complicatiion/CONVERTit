using System.Collections.ObjectModel;

namespace CONVERTit.Core.Jobs;

/// <summary>Thrown by job work to mark a file as skipped (e.g. already in the target format).</summary>
public sealed class JobSkippedException(string reasonKey) : Exception(reasonKey)
{
    public string ReasonKey { get; } = reasonKey;
}

/// <summary>Everything a running job may use: cancellation, progress, log, temp files.</summary>
public sealed class JobContext
{
    private readonly ConversionJob _job;
    private readonly Action<Action> _post;
    private readonly List<string> _tempFiles = [];
    private DateTime _lastReport = DateTime.MinValue;
    private DateTime _lastLog = DateTime.MinValue;

    internal JobContext(ConversionJob job, Action<Action> post, CancellationToken token)
    {
        _job = job;
        _post = post;
        CancellationToken = token;
    }

    public CancellationToken CancellationToken { get; }

    public ConversionJob Job => _job;

    public IReadOnlyList<string> TempFiles => _tempFiles;

    /// <summary>Progress 0…1 and an optional status text; throttled to ~6 UI updates per second.</summary>
    public void Report(double fraction, string? status = null, bool force = false)
    {
        var now = DateTime.UtcNow;
        if (!force && (now - _lastReport).TotalMilliseconds < 160)
        {
            return;
        }

        _lastReport = now;
        var value = Math.Clamp(fraction, 0, 1);
        _post(() =>
        {
            _job.Progress = value;
            if (status is not null)
            {
                _job.StatusText = status;
            }
        });
    }

    public void Log(string line)
    {
        _job.AppendLog(line);
        var now = DateTime.UtcNow;
        if ((now - _lastLog).TotalMilliseconds > 400)
        {
            _lastLog = now;
            _post(() => _job.LogVersion++);
        }
    }

    public void SetCommand(string commandLine)
    {
        Log("> " + commandLine);
        _post(() => _job.CommandLine = commandLine);
    }

    public void SetOutputPath(string path) => _post(() => _job.OutputPath = path);

    /// <summary>Registers a file that is deleted when the job ends (also on failure).</summary>
    public string AddTemp(string path)
    {
        _tempFiles.Add(path);
        return path;
    }

    public void Warn(string key) => _post(() =>
    {
        if (!_job.Warnings.Contains(key))
        {
            _job.Warnings.Add(key);
            _job.LogVersion++;
        }
    });
}

/// <summary>Summary of everything that ran since the queue was last idle.</summary>
public sealed record BatchSummary(int Done, int Failed, int Skipped, int Canceled, long SourceBytes, long OutputBytes, string? OutputFolder);

/// <summary>
/// Job queue with two lanes (FFmpeg and images) and an adjustable number of parallel jobs per lane.
/// All changes of <see cref="Jobs"/> and job properties happen through <c>post</c> (the UI dispatcher).
/// </summary>
public sealed class JobQueue
{
    private readonly Action<Action> _post;
    private readonly object _lock = new();
    private readonly List<ConversionJob> _waiting = [];
    private readonly HashSet<ConversionJob> _running = [];
    private readonly List<ConversionJob> _sinceIdle = [];

    public JobQueue(Action<Action> post)
    {
        _post = post;
    }

    public ObservableCollection<ConversionJob> Jobs { get; } = [];

    /// <summary>Parallel FFmpeg processes (video/audio). Video encoders already use all cores.</summary>
    public int MediaParallel { get; set; } = 1;

    /// <summary>Parallel image conversions.</summary>
    public int ImageParallel { get; set; } = Math.Clamp(Environment.ProcessorCount / 2, 1, 8);

    /// <summary>Raised on the UI thread after a job started, finished or was added.</summary>
    public event Action? Changed;

    /// <summary>Raised on the UI thread when the last job finished.</summary>
    public event Action<BatchSummary>? Drained;

    public bool IsBusy
    {
        get
        {
            lock (_lock)
            {
                return _waiting.Count > 0 || _running.Count > 0;
            }
        }
    }

    public int ActiveCount
    {
        get
        {
            lock (_lock)
            {
                return _waiting.Count + _running.Count;
            }
        }
    }

    /// <summary>Progress of all jobs since the queue was last idle (0…1).</summary>
    public double OverallProgress
    {
        get
        {
            lock (_lock)
            {
                if (_sinceIdle.Count == 0)
                {
                    return 0;
                }

                return _sinceIdle.Sum(j => j.IsFinished ? 1 : j.Progress) / _sinceIdle.Count;
            }
        }
    }

    /// <summary>Adds jobs (call on the UI thread).</summary>
    public void Enqueue(IEnumerable<ConversionJob> jobs)
    {
        var list = jobs.ToList();
        if (list.Count == 0)
        {
            return;
        }

        foreach (var job in list)
        {
            Jobs.Add(job);
        }

        lock (_lock)
        {
            _waiting.AddRange(list);
            _sinceIdle.AddRange(list);
        }

        Changed?.Invoke();
        Pump();
    }

    public void Cancel(ConversionJob job)
    {
        var removed = false;
        lock (_lock)
        {
            if (_waiting.Remove(job))
            {
                removed = true;
            }
        }

        if (removed)
        {
            job.State = JobState.Canceled;
            job.StatusText = "";
            Changed?.Invoke();
            CheckDrained();
            return;
        }

        try
        {
            job.Cancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public void CancelAll()
    {
        List<ConversionJob> waiting;
        List<ConversionJob> running;
        lock (_lock)
        {
            waiting = [.. _waiting];
            running = [.. _running];
            _waiting.Clear();
        }

        foreach (var job in waiting)
        {
            job.State = JobState.Canceled;
        }

        foreach (var job in running)
        {
            try
            {
                job.Cancellation?.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        Changed?.Invoke();
        CheckDrained();
    }

    /// <summary>Puts a finished job back into the queue.</summary>
    public void Retry(ConversionJob job)
    {
        if (!job.IsFinished)
        {
            return;
        }

        job.State = JobState.Queued;
        job.Progress = 0;
        job.StatusText = "";
        job.ErrorText = "";
        job.OutputSize = 0;
        job.StartedAt = null;
        job.FinishedAt = null;
        job.ClearLog();
        lock (_lock)
        {
            _waiting.Add(job);
            if (!_sinceIdle.Contains(job))
            {
                _sinceIdle.Add(job);
            }
        }

        Changed?.Invoke();
        Pump();
    }

    /// <summary>Removes finished jobs from the list (call on the UI thread).</summary>
    public void ClearFinished()
    {
        foreach (var job in Jobs.Where(j => j.IsFinished).ToList())
        {
            Jobs.Remove(job);
        }

        Changed?.Invoke();
    }

    public void Remove(ConversionJob job)
    {
        if (job.IsFinished)
        {
            Jobs.Remove(job);
            Changed?.Invoke();
        }
    }

    /// <summary>Starts waiting jobs while their lane has free slots.</summary>
    public void Pump()
    {
        List<ConversionJob> start = [];
        lock (_lock)
        {
            foreach (var lane in new[] { JobLane.Media, JobLane.Image })
            {
                var limit = Math.Max(1, lane == JobLane.Media ? MediaParallel : ImageParallel);
                var running = _running.Count(j => j.Lane == lane);
                foreach (var job in _waiting.Where(j => j.Lane == lane).ToList())
                {
                    if (running >= limit)
                    {
                        break;
                    }

                    _waiting.Remove(job);
                    _running.Add(job);
                    start.Add(job);
                    running++;
                }
            }
        }

        foreach (var job in start)
        {
            var cts = new CancellationTokenSource();
            job.Cancellation = cts;
            _post(() =>
            {
                job.State = JobState.Running;
                job.StartedAt = DateTime.Now;
                job.Progress = 0;
                Changed?.Invoke();
            });
            _ = Task.Run(() => RunAsync(job, cts));
        }
    }

    private async Task RunAsync(ConversionJob job, CancellationTokenSource cts)
    {
        var context = new JobContext(job, _post, cts.Token);
        JobState state;
        var error = "";
        var status = "";
        try
        {
            await job.Work(context).ConfigureAwait(false);
            cts.Token.ThrowIfCancellationRequested();
            state = JobState.Done;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            state = JobState.Canceled;
        }
        catch (JobSkippedException skipped)
        {
            state = JobState.Skipped;
            status = skipped.ReasonKey;
        }
        catch (Exception ex)
        {
            state = JobState.Failed;
            error = ex is Ffmpeg.ConversionException ce ? ce.Key + (ce.Arguments.Length > 0 ? "|" + string.Join("|", ce.Arguments) : "") : ex.Message;
            job.AppendLog("ERROR: " + ex.Message);
        }
        finally
        {
            foreach (var temp in context.TempFiles)
            {
                TryDelete(temp);
            }

            cts.Dispose();
            job.Cancellation = null;
        }

        long size = 0;
        if (state == JobState.Done)
        {
            try
            {
                size = File.Exists(job.OutputPath) ? new FileInfo(job.OutputPath).Length : 0;
            }
            catch
            {
                size = 0;
            }
        }

        lock (_lock)
        {
            _running.Remove(job);
        }

        _post(() =>
        {
            job.State = state;
            job.Progress = state == JobState.Done ? 1 : job.Progress;
            job.ErrorText = error;
            job.StatusText = status;
            job.OutputSize = size;
            job.FinishedAt = DateTime.Now;
            job.LogVersion++;
            Changed?.Invoke();
            CheckDrained();
        });

        Pump();
    }

    private void CheckDrained()
    {
        BatchSummary? summary = null;
        lock (_lock)
        {
            if (_waiting.Count == 0 && _running.Count == 0 && _sinceIdle.Count > 0)
            {
                var done = _sinceIdle.Where(j => j.State == JobState.Done).ToList();
                var folder = done.Select(j => Path.GetDirectoryName(j.OutputPath)).FirstOrDefault(d => !string.IsNullOrEmpty(d));
                summary = new BatchSummary(
                    done.Count,
                    _sinceIdle.Count(j => j.State == JobState.Failed),
                    _sinceIdle.Count(j => j.State == JobState.Skipped),
                    _sinceIdle.Count(j => j.State == JobState.Canceled),
                    done.Sum(j => j.SourceSize),
                    done.Sum(j => j.OutputSize),
                    folder);
                _sinceIdle.Clear();
            }
        }

        if (summary is not null)
        {
            Drained?.Invoke(summary);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Locked temp files are removed on the next run at the latest (they are never scanned).
        }
    }
}
