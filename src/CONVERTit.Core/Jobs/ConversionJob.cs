using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CONVERTit.Core.Media;

namespace CONVERTit.Core.Jobs;

public enum JobState { Queued, Running, Done, Failed, Canceled, Skipped }

/// <summary>FFmpeg jobs and image jobs run in separate lanes with their own parallelism.</summary>
public enum JobLane { Media, Image }

/// <summary>
/// One conversion in the queue. Observable properties are only changed on the UI thread
/// (the queue marshals all updates); the log is thread-safe.
/// </summary>
public sealed partial class ConversionJob : ObservableObject
{
    private const int MaxLogLines = 600;
    private readonly object _logLock = new();
    private readonly LinkedList<string> _log = new();

    public ConversionJob(string sourcePath, string outputPath, MediaKind kind, JobLane lane, string operationKey,
        Func<JobContext, Task> work)
    {
        SourcePath = sourcePath;
        OutputPath = outputPath;
        Kind = kind;
        Lane = lane;
        OperationKey = operationKey;
        Work = work;
        try
        {
            SourceSize = File.Exists(sourcePath) ? new FileInfo(sourcePath).Length : 0;
        }
        catch
        {
            SourceSize = 0;
        }
    }

    public Guid Id { get; } = Guid.NewGuid();

    public string SourcePath { get; }

    public string SourceName => Path.GetFileName(SourcePath);

    public MediaKind Kind { get; }

    public JobLane Lane { get; }

    /// <summary>UI text key of the operation ("OpConvertVideo", "OpTrim" …).</summary>
    public string OperationKey { get; }

    /// <summary>The work; can run again on retry.</summary>
    public Func<JobContext, Task> Work { get; }

    public long SourceSize { get; }

    /// <summary>Batch the job belongs to (one "Start" click).</summary>
    public Guid BatchId { get; init; }

    [ObservableProperty] private string _outputPath = "";
    [ObservableProperty] private JobState _state = JobState.Queued;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _errorText = "";
    [ObservableProperty] private long _outputSize;
    [ObservableProperty] private DateTime? _startedAt;
    [ObservableProperty] private DateTime? _finishedAt;
    [ObservableProperty] private string _commandLine = "";

    /// <summary>Increases whenever log lines were added (UI refresh trigger).</summary>
    [ObservableProperty] private int _logVersion;

    /// <summary>Warnings from planning (UI text keys), shown in the queue.</summary>
    public List<string> Warnings { get; } = [];

    public string OutputName => Path.GetFileName(OutputPath);

    public bool IsActive => State is JobState.Queued or JobState.Running;

    public bool IsFinished => State is JobState.Done or JobState.Failed or JobState.Canceled or JobState.Skipped;

    public bool IsFailed => State == JobState.Failed;

    /// <summary>Saved space in percent (negative = larger), null until done.</summary>
    public double? SavingsPercent =>
        State == JobState.Done && SourceSize > 0 && OutputSize > 0 ? (1d - (double)OutputSize / SourceSize) * 100d : null;

    public TimeSpan? Elapsed => StartedAt is { } s ? (FinishedAt ?? DateTime.Now) - s : null;

    internal CancellationTokenSource? Cancellation { get; set; }

    partial void OnStateChanged(JobState value)
    {
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(IsFinished));
        OnPropertyChanged(nameof(IsFailed));
        OnPropertyChanged(nameof(SavingsPercent));
    }

    partial void OnOutputSizeChanged(long value) => OnPropertyChanged(nameof(SavingsPercent));

    partial void OnOutputPathChanged(string value) => OnPropertyChanged(nameof(OutputName));

    public void AppendLog(string line)
    {
        lock (_logLock)
        {
            _log.AddLast(line);
            while (_log.Count > MaxLogLines)
            {
                _log.RemoveFirst();
            }
        }
    }

    public string LogText()
    {
        lock (_logLock)
        {
            var sb = new StringBuilder();
            foreach (var line in _log)
            {
                sb.AppendLine(line);
            }

            return sb.ToString();
        }
    }

    public void ClearLog()
    {
        lock (_logLock)
        {
            _log.Clear();
        }
    }
}
