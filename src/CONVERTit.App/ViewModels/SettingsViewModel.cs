using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CONVERTit.App.Localization;
using CONVERTit.App.Services;
using CONVERTit.Core.Machine;
using CONVERTit.Engines.Ffmpeg;

namespace CONVERTit.App.ViewModels;

/// <summary>Settings: language, theme, performance, Windows integration, data.</summary>
public sealed partial class SettingsViewModel : PageViewModel
{
    private bool _loading;

    public SettingsViewModel(MainViewModel main) : base(main)
    {
        Load();
    }

    public override string TitleKey => "NavSettings";

    public override string IconKey => "IconCog";

    private AppSettings S => Main.Settings;

    public IReadOnlyList<LanguageOption> Languages => LanguageOption.All;

    public ThemeOption[] Themes => Choices.Themes;

    public DisplayProfile[] DisplayProfiles => Choices.DisplayProfiles;

    /// <summary>FFmpeg module card (same state as the dependencies page).</summary>
    public DependenciesViewModel Modules => Main.Dependencies;

    public bool IsWindows => OperatingSystem.IsWindows();

    public PriorityOption[] Priorities => Choices.Priorities;

    public PerformanceProfile[] PerformanceProfiles => Choices.PerformanceProfiles;

    public IReadOnlyList<int> MediaParallelChoices { get; } = [1, 2, 3, 4];

    public IReadOnlyList<int> ImageParallelChoices { get; } = Enumerable.Range(1, Math.Clamp(Environment.ProcessorCount, 2, 16)).ToList();

    public string DataFolder => AppEnvironment.DataDirectory;

    public bool IsPortable => AppEnvironment.IsPortable;

