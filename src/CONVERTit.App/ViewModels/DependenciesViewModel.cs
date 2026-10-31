using System.Runtime.InteropServices;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CONVERTit.App.Localization;
using CONVERTit.App.Services;
using CONVERTit.Core.Catalog;
using CONVERTit.Core.Storage;
using CONVERTit.Engines.Ffmpeg;
using CONVERTit.Engines.Image;

namespace CONVERTit.App.ViewModels;

/// <summary>Hardware encoder row.</summary>
public sealed record EncoderRow(string Encoder, string Vendor, string Codec, bool Available, bool Works)
{
    public string State => Loc.T(Works ? "EncoderWorks" : Available ? "EncoderNotUsable" : "EncoderMissing");
}

/// <summary>Bundled component row.</summary>
public sealed record ComponentRow(string Name, string Version, string License, string Usage);

/// <summary>Kind of the status pill under the FFmpeg card and in the setup dialog.</summary>
public enum StatusKind { Info, Ok, Warn, Error }

/// <summary>FFmpeg (download, update, custom path), tested hardware encoders and bundled components.</summary>
public sealed partial class DependenciesViewModel : PageViewModel
{
    public DependenciesViewModel(MainViewModel main) : base(main)
    {
        main.FfmpegChanged += Refresh;
        main.EncodersChanged += Refresh;
    }

    public override string TitleKey => "NavDependencies";

    public override string IconKey => "IconPackage";

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private double _downloadProgress;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private StatusKind _statusKind;
    [ObservableProperty] private string _latestVersion = "";
    [ObservableProperty] private long _downloadSize;

    /// <summary>Status shown in the pill: the last action, otherwise the installation state.</summary>
    public string StatusDisplay => StatusText.Length > 0
        ? StatusText
        : FfmpegReady ? Loc.F("StatusInstalledShort", FfmpegVersion) : Loc.T("StatusNotInstalled");

    private StatusKind EffectiveKind => StatusText.Length > 0 ? StatusKind : FfmpegReady ? StatusKind.Ok : StatusKind.Warn;

    public bool StatusIsOk => EffectiveKind == StatusKind.Ok;
    public bool StatusIsWarn => EffectiveKind == StatusKind.Warn;
    public bool StatusIsError => EffectiveKind == StatusKind.Error;
    public bool StatusIsInfo => EffectiveKind == StatusKind.Info;

    public Geometry? StatusIcon => Res.Icon(EffectiveKind switch
    {
        StatusKind.Ok => "IconCheckCircle",
        StatusKind.Warn => "IconAlert",
        StatusKind.Error => "IconAlertCircle",
        _ => "IconInfo"
    });

    public string DownloadSizeText => DownloadSize > 0 ? Units.Bytes(DownloadSize) + (LatestVersion.Length > 0 ? $" · FFmpeg {LatestVersion}" : "") : "";

    public bool HasDownloadSize => DownloadSize > 0;

    public string InstallFolder => AppEnvironment.FfmpegDirectory;

    partial void OnStatusTextChanged(string value) => RaiseStatus();

    partial void OnStatusKindChanged(StatusKind value) => RaiseStatus();

    partial void OnDownloadSizeChanged(long value)
    {
        OnPropertyChanged(nameof(DownloadSizeText));
        OnPropertyChanged(nameof(HasDownloadSize));
    }

    private void RaiseStatus()
    {
        foreach (var name in new[] { nameof(StatusDisplay), nameof(StatusIsOk), nameof(StatusIsWarn), nameof(StatusIsError), nameof(StatusIsInfo), nameof(StatusIcon) })
        {
            OnPropertyChanged(name);
        }
    }

    private void SetStatus(string text, StatusKind kind)
    {
        StatusKind = kind;
        StatusText = text;
    }

    /// <summary>Setup dialog: reads size and version of the current build (no download yet).</summary>
    public async Task PrepareSetupAsync()
    {
        if (DownloadSize > 0 || IsBusy)
        {
            return;
        }

        try
        {
            using var client = FfmpegDownloader.CreateClient(AppEnvironment.Version);
            var release = await FfmpegDownloader.FindLatestAsync(client);
            LatestVersion = release.Version;
            DownloadSize = release.Size;
        }
        catch
        {
            // Offline: the dialog simply shows no size.
        }
    }

    public bool FfmpegReady => Main.Locator.IsAvailable;

    public bool FfmpegMissing => !FfmpegReady;

