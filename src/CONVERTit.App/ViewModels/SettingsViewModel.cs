using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CONVERTit.App.Localization;
using CONVERTit.App.Services;

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