    [ObservableProperty] private LanguageOption? _selectedLanguage;
    [ObservableProperty] private ThemeOption _theme;
    [ObservableProperty] private DisplayProfile _displayProfile;
    [ObservableProperty] private bool _explorerMenu;
    [ObservableProperty] private bool _sidebarAccordion;
    [ObservableProperty] private bool _startMaximized;
    [ObservableProperty] private int _mediaParallel;
    [ObservableProperty] private int _imageParallel;
    [ObservableProperty] private PriorityOption _priority;
    [ObservableProperty] private bool _preventSleep;
    [ObservableProperty] private bool _taskbarProgress;
    [ObservableProperty] private bool _systemNotifications;
    [ObservableProperty] private bool _inAppNotifications;
    [ObservableProperty] private bool _openFolderWhenDone;
    [ObservableProperty] private bool _rememberSettings;
    [ObservableProperty] private bool _useHardwareEncoders;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustomProfile))]
    private PerformanceProfile _performanceProfile;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustomAssociations))]
    private AssociationMode _associations;

    [ObservableProperty] private string _customAssociations = "";

    public AssociationMode[] AssociationModes { get; } = Enum.GetValues<AssociationMode>();

    public bool IsCustomAssociations => Associations == AssociationMode.Custom;

    /// <summary>"Registered for 31 file types. Windows asks you to confirm CONVERTit as default app."</summary>
    public string AssociationsHint => OperatingSystem.IsWindows() && Platform.Windows.FileAssociations.RegisteredCount() is var n and > 0
        ? Loc.F("HintAssociationsActive", n)
        : Loc.T("HintAssociations");

    [ObservableProperty] private bool _showHints;
    [ObservableProperty] private bool _checkForUpdates;
    [ObservableProperty] private bool _listThumbnails;
    [ObservableProperty] private TransparencyPolicy _transparentImages;

    public TransparencyPolicy[] TransparencyPolicies { get; } = Enum.GetValues<TransparencyPolicy>();

    [ObservableProperty] private bool _hardwareDecode;
    [ObservableProperty] private bool _gpuTurbo;
    [ObservableProperty] private bool _hdrToSdr;
    [ObservableProperty] private bool _autoDeinterlace;
    [ObservableProperty] private bool _minimizeToTray;
    [ObservableProperty] private bool _closeToTray;
    [ObservableProperty] private bool _startWithWindows;
    [ObservableProperty] private bool _startInTray;
    [ObservableProperty] private bool _backgroundExplorerJobs;
    [ObservableProperty] private bool _exitAfterBackgroundJobs;

    /// <summary>Manual parallelism and priority only for the Custom profile.</summary>
    public bool IsCustomProfile => PerformanceProfile == PerformanceProfile.Custom;

    // ---------------------------------------------------------------- "This system" card

    private MachineInfo Info => Main.Machine.Info;

    /// <summary>"Active: Eco – on battery".</summary>
    public string ProfileStatus => Main.Plan is { } plan
        ? Loc.F("ProfileActive", Loc.Enum(plan.Profile), Loc.T(plan.ReasonKey))
        : "";

    /// <summary>What the active profile does (threads, parallel jobs, priority).</summary>
    public string ProfileDetails => Main.Plan is { } plan
        ? Loc.F("ProfileDetails",
            plan.Threads > 0 ? plan.Threads.ToString() : Loc.T("ThreadsAll"),
            plan.MediaParallel, plan.ImageParallel, Loc.Enum(plan.Priority))
          + (plan.EcoQos ? " · EcoQoS" : "")
        : "";

    public string SystemCpu => Info.CpuName.Length > 0 ? Info.CpuName : Loc.T("Unknown");

    public string SystemCores => Info.PhysicalCores > 0
        ? Loc.F(Info.EfficiencyCores > 0 ? "CoresHybrid" : "Cores", Info.PhysicalCores, Info.LogicalCores,
            Info.PhysicalCores - Info.EfficiencyCores, Info.EfficiencyCores)
        : Loc.F("CoresLogical", Info.LogicalCores);

    public string SystemMemory => Info.MemoryBytes > 0 ? Core.Storage.Units.Bytes(Info.MemoryBytes) : Loc.T("Unknown");

    public string SystemGpus => Info.Gpus.Count == 0
        ? Loc.T(Main.Machine.IsDetected ? "NoGpu" : "Detecting")
        : string.Join("\n", Info.Gpus.Select(g => g.Name + " · " + Loc.T(g.Integrated ? "GpuIntegrated" : "GpuDedicated")
            + (g.DedicatedMemory > 0 && !g.Integrated ? " · " + Core.Storage.Units.Bytes(g.DedicatedMemory) : "")));

    public string SystemEncoders
    {
        get
        {
            var working = Main.Settings.WorkingHardwareEncoders;
            return working.Count == 0 ? Loc.T("NoHardwareEncoders") : string.Join(", ", working);
        }
    }

    public string SystemPower => !Info.HasBattery
        ? Loc.T("PowerMains")
        : Loc.F(Info.OnBattery ? "PowerBattery" : "PowerNotebookMains", Info.BatteryPercent >= 0 ? Info.BatteryPercent + " %" : "–")
          + (Info.EnergySaver ? " · " + Loc.T("PowerSaver") : "");

    /// <summary>Called by the shell when the profile or the detection changed.</summary>
    public void RefreshSystem()
    {
        foreach (var name in new[] { nameof(ProfileStatus), nameof(ProfileDetails), nameof(SystemCpu), nameof(SystemCores),
                     nameof(SystemMemory), nameof(SystemGpus), nameof(SystemEncoders), nameof(SystemPower) })
        {
            OnPropertyChanged(name);
        }
    }

    private void Load()
    {
        _loading = true;
        SelectedLanguage = LanguageOption.Current;
        Theme = S.Theme;
        DisplayProfile = S.DisplayProfile;
        ExplorerMenu = S.ExplorerMenu;
        SidebarAccordion = S.SidebarAccordion;
        StartMaximized = S.StartMaximized;
        MediaParallel = Math.Clamp(S.MediaParallel, 1, 4);
        ImageParallel = Math.Clamp(S.ImageParallel, 1, ImageParallelChoices.Count);
        Priority = S.Priority;
        PreventSleep = S.PreventSleep;
        TaskbarProgress = S.TaskbarProgress;
        SystemNotifications = S.SystemNotifications;
        InAppNotifications = S.InAppNotifications;
        OpenFolderWhenDone = S.OpenFolderWhenDone;
        RememberSettings = S.RememberSettings;
        UseHardwareEncoders = S.UseHardwareEncoders;
        PerformanceProfile = S.PerformanceProfile;
        ShowHints = S.ShowHints;
        CheckForUpdates = S.CheckForUpdates;
        Associations = S.Associations;
        CustomAssociations = S.CustomAssociations;
        ListThumbnails = S.ListThumbnails;
        TransparentImages = S.TransparentImages;
        HardwareDecode = S.HardwareDecode;
        GpuTurbo = S.GpuTurbo;
        HdrToSdr = S.HdrToSdr;
        AutoDeinterlace = S.AutoDeinterlace;
        MinimizeToTray = S.MinimizeToTray;
        CloseToTray = S.CloseToTray;
        StartWithWindows = OperatingSystem.IsWindows() && Platform.Windows.Autostart.IsEnabled(AppEnvironment.AutostartName);
        StartInTray = S.StartInTray;
        BackgroundExplorerJobs = S.BackgroundExplorerJobs;
        ExitAfterBackgroundJobs = S.ExitAfterBackgroundJobs;
        _loading = false;
    }

    // Every change is applied and saved immediately (no Save button needed).
    partial void OnSelectedLanguageChanged(LanguageOption? value)
    {
        if (_loading || value is null)
        {
            return;
        }

        S.Language = value.Code;
        Main.ApplyLanguage(value.Code);
        Persist();
    }

    partial void OnThemeChanged(ThemeOption value)
    {
        if (_loading)
        {
            return;
        }

        Main.ApplyTheme(value);
        Persist();
    }

    partial void OnDisplayProfileChanged(DisplayProfile value)
    {
        if (_loading)
        {
            return;
        }

        S.DisplayProfile = value;
        Main.ApplyDisplayProfile();
        Persist();
    }

    partial void OnExplorerMenuChanged(bool value)
    {
        if (_loading)
        {
            return;
        }

        S.ExplorerMenu = value;
        Persist();
        try
        {
            if (value)
            {
                ExplorerIntegration.Register();
                Main.ShowToast(Loc.T("ToastExplorerMenuOn"));
            }
            else
            {
                ExplorerIntegration.Unregister();
                Main.ShowToast(Loc.T("ToastExplorerMenuOff"));
            }
        }
        catch (Exception ex)
        {
            Main.ShowToast(ex.Message, error: true);
        }
    }

    partial void OnSidebarAccordionChanged(bool value) => Apply(() => S.SidebarAccordion = value);
    partial void OnStartMaximizedChanged(bool value) => Apply(() => S.StartMaximized = value);

    partial void OnMediaParallelChanged(int value) => Apply(() => S.MediaParallel = value);
    partial void OnImageParallelChanged(int value) => Apply(() => S.ImageParallel = value);
    partial void OnPriorityChanged(PriorityOption value) => Apply(() => S.Priority = value);
    partial void OnPreventSleepChanged(bool value) => Apply(() => S.PreventSleep = value);
    partial void OnTaskbarProgressChanged(bool value) => Apply(() => S.TaskbarProgress = value);
    partial void OnSystemNotificationsChanged(bool value) => Apply(() => S.SystemNotifications = value);
    partial void OnInAppNotificationsChanged(bool value) => Apply(() => S.InAppNotifications = value);
    partial void OnOpenFolderWhenDoneChanged(bool value) => Apply(() => S.OpenFolderWhenDone = value);
    partial void OnRememberSettingsChanged(bool value) => Apply(() => S.RememberSettings = value);
    partial void OnUseHardwareEncodersChanged(bool value) => Apply(() => S.UseHardwareEncoders = value);
    partial void OnPerformanceProfileChanged(PerformanceProfile value) => Apply(() => S.PerformanceProfile = value);
    partial void OnHardwareDecodeChanged(bool value) => Apply(() => S.HardwareDecode = value);

    // ------------------------------------------------------------ Updates (opt-in)

    public string CurrentVersionText => Loc.F("UpdateCurrentVersion", AppEnvironment.Version);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdate))]
    private AppRelease? _latestRelease;

    [ObservableProperty] private string _updateStatus = "";
    [ObservableProperty] private bool _isUpdating;
    [ObservableProperty] private double _updateProgress;

    public bool HasUpdate => LatestRelease is { } r && AppUpdater.IsNewer(r.Version, AppEnvironment.Version);

    public bool HasUpdateStatus => UpdateStatus.Length > 0;

    partial void OnUpdateStatusChanged(string value) => OnPropertyChanged(nameof(HasUpdateStatus));

    /// <summary>Switching it on asks first: the check contacts api.github.com.</summary>
    partial void OnCheckForUpdatesChanged(bool value)
    {
        if (_loading)
        {
            return;
        }

        if (!value)
        {
            Apply(() => S.CheckForUpdates = false);
            return;
        }

        _ = ConfirmUpdateSwitchAsync();
    }

    private async Task ConfirmUpdateSwitchAsync()
    {
        var ok = await Host.ConfirmAsync(Loc.T("ConfirmUpdatesTitle"), Loc.T("ConfirmUpdatesText"), Loc.T("BtnTurnOn"), confirmIcon: "IconUpdate");
        if (!ok)
        {
            _loading = true;
            CheckForUpdates = false;
            _loading = false;
            return;
        }

        Apply(() => S.CheckForUpdates = true);
        await CheckUpdatesNowCoreAsync();
    }

    /// <summary>"Check now": asks first while automatic checking is off.</summary>
    [RelayCommand]
    private async Task CheckUpdatesNowAsync()
    {
        if (!S.CheckForUpdates && !await Host.ConfirmAsync(Loc.T("ConfirmUpdatesTitle"), Loc.T("ConfirmUpdatesNowText"), Loc.T("BtnCheckNow"), confirmIcon: "IconUpdate"))
        {
            return;
        }

        await CheckUpdatesNowCoreAsync();
    }

    public async Task CheckUpdatesNowCoreAsync(bool quiet = false)
    {
        if (IsUpdating)
        {
            return;
        }

        IsUpdating = true;
        UpdateStatus = Loc.T("UpdateChecking");
        try
        {
            using var client = FfmpegDownloader.CreateClient(AppEnvironment.Version);
            LatestRelease = await AppUpdater.FindLatestAsync(client);
            S.LastUpdateCheck = DateTime.UtcNow;
            Persist();
            UpdateStatus = LatestRelease is null
                ? Loc.T("UpdateNoRelease")
                : HasUpdate ? Loc.F("UpdateAvailable", LatestRelease.Tag) : Loc.F("UpdateCurrent", AppEnvironment.Version);
            if (HasUpdate)
            {
                Main.ShowToast(Loc.F("ToastUpdateAvailable", LatestRelease!.Tag));
            }
            else if (!quiet)
            {
                Main.ShowToast(UpdateStatus);
            }
        }
        catch (Exception ex)
        {
            UpdateStatus = Loc.F("UpdateFailed", ex.Message);
            if (!quiet)
            {
                Main.ShowToast(UpdateStatus, error: true);
            }
        }
        finally
        {
            IsUpdating = false;
        }
    }

    /// <summary>
    /// Installed: downloads the setup (SHA-256 checked), asks, starts it and closes CONVERTit. Portable: downloads
    /// the ZIP into the data folder and opens it – the files are replaced by hand.
    /// </summary>
    [RelayCommand]
    private async Task DownloadUpdateAsync()
    {
        if (LatestRelease is not { } release || IsUpdating)
        {
            return;
        }

        var portable = AppEnvironment.IsPortable;
        var url = portable ? release.PortableUrl : release.SetupUrl;
        var name = portable ? release.PortableName : release.SetupName;
        if (url is null || name is null)
        {
            AppEnvironment.OpenUrl(release.PageUrl);
            return;
        }

        if (Main.Queue.IsBusy)
        {
            Main.ShowToast(Loc.T("ToastQueueBusy"), error: true);
            return;
        }

        IsUpdating = true;
        UpdateProgress = 0;
        UpdateStatus = Loc.F("StatusDownloading", name);
        try
        {
            using var client = FfmpegDownloader.CreateClient(AppEnvironment.Version);
            var folder = portable ? Path.Combine(AppEnvironment.DataDirectory, "updates") : Path.Combine(Path.GetTempPath(), "CONVERTit-update");
            var progress = new Progress<double>(p => UpdateProgress = p * 100);
            var file = await AppUpdater.DownloadAsync(client, url, name, portable ? release.PortableDigest : release.SetupDigest,
                portable ? release.Sha256PortableUrl : release.Sha256SetupUrl, folder, progress);
            if (portable)
            {
                UpdateStatus = Loc.F("UpdatePortableReady", name);
                AppEnvironment.OpenFolder(folder);
                return;
            }

            UpdateStatus = Loc.F("UpdateReady", release.Tag);
            if (await Host.ConfirmAsync(Loc.F("ConfirmInstallUpdateTitle", release.Tag), Loc.T("ConfirmInstallUpdateText"), Loc.T("BtnInstallNow"),
                    confirmIcon: "IconDownload"))
            {
                AppUpdater.RunSetup(file);
                Main.RequestExit();
            }
        }
        catch (Exception ex)
        {
            UpdateStatus = Loc.F("UpdateFailed", ex.Message);
            Main.ShowToast(UpdateStatus, error: true);
        }
        finally
        {
            IsUpdating = false;
        }
    }

    [RelayCommand]
    private void OpenReleasePage() => AppEnvironment.OpenUrl(LatestRelease?.PageUrl ?? AppEnvironment.ReleasesUrl);

    partial void OnShowHintsChanged(bool value)
    {
        Apply(() => S.ShowHints = value);
        Main.HideHints = !value;
    }

    partial void OnAssociationsChanged(AssociationMode value)
    {
        if (_loading)
        {
            return;
        }

        S.Associations = value;
        Persist();
        // Custom applies with its button (after the list was typed).
        if (value != AssociationMode.Custom)
        {
            ApplyAssociations();
        }
    }

    [RelayCommand]
    private void ApplyAssociations()
    {
        S.CustomAssociations = CustomAssociations;
        Persist();
        try
        {
            var count = FileAssociationService.Apply(S);
            Main.ShowToast(count > 0 ? Loc.F("ToastAssociationsOn", count) : Loc.T("ToastAssociationsOff"));
        }
        catch (Exception ex)
        {
            Main.ShowToast(ex.Message, error: true);
        }

        OnPropertyChanged(nameof(AssociationsHint));
    }

    [RelayCommand]
    private async Task ClearThumbnailCacheAsync()
    {
        await Task.Run(ThumbnailService.ClearCache);
        Main.ShowToast(Loc.T("ToastThumbnailsCleared"));
    }

    [RelayCommand]
    private void OpenDefaultApps()
    {
        if (OperatingSystem.IsWindows())
        {
            Platform.Windows.FileAssociations.OpenDefaultAppsSettings();
        }
    }

    partial void OnListThumbnailsChanged(bool value) => Apply(() => S.ListThumbnails = value);
    partial void OnTransparentImagesChanged(TransparencyPolicy value) => Apply(() => S.TransparentImages = value);
    partial void OnGpuTurboChanged(bool value) => Apply(() => S.GpuTurbo = value);
    partial void OnHdrToSdrChanged(bool value) => Apply(() => S.HdrToSdr = value);
    partial void OnAutoDeinterlaceChanged(bool value) => Apply(() => S.AutoDeinterlace = value);
    partial void OnMinimizeToTrayChanged(bool value) => Apply(() => S.MinimizeToTray = value);
    partial void OnCloseToTrayChanged(bool value) => Apply(() => S.CloseToTray = value);
    partial void OnStartInTrayChanged(bool value) => Apply(() => S.StartInTray = value);
    partial void OnBackgroundExplorerJobsChanged(bool value) => Apply(() => S.BackgroundExplorerJobs = value);
    partial void OnExitAfterBackgroundJobsChanged(bool value) => Apply(() => S.ExitAfterBackgroundJobs = value);

    /// <summary>Start with Windows: HKCU Run entry "CONVERTit.exe --tray" (same entry as the installer option).</summary>
    partial void OnStartWithWindowsChanged(bool value)
    {
        if (_loading || !OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            Platform.Windows.Autostart.Set(AppEnvironment.AutostartName, value, AppEnvironment.ExecutablePath, "--tray");
            Main.ShowToast(Loc.T(value ? "ToastAutostartOn" : "ToastAutostartOff"));
        }
        catch (Exception ex)
        {
            Main.ShowToast(ex.Message, error: true);
        }

        Main.ApplyRuntimeSettings();
    }

    private void Apply(Action change)
    {
        if (_loading)
        {
            return;
        }

        change();
        Main.ApplyRuntimeSettings();
        Persist();
    }

    private void Persist() => S.Save();

    [RelayCommand]
    private void OpenDataFolder() => AppEnvironment.OpenFolder(AppEnvironment.DataDirectory);

    [RelayCommand]
    private async Task ResetPageSettingsAsync()
    {
        if (await Host.ConfirmAsync(Loc.T("ConfirmResetTitle"), Loc.T("ConfirmResetText"), Loc.T("BtnReset"), danger: true, confirmIcon: "IconUndo"))
        {
            S.Pages.Clear();
            Persist();
            Main.ShowToast(Loc.T("ToastResetDone"));
        }
    }

    [RelayCommand]
    private void RemoveWindowsIntegration()
    {
        if (OperatingSystem.IsWindows())
        {
            Platform.Windows.Notifications.Unregister();
        }

        SystemNotifications = false;
        ExplorerMenu = false;
        Associations = AssociationMode.Off;
        Main.ShowToast(Loc.T("ToastIntegrationRemoved"));
    }

    [RelayCommand]
    private void TestNotification()
    {
        var error = Main.ShowSystemNotification(Loc.T("NotifyTestTitle"), Loc.T("NotifyTestText"), AppEnvironment.DataDirectory);
        if (error is not null)
        {
            Main.ShowToast(Loc.F("ToastNotificationFailed", error), error: true);
        }
    }

    public override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
    }
}