    public string FfmpegVersion => Main.FfmpegVersion.Length > 0 ? Main.FfmpegVersion : "–";

    public string FfmpegSourceText => Loc.Enum(Main.Locator.Source);

    public string FfmpegPath => Main.Locator.FfmpegPath ?? Loc.T("NotFound");

    public bool IsDownloaded => Main.Locator.Source == FfmpegSource.Downloaded;

    public bool HasCustomPath => Main.Settings.FfmpegPath.Length > 0;

    public string InstalledBuild =>
        FfmpegDownloader.ReadInstallInfo(AppEnvironment.FfmpegDirectory) is { } info
            ? $"{info.AssetName} · {info.InstalledAt.ToLocalTime():d}"
            : "";

    public bool HasInstalledBuild => InstalledBuild.Length > 0;

    public IReadOnlyList<EncoderRow> Encoders => FormatCatalog.HardwareEncoders.Select(e => new EncoderRow(
        e,
        Loc.Enum(FormatCatalog.AccelerationOf(e)),
        e.StartsWith("h264", StringComparison.Ordinal) ? "H.264" : e.StartsWith("hevc", StringComparison.Ordinal) ? "H.265" : "AV1",
        Main.HasEncoder(e),
        Main.HardwareWorks(e))).ToList();

    public bool AnyHardware => Encoders.Any(e => e.Works);

    public IReadOnlyList<ComponentRow> Components =>
    [
        new("FFmpeg", FfmpegVersion, Loc.T("LicenseFfmpeg"), Loc.T("UsageFfmpeg")),
        new("ImageMagick (Magick.NET)", ImageEngine.Version is { Length: > 0 } v ? v.Split(' ').FirstOrDefault(p => p.Any(char.IsDigit)) ?? v : "–", "Apache-2.0 · ImageMagick", Loc.T("UsageMagick")),
        new("LibVLC (LibVLCSharp)", Main.Player.Version is { Length: > 0 } vlc ? vlc.Split(' ')[0] : "–", "LGPL-2.1", Loc.T("UsageVlc")),
        new("TagLib# (TagLibSharp)", "2.3", "LGPL-2.1", Loc.T("UsageTaglib")),
        new(".NET", RuntimeInformation.FrameworkDescription.Replace(".NET ", ""), "MIT", Loc.T("UsageDotnet")),
        new("Avalonia UI", "11.3", "MIT", Loc.T("UsageAvalonia"))
    ];

