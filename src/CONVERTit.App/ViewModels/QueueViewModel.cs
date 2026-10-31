using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CONVERTit.App.Localization;
using CONVERTit.App.Services;
using CONVERTit.Core.Jobs;
using CONVERTit.Core.Storage;

namespace CONVERTit.App.ViewModels;

/// <summary>Queue: all jobs with progress, result and console output of the selected job.</summary>
public sealed partial class QueueViewModel : PageViewModel
{
    public QueueViewModel(MainViewModel main) : base(main)
    {
        main.Queue.Changed += Refresh;
    }

    public override string TitleKey => "NavQueue";

    public override string IconKey => "IconQueue";

    public JobQueue Queue => Main.Queue;

    [ObservableProperty] private ConversionJob? _selectedJob;
    [ObservableProperty] private string _logText = "";
    [ObservableProperty] private bool _showConsole = true;

    public int RunningCount => Queue.Jobs.Count(j => j.State == JobState.Running);
    public int WaitingCount => Queue.Jobs.Count(j => j.State == JobState.Queued);
    public int DoneCount => Queue.Jobs.Count(j => j.State == JobState.Done);
    public int FailedCount => Queue.Jobs.Count(j => j.State == JobState.Failed);
    public bool IsEmpty => Queue.Jobs.Count == 0;
    public bool HasJobs => Queue.Jobs.Count > 0;
    public bool IsBusy => Queue.IsBusy;
    public double OverallProgress => Queue.OverallProgress * 100;

    /// <summary>"3 done · saved 1.2 GB (−41 %)".</summary>
    public string SavedText
    {
        get
        {
            var done = Queue.Jobs.Where(j => j.State == JobState.Done && j.SourceSize > 0 && j.OutputSize > 0).ToList();
            if (done.Count == 0)
            {
                return "";
            }

            var before = done.Sum(j => j.SourceSize);
            var after = done.Sum(j => j.OutputSize);
            var percent = (1 - (double)after / before) * 100;
            return Loc.F(after <= before ? "QueueSaved" : "QueueGrew", Units.Bytes(Math.Abs(before - after)), Math.Abs(percent).ToString("0"));
        }
    }

    public bool SelectedHasError => SelectedJob?.State == JobState.Failed;

    public string SelectedError => SelectedJob is null ? "" : Loc.Error(SelectedJob.ErrorText);

    public string SelectedWarnings => SelectedJob is null ? "" : string.Join(Environment.NewLine, SelectedJob.Warnings.Select(Loc.T));

    public bool SelectedHasWarnings => SelectedJob?.Warnings.Count > 0;

    partial void OnSelectedJobChanged(ConversionJob? oldValue, ConversionJob? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.PropertyChanged -= OnSelectedJobPropertyChanged;
        }

        if (newValue is not null)
        {
            newValue.PropertyChanged += OnSelectedJobPropertyChanged;
        }

        UpdateLog();
        OnPropertyChanged(nameof(SelectedHasError));
        OnPropertyChanged(nameof(SelectedError));
        OnPropertyChanged(nameof(SelectedWarnings));
        OnPropertyChanged(nameof(SelectedHasWarnings));
    }

    private void OnSelectedJobPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ConversionJob.LogVersion) or nameof(ConversionJob.CommandLine))
        {
            UpdateLog();
            OnPropertyChanged(nameof(SelectedWarnings));
            OnPropertyChanged(nameof(SelectedHasWarnings));
        }
        else if (e.PropertyName is nameof(ConversionJob.State) or nameof(ConversionJob.ErrorText))
        {
            OnPropertyChanged(nameof(SelectedHasError));
            OnPropertyChanged(nameof(SelectedError));
            OnPropertyChanged(nameof(SelectedWarnings));
            OnPropertyChanged(nameof(SelectedHasWarnings));
        }
    }

    private void UpdateLog() => LogText = SelectedJob?.LogText() ?? "";

    [RelayCommand]
    private void Cancel(ConversionJob? job)
    {
        if (job is not null)
        {
            Queue.Cancel(job);
        }
    }

    [RelayCommand]
    private void Retry(ConversionJob? job)
    {
        if (job is not null)
        {
            Queue.Retry(job);
        }
    }

    [RelayCommand]
    private void Remove(ConversionJob? job)
    {
        if (job is not null)
        {
            if (ReferenceEquals(job, SelectedJob))
            {
                SelectedJob = null;
            }

            Queue.Remove(job);
        }
    }

    [RelayCommand]
    private void ShowOutput(ConversionJob? job)
    {
        if (job is not null)
        {
            AppEnvironment.ShowInExplorer(File.Exists(job.OutputPath) ? job.OutputPath : Path.GetDirectoryName(job.OutputPath));
        }
    }

    [RelayCommand]
    private void OpenOutput(ConversionJob? job) => AppEnvironment.OpenFile(job?.OutputPath);

    [RelayCommand]
    private async Task CancelAllAsync()
    {
        if (await Host.ConfirmAsync(Loc.T("ConfirmCancelAllTitle"), Loc.T("ConfirmCancelAllText"), Loc.T("BtnCancelAll"), danger: true, confirmIcon: "IconCancel"))
        {
            Queue.CancelAll();
        }
    }

    [RelayCommand]
    private void ClearFinished()
    {
        if (SelectedJob?.IsFinished == true)
        {
            SelectedJob = null;
        }

        Queue.ClearFinished();
    }

    [RelayCommand]
    private async Task CopyCommandAsync()
    {
        if (SelectedJob is { CommandLine.Length: > 0 } job)
        {
            await Host.CopyTextAsync(job.CommandLine);
            Main.ShowToast(Loc.T("ToastCopied"));
        }
    }

    [RelayCommand]
    private void ToggleConsole() => ShowConsole = !ShowConsole;

    public void Refresh()
    {
        OnPropertyChanged(nameof(RunningCount));
        OnPropertyChanged(nameof(WaitingCount));
        OnPropertyChanged(nameof(DoneCount));
        OnPropertyChanged(nameof(FailedCount));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasJobs));
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(OverallProgress));
        OnPropertyChanged(nameof(SavedText));
    }
}
