using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CONVERTit.App.Localization;
using CONVERTit.Core.Ffmpeg;
using CONVERTit.Core.Jobs;
using CONVERTit.Core.Media;
using CONVERTit.Core.Storage;

namespace CONVERTit.App.ViewModels;

/// <summary>
/// Base of all conversion pages: source list (files or folders), output card, size estimate and the
/// "Add to queue" action in the footer.
/// </summary>
public abstract partial class ConvertPageViewModel : PageViewModel
{
    protected ConvertPageViewModel(MainViewModel main, IReadOnlySet<MediaKind> kinds, MediaKind folderKind, bool singleFile = false,
        string? miniPlayerKey = null)
        : base(main)
    {
        Sources = new SourceListViewModel(main, kinds, folderKind, singleFile) { MiniPlayerKey = miniPlayerKey };
        Output = new OutputViewModel(main);
        Sources.ExcludedFolders = root => Output.ExcludedFolders(root);
        Sources.Changed += () =>
        {
            UpdateEstimate();
            StartCommand.NotifyCanExecuteChanged();
            StartSelectedCommand.NotifyCanExecuteChanged();
        };
        Sources.SelectionSetChanged += () => StartSelectedCommand.NotifyCanExecuteChanged();
        main.FfmpegChanged += () =>
        {
            OnPropertyChanged(nameof(ShowFfmpegBanner));
            StartCommand.NotifyCanExecuteChanged();
        };
    }

    public SourceListViewModel Sources { get; }

    public OutputViewModel Output { get; }

    /// <summary>Video and audio pages need FFmpeg; image pages do not.</summary>
    public virtual bool NeedsFfmpeg => true;

    public bool ShowFfmpegBanner => NeedsFfmpeg && !Main.Locator.IsAvailable;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartSelectedCommand))]
    private bool _isPlanning;

    [ObservableProperty] private string _estimateText = "";

    public bool HasEstimate => EstimateText.Length > 0;

    partial void OnEstimateTextChanged(string value) => OnPropertyChanged(nameof(HasEstimate));

    protected bool CanStart() => Sources.HasItems && !IsPlanning && (!NeedsFfmpeg || Main.Locator.IsAvailable) && ExtraCanStart();

    protected virtual bool ExtraCanStart() => true;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        IsPlanning = true;
        try
        {
            var jobs = await PlanAsync();
            if (jobs.Count > 0)
            {
                Main.Queue.Enqueue(jobs);
                Main.ShowToast(Loc.F("ToastQueued", jobs.Count));
                Main.SaveState();
            }
        }
        catch (ConversionException ex)
        {
            Main.ShowToast(Loc.F(ex.Key, ex.Arguments), error: true);
        }
        catch (Exception ex)
        {
            Main.ShowToast(ex.Message, error: true);
        }
        finally
        {
            IsPlanning = false;
        }
    }

    private bool CanStartSelected() => CanStart() && Sources.HasPartialSelection;

    /// <summary>
    /// Only the selected files, with the current settings; they leave the list afterwards, so the remaining files
    /// can get other settings.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanStartSelected))]
    private async Task StartSelectedAsync()
    {
        var selected = Sources.Selection.ToList();
        IsPlanning = true;
        Sources.RestrictToSelection = true;
        try
        {
            var jobs = await PlanAsync();
            if (jobs.Count > 0)
            {
                Main.Queue.Enqueue(jobs);
                Main.ShowToast(Loc.F("ToastQueued", jobs.Count));
                Sources.RemoveItems(selected);
                Main.SaveState();
            }
        }
        catch (ConversionException ex)
        {
            Main.ShowToast(Loc.F(ex.Key, ex.Arguments), error: true);
        }
        catch (Exception ex)
        {
            Main.ShowToast(ex.Message, error: true);
        }
        finally
        {
            Sources.RestrictToSelection = false;
            IsPlanning = false;
        }
    }

    [RelayCommand]
    private void DownloadFfmpeg() => Main.OpenDependencies(startDownload: true);

    /// <summary>Creates the jobs (options are cloned inside).</summary>
    protected abstract Task<List<ConversionJob>> PlanAsync();

    /// <summary>Estimated output size of one file, null = unknown.</summary>
    protected virtual long? Estimate(SourceItem item) => null;

    /// <summary>Recalculates "≈ 1.2 GB" for the footer (after option or list changes).</summary>
    protected void UpdateEstimate()
    {
        long total = 0;
        var known = 0;
        foreach (var item in Sources.Items)
        {
            if (Estimate(item) is { } bytes)
            {
                total += bytes;
                known++;
            }
        }

        EstimateText = known == 0 ? "" : known == Sources.Items.Count
            ? Loc.F("EstimateAll", Units.Bytes(total), Units.Bytes(Sources.Items.Sum(i => i.Size)))
            : Loc.F("EstimatePart", Units.Bytes(total), known, Sources.Items.Count);
    }

    public override bool AcceptDrop(IReadOnlyList<string> paths)
    {
        if (!Sources.CanAccept(paths))
        {
            return false;
        }

        _ = Sources.AddPathsAsync(paths);
        return true;
    }

    public override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        Sources.RefreshTexts();
        Output.Refresh();
        UpdateEstimate();
    }
}