    [RelayCommand]
    private async Task DownloadAsync()
    {
        if (IsBusy)
        {
            return;
        }

        if (Main.Queue.IsBusy)
        {
            Main.ShowToast(Loc.T("ToastQueueBusy"), error: true);
            return;
        }

        IsBusy = true;
        DownloadProgress = 0;
        SetStatus(Loc.T("StatusSearchingRelease"), StatusKind.Info);
        using var activity = Main.BeginActivity(Loc.T("ActivityFfmpegDownload"));
        try
        {
            using var client = FfmpegDownloader.CreateClient(AppEnvironment.Version);
            var release = await FfmpegDownloader.FindLatestAsync(client);
            LatestVersion = release.Version;
            DownloadSize = release.Size;
            SetStatus(Loc.F("StatusDownloading", release.AssetName), StatusKind.Info);
            var progress = new Progress<double>(p =>
            {
                DownloadProgress = p * 100;
                activity.Progress = p;
            });
            await FfmpegDownloader.InstallAsync(client, release, AppEnvironment.FfmpegDirectory, progress);
            SetStatus(Loc.F("StatusVerifying", release.Version), StatusKind.Info);
            activity.Progress = null;
            activity.Text = Loc.F("StatusVerifying", release.Version);
            await Main.RefreshFfmpegAsync(retestHardware: true);
            SetStatus(Loc.F("StatusInstalled", release.Version), StatusKind.Ok);
            Main.ShowToast(Loc.F("ToastFfmpegInstalled", release.Version));
        }
        catch (Exception ex)
        {
            SetStatus(Loc.F("StatusDownloadFailed", ex.Message), StatusKind.Error);
            Main.ShowToast(StatusText, error: true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CheckUpdateAsync()
    {
        IsBusy = true;
        using var activity = Main.BeginActivity(Loc.T("StatusSearchingRelease"));
        try
        {
            using var client = FfmpegDownloader.CreateClient(AppEnvironment.Version);
            var release = await FfmpegDownloader.FindLatestAsync(client);
            LatestVersion = release.Version;
            DownloadSize = release.Size;
            var installed = FfmpegDownloader.ReadInstallInfo(AppEnvironment.FfmpegDirectory);
            if (installed is not null && installed.AssetName == release.AssetName)
            {
                SetStatus(Loc.F("StatusUpToDate", release.Version), StatusKind.Ok);
            }
            else
            {
                SetStatus(Loc.F("StatusUpdateAvailable", release.Version), StatusKind.Warn);
            }
        }
        catch (Exception ex)
        {
            SetStatus(Loc.F("StatusDownloadFailed", ex.Message), StatusKind.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ChooseAsync()
    {
        var files = await Host.PickFilesAsync(Loc.T("PickFfmpeg"), ["exe"], multiple: false);
        if (files.Count == 0)
        {
            return;
        }

        await UseCustomAsync(files[0]);
    }

    /// <summary>A folder that already contains ffmpeg.exe and ffprobe.exe (installed independently of CONVERTit).</summary>
    [RelayCommand]
    private async Task ChooseFolderAsync()
    {
        var folder = await Host.PickFolderAsync(Loc.T("PickFfmpegFolder"));
        if (folder is null)
        {
            return;
        }

        // Typical layouts: the folder itself or its "bin" subfolder.
        var bin = Path.Combine(folder, "bin");
        var exe = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        await UseCustomAsync(!File.Exists(Path.Combine(folder, exe)) && File.Exists(Path.Combine(bin, exe)) ? bin : folder);
    }

    private async Task UseCustomAsync(string path)
    {
        var previous = Main.Settings.FfmpegPath;
        Main.Settings.FfmpegPath = path;
        await Main.RefreshFfmpegAsync(retestHardware: true);
        if (Main.Locator.Source != FfmpegSource.Custom)
        {
            // Not usable: keep the previous choice.
            Main.Settings.FfmpegPath = previous;
            await Main.RefreshFfmpegAsync();
            SetStatus(Loc.T("ToastFfprobeMissing"), StatusKind.Error);
            Main.ShowToast(Loc.T("ToastFfprobeMissing"), error: true);
            return;
        }

        Main.Settings.Save();
        SetStatus(Loc.F("StatusCustomFound", Main.FfmpegVersion), StatusKind.Ok);
    }

    [RelayCommand]
    private async Task ResetCustomAsync()
    {
        Main.Settings.FfmpegPath = "";
        Main.Settings.Save();
        StatusText = "";
        await Main.RefreshFfmpegAsync(retestHardware: true);
    }

    [RelayCommand]
    private void OpenFolder()
    {
        var path = Main.Locator.FfmpegPath;
        AppEnvironment.OpenFolder(path is null ? AppEnvironment.FfmpegDirectory : Path.GetDirectoryName(path));
    }

    [RelayCommand]
    private async Task RemoveAsync()
    {
        if (Main.Queue.IsBusy)
        {
            Main.ShowToast(Loc.T("ToastQueueBusy"), error: true);
            return;
        }

        if (!await Host.ConfirmAsync(Loc.T("ConfirmRemoveFfmpegTitle"), Loc.T("ConfirmRemoveFfmpegText"), Loc.T("BtnRemove"), danger: true))
        {
            return;
        }

        try
        {
            FfmpegDownloader.Remove(AppEnvironment.FfmpegDirectory);
        }
        catch (Exception ex)
        {
            Main.ShowToast(ex.Message, error: true);
        }

        StatusText = "";
        await Main.RefreshFfmpegAsync(retestHardware: true);
    }

    [RelayCommand]
    private async Task RetestAsync()
    {
        IsBusy = true;
        SetStatus(Loc.T("StatusTestingEncoders"), StatusKind.Info);
        try
        {
            await Main.RefreshFfmpegAsync(retestHardware: true);
            SetStatus(Loc.T("StatusEncodersTested"), StatusKind.Ok);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void OpenReleases() => AppEnvironment.OpenUrl(FfmpegDownloader.ReleasePage);

    [RelayCommand]
    private void OpenFfmpegLicense() => AppEnvironment.OpenUrl("https://ffmpeg.org/legal.html");

    public void Refresh()
    {
        OnPropertyChanged(string.Empty);
        RaiseStatus();
    }

    public override void OnActivated() => Refresh();
}
