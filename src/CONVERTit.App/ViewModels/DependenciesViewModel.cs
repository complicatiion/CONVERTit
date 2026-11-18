using System.Runtime.InteropServices;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CONVERTit.App.Localization;
using CONVERTit.App.Services;
using CONVERTit.App.Views;
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
        Modules = ModuleCatalog.Optional.Select(m => new ModuleCardViewModel(main, m)).ToList();
        main.Modules.Changed += () => Avalonia.Threading.Dispatcher.UIThread.Post(RaiseOverview);
        main.FfmpegChanged += RaiseOverview;
    }

    /// <summary>Optional modules (upscalers, frame interpolation) as cards.</summary>
    public IReadOnlyList<ModuleCardViewModel> Modules { get; }

    // ------------------------------------------------------------ Overview of all dependencies

    /// <summary>One line per dependency: status dot, name, purpose, ready / not ready.</summary>
    public IReadOnlyList<DependencyRow> Overview
    {
        get
        {
            var rows = new List<DependencyRow>
            {
                new("FFmpeg", Loc.T("ModuleFfmpeg"), Main.Locator.FfmpegPath is not null, true,
                    Main.Locator.FfmpegPath is not null ? ShortVersion(FfmpegVersion) : ""),
                new("ffprobe", Loc.T("ModuleFfprobe"), Main.Locator.FfprobePath is not null, true, "")
            };
            rows.AddRange(Modules.Select(m => new DependencyRow(m.Name, m.Description, m.IsInstalled, false,
                m.IsInstalled ? m.Module.Tag ?? "" : "")));
            return rows;
        }
    }

    public int ReadyCount => Overview.Count(r => r.Ready);

    public string OverviewSummary => Loc.F("DependenciesSummary", ReadyCount, Overview.Count);

    public bool AnyMissing => Overview.Any(r => !r.Ready);

    private void RaiseOverview()
    {
        OnPropertyChanged(nameof(Overview));
        OnPropertyChanged(nameof(ReadyCount));
        OnPropertyChanged(nameof(OverviewSummary));
        OnPropertyChanged(nameof(AnyMissing));
    }

    /// <summary>Checks FFmpeg (version, encoders stay cached) and every module folder again.</summary>
    [RelayCommand]
    private async Task CheckAllAsync()
    {
        StatusText = "";
        await Main.RefreshFfmpegAsync();
        Main.Modules.Refresh();
        RaiseOverview();
        Main.ShowToast(Loc.F("ToastDependenciesChecked", ReadyCount, Overview.Count), error: AnyMissing && !Main.Locator.IsAvailable);
    }

    /// <summary>Installs everything that is missing after one opt-in dialog for all of it.</summary>
    [RelayCommand]
    private async Task InstallMissingAsync()
    {
        var modules = Modules.Where(m => !m.IsInstalled).ToList();
        var ffmpeg = !Main.Locator.IsAvailable;
        if (modules.Count == 0 && !ffmpeg)
        {
            Main.ShowToast(Loc.T("ToastAllReady"));
            return;
        }

        var items = new List<OptInItem>();
        if (ffmpeg)
        {
            items.Add(FfmpegOptIn());
        }

        items.AddRange(modules.Select(m => OptIn(m.Module)));
        if (!await Host.OptInAsync(Loc.T("OptInModulesTitle"), Loc.T("OptInModulesText"), items, Loc.T("BtnDownloadInstall")))
        {
            return;
        }

        if (ffmpeg)
        {
            await DownloadCommand.ExecuteAsync(null);
        }

        foreach (var card in modules)
        {
            await card.InstallAsync();
        }

        RaiseOverview();
    }

    /// <summary>
    /// Updates FFmpeg to the newest build and reinstalls modules whose installed release differs from the pinned one
    /// (modules installed from a ZIP or an own folder are left alone).
    /// </summary>
    [RelayCommand]
    private async Task UpdateAllAsync()
    {
        if (Main.Locator.IsAvailable && IsDownloaded)
        {
            await CheckUpdateCommand.ExecuteAsync(null);
        }

        var outdated = Modules.Where(m => m.IsOutdated).ToList();
        if (outdated.Count > 0 && await Host.OptInAsync(Loc.T("OptInModulesTitle"), Loc.T("OptInModulesText"),
                outdated.Select(m => OptIn(m.Module)).ToList(), Loc.T("BtnUpdate")))
        {
            foreach (var card in outdated)
            {
                await card.InstallAsync();
            }
        }
        else if (outdated.Count == 0)
        {
            Main.ShowToast(Loc.T("ToastModulesCurrent"));
        }

        RaiseOverview();
    }

    /// <summary>Opt-in entry of a module (license text from licenses\third-party).</summary>
    public static OptInItem OptIn(ModuleDefinition module) =>
        new(module.Name + (module.ApproximateSize > 0 ? $" · ≈ {Units.Bytes(module.ApproximateSize)}" : ""), Loc.T("Module" + module.Id),
            module.License, module.Homepage, "third-party/OPTIONAL-MODULES.md");

    public static OptInItem FfmpegOptIn() =>
        new("FFmpeg · ≈ 140 MB", Loc.T("ModuleFfmpeg"), "GPL-3.0", "https://github.com/BtbN/FFmpeg-Builds", "third-party/FFMPEG.md");

    /// <summary>Opt-in dialog for modules; used by the cards, the overview and the tool pages.</summary>
    public static Task<bool> ConfirmModulesAsync(MainViewModel main, IReadOnlyList<ModuleDefinition> modules) =>
        main.Host.OptInAsync(Loc.T("OptInModulesTitle"), Loc.T("OptInModulesText"), modules.Select(OptIn).ToList(), Loc.T("BtnDownloadInstall"));

    /// <summary>Tool pages: opt-in, then download and install the module (progress shows on its card and in the header).</summary>
    public async Task<bool> InstallModuleAsync(ModuleId id)
    {
        var card = Modules.First(m => m.Module.Id == id);
        if (card.IsInstalled || !await ConfirmModulesAsync(Main, [card.Module]))
        {
            return card.IsInstalled;
        }

        var ok = await card.InstallAsync();
        RaiseOverview();
        return ok;
    }

    /// <summary>Folder for modules of all users (installer); null in the portable version.</summary>
    public string SharedModulesText => AppEnvironment.SharedModulesDirectory is { } shared
        ? Loc.F("ModulesSharedFolder", shared)
        : Loc.T("ModulesPortable");

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
            : ModuleInstaller.ReadInstallInfo(AppEnvironment.FfmpegDirectory) is { } zip
                ? $"{zip.AssetName} · {zip.InstalledAt.ToLocalTime():d}"
                : "";

    public bool HasInstalledBuild => InstalledBuild.Length > 0;

    public IReadOnlyList<EncoderRow> Encoders => FormatCatalog.HardwareEncoders.Select(e => new EncoderRow(
        e,
        Loc.Enum(FormatCatalog.AccelerationOf(e)),
        e.StartsWith("h264", StringComparison.Ordinal) ? "H.264" : e.StartsWith("hevc", StringComparison.Ordinal) ? "H.265" : "AV1",
        Main.HasEncoder(e),
        Main.HardwareWorks(e))).ToList();

    public bool AnyHardware => Encoders.Any(e => e.Works);

    /// <summary>
    /// Components and optional modules, sorted like the About page: by the width of name and license together,
    /// shortest first (same font, so the character count matches the visual width).
    /// </summary>
    public IReadOnlyList<ComponentRow> Components => ComponentRowsUnsorted
        .OrderBy(c => c.Name.Length + c.License.Length).ThenBy(c => c.License.Length).ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
        .ToList();

    /// <summary>"7.1.1-full_build-www.gyan.dev" → "7.1.1", "N-121000-g1a2b3c-20251101" → "N-121000" (full build: FFmpeg card).</summary>
    public static string ShortVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return "–";
        }

        var parts = version.Trim().Split('-', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 && parts[0] == "N" ? $"N-{parts[1]}" : parts[0];
    }

    private IReadOnlyList<ComponentRow> ComponentRowsUnsorted =>
    [
        new("FFmpeg", ShortVersion(FfmpegVersion), Loc.T("LicenseFfmpeg"), Loc.T("UsageFfmpeg")),
        new("ImageMagick (Magick.NET)", ImageEngine.Version is { Length: > 0 } v ? v.Split(' ').FirstOrDefault(p => p.Any(char.IsDigit)) ?? v : "–", "Apache-2.0 · ImageMagick", Loc.T("UsageMagick")),
        new("LibVLC (LibVLCSharp)", Main.Player.Version is { Length: > 0 } vlc ? vlc.Split(' ')[0] : "–", "LGPL-2.1", Loc.T("UsageVlc")),
        new("TagLib# (TagLibSharp)", "2.3", "LGPL-2.1", Loc.T("UsageTaglib")),
        new(".NET", RuntimeInformation.FrameworkDescription.Replace(".NET ", ""), "MIT", Loc.T("UsageDotnet")),
        new("Avalonia UI", "11.3", "MIT", Loc.T("UsageAvalonia")),
        new("SkiaSharp / HarfBuzzSharp", "–", "MIT · BSD-3-Clause", Loc.T("UsageSkia")),
        new("CommunityToolkit.Mvvm", "8.4", "MIT", Loc.T("UsageMvvm")),
        new("Material Design Icons", "7.4", "Apache-2.0", Loc.T("UsageIcons")),
        new("Inter", "4.x", "SIL OFL 1.1", Loc.T("UsageFont")),
        new("Real-ESRGAN", ModuleVersion(ModuleCatalog.RealEsrgan), "BSD-3-Clause", Loc.T("UsageRealEsrgan")),
        new("waifu2x", ModuleVersion(ModuleCatalog.Waifu2x), "MIT", Loc.T("UsageWaifu2x")),
        new("RIFE", ModuleVersion(ModuleCatalog.Rife), "MIT", Loc.T("UsageRife")),
        new("ncnn", "–", "BSD-3-Clause", Loc.T("UsageNcnn"))
    ];

    /// <summary>Pinned release tag when the module is installed, otherwise "–".</summary>
    private string ModuleVersion(ModuleDefinition module) => Main.Modules.IsAvailable(module.Id) ? module.Tag ?? "✓" : "–";

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
            // The current download folder or the one used up to 0.0.6 (tools\ffmpeg).
            FfmpegDownloader.Remove(Path.GetDirectoryName(Main.Locator.FfmpegPath) ?? AppEnvironment.FfmpegDirectory);
        }
        catch (Exception ex)
        {
            Main.ShowToast(ex.Message, error: true);
        }

        StatusText = "";
        await Main.RefreshFfmpegAsync(retestHardware: true);
    }

    /// <summary>FFmpeg from a ZIP the user downloaded (e.g. a BtbN or gyan.dev build).</summary>
    [RelayCommand]
    private async Task InstallZipAsync()
    {
        var files = await Host.PickFilesAsync(Loc.F("PickModuleZip", "FFmpeg"), ["zip"], multiple: false);
        if (files.Count == 0)
        {
            return;
        }

        if (Main.Queue.IsBusy)
        {
            Main.ShowToast(Loc.T("ToastQueueBusy"), error: true);
            return;
        }

        IsBusy = true;
        try
        {
            await Task.Run(() => ModuleInstaller.InstallFromZip(ModuleCatalog.Ffmpeg, files[0], AppEnvironment.FfmpegDirectory));
            await Main.RefreshFfmpegAsync(retestHardware: true);
            SetStatus(Loc.F("StatusInstalled", ShortVersion(FfmpegVersion)), StatusKind.Ok);
        }
        catch (Exception ex)
        {
            SetStatus(Loc.F("ModuleZipFailed", ex.Message), StatusKind.Error);
            Main.ShowToast(StatusText, error: true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RecheckAsync()
    {
        StatusText = "";
        await Main.RefreshFfmpegAsync();
        Main.Modules.Refresh();
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

/// <summary>Row of the dependency overview.</summary>
public sealed record DependencyRow(string Name, string Purpose, bool Ready, bool Required, string Version)
{
    public string StateText => Loc.T(Ready ? "StateReady" : Required ? "StateMissing" : "StateNotInstalled");

    public bool Missing => !Ready;
}

/// <summary>
/// Card of an optional module (Real-ESRGAN, waifu2x, RIFE): status, download of the pinned release, own folder,
/// install from a ZIP the user downloaded, re-check and remove.
/// </summary>
public sealed partial class ModuleCardViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    public ModuleCardViewModel(MainViewModel main, ModuleDefinition module)
    {
        _main = main;
        Module = module;
        main.Modules.Changed += () => Avalonia.Threading.Dispatcher.UIThread.Post(Refresh);
    }

    public ModuleDefinition Module { get; }

    public string Name => Module.Name;

    public string License => Module.License;

    public string Description => Loc.T("Module" + Module.Id);

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private StatusKind _statusKind;

    private (string? Folder, ModuleSource Source) Found => _main.Modules.Find(Module);

    public bool IsInstalled => Found.Folder is not null;

    public bool IsMissing => !IsInstalled;

    public string FolderText => Found.Folder ?? _main.Modules.InstallFolder(Module);

    public string SourceText => Loc.Enum(Found.Source);

    /// <summary>Downloaded or installed from ZIP by CONVERTit into the user's module folder (can be removed here).</summary>
    public bool CanRemove => Found.Source == ModuleSource.User;

    public bool HasCustomFolder => _main.Settings.ModuleFolders.ContainsKey(Module.Id.ToString());

    public string InstalledText =>
        IsInstalled && ModuleInstaller.ReadInstallInfo(Found.Folder!) is { } info
            ? Loc.F(info.Verified ? "ModuleInstalledVerified" : "ModuleInstalledZip", info.AssetName, info.InstalledAt.ToLocalTime().ToString("d"))
            : "";

    public bool HasInstalledText => InstalledText.Length > 0;

    /// <summary>Downloaded by CONVERTit from an older pinned release.</summary>
    public bool IsOutdated => Found.Source == ModuleSource.User && ModuleInstaller.ReadInstallInfo(Found.Folder!) is { Verified: true } info
        && info.Tag != Module.Tag;

    /// <summary>"Expected: realesrgan-ncnn-vulkan.exe, models\…" for manual placement.</summary>
    public string ExpectedFiles => Loc.F("ModuleExpectedFiles", string.Join(", ", Module.RequiredFiles.Select(f => f.Replace('/', '\\'))));

    public string SizeText => Module.ApproximateSize > 0 ? Loc.F("ModuleSize", Units.Bytes(Module.ApproximateSize)) : "";

    public string StatusDisplay => StatusText.Length > 0
        ? StatusText
        : IsInstalled ? Loc.F("ModuleFound", SourceText) : Loc.F("ModuleNotInstalled", Module.Name);

    private StatusKind EffectiveKind => StatusText.Length > 0 ? StatusKind : IsInstalled ? StatusKind.Ok : StatusKind.Warn;

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

    partial void OnStatusTextChanged(string value) => Refresh();

    partial void OnStatusKindChanged(StatusKind value) => Refresh();

    public void Refresh() => OnPropertyChanged(string.Empty);

    private void SetStatus(string text, StatusKind kind)
    {
        StatusKind = kind;
        StatusText = text;
    }

    /// <summary>Card button: asks with the opt-in dialog first.</summary>
    [RelayCommand]
    private async Task DownloadAsync()
    {
        if (await DependenciesViewModel.ConfirmModulesAsync(_main, [Module]))
        {
            await InstallAsync();
        }
    }

    /// <summary>Downloads and installs the pinned release (the caller has asked the user already).</summary>
    public async Task<bool> InstallAsync()
    {
        if (IsBusy)
        {
            return false;
        }

        if (_main.Queue.IsBusy)
        {
            _main.ShowToast(Loc.T("ToastQueueBusy"), error: true);
            return false;
        }

        IsBusy = true;
        Progress = 0;
        SetStatus(Loc.T("StatusSearchingRelease"), StatusKind.Info);
        using var activity = _main.BeginActivity(Loc.F("ActivityModuleDownload", Module.Name));
        try
        {
            using var client = FfmpegDownloader.CreateClient(AppEnvironment.Version);
            var release = await ModuleInstaller.FindReleaseAsync(client, Module);
            SetStatus(Loc.F("StatusDownloading", release.AssetName), StatusKind.Info);
            var progress = new Progress<double>(p =>
            {
                Progress = p * 100;
                activity.Progress = p;
            });
            var info = await ModuleInstaller.InstallAsync(client, Module, release, _main.Modules.InstallFolder(Module), progress);
            _main.Modules.Refresh();
            SetStatus(Loc.F(info.Verified ? "ModuleInstalledOk" : "ModuleInstalledUnverified", Module.Name), info.Verified ? StatusKind.Ok : StatusKind.Warn);
            _main.ShowToast(Loc.F("ToastModuleInstalled", Module.Name));
            return true;
        }
        catch (Exception ex)
        {
            SetStatus(Loc.F("StatusDownloadFailed", ex.Message), StatusKind.Error);
            _main.ShowToast(StatusText, error: true);
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task InstallZipAsync()
    {
        var files = await _main.Host.PickFilesAsync(Loc.F("PickModuleZip", Module.Name), ["zip"], multiple: false);
        if (files.Count == 0)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var target = _main.Modules.InstallFolder(Module);
            await Task.Run(() => ModuleInstaller.InstallFromZip(Module, files[0], target));
            _main.Modules.Refresh();
            SetStatus(Loc.F("ModuleInstalledUnverified", Module.Name), StatusKind.Ok);
            _main.ShowToast(Loc.F("ToastModuleInstalled", Module.Name));
        }
        catch (Exception ex)
        {
            SetStatus(Loc.F("ModuleZipFailed", ex.Message), StatusKind.Error);
            _main.ShowToast(StatusText, error: true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ChooseFolderAsync()
    {
        var folder = await _main.Host.PickFolderAsync(Loc.F("PickModuleFolder", Module.Name));
        if (folder is null)
        {
            return;
        }

        if (!ModuleLocator.Complete(Module, folder))
        {
            var missing = ModuleLocator.Missing(Module, folder);
            SetStatus(Loc.F("ModuleFolderIncomplete", string.Join(", ", missing.Select(m => m.Replace('/', '\\')))), StatusKind.Error);
            return;
        }

        _main.Settings.ModuleFolders[Module.Id.ToString()] = folder;
        _main.Settings.Save();
        _main.Modules.Refresh();
        SetStatus(Loc.F("ModuleFound", Loc.Enum(ModuleSource.Custom)), StatusKind.Ok);
    }

    [RelayCommand]
    private void ResetFolder()
    {
        _main.Settings.ModuleFolders.Remove(Module.Id.ToString());
        _main.Settings.Save();
        StatusText = "";
        _main.Modules.Refresh();
    }

    [RelayCommand]
    private void OpenFolder() => AppEnvironment.OpenFolder(FolderText);

    [RelayCommand]
    private void Recheck()
    {
        StatusText = "";
        _main.Modules.Refresh();
        _main.ShowToast(IsInstalled ? Loc.F("ModuleFound", SourceText) : Loc.F("ModuleNotFoundToast", Module.Name), error: !IsInstalled);
    }

    [RelayCommand]
    private async Task RemoveAsync()
    {
        if (_main.Queue.IsBusy)
        {
            _main.ShowToast(Loc.T("ToastQueueBusy"), error: true);
            return;
        }

        if (!await _main.Host.ConfirmAsync(Loc.F("ConfirmRemoveModuleTitle", Module.Name), Loc.T("ConfirmRemoveModuleText"), Loc.T("BtnRemove"), danger: true))
        {
            return;
        }

        try
        {
            ModuleInstaller.Remove(_main.Modules.InstallFolder(Module));
        }
        catch (Exception ex)
        {
            _main.ShowToast(ex.Message, error: true);
        }

        StatusText = "";
        _main.Modules.Refresh();
    }

    [RelayCommand]
    private void OpenHomepage() => AppEnvironment.OpenUrl(Module.Homepage);

    [RelayCommand]
    private void OpenLicense() => AppEnvironment.OpenUrl(Module.LicenseUrl);
}
