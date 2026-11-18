using System.Diagnostics;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CONVERTit.App.Localization;
using CONVERTit.App.Services;
using CONVERTit.Core.Catalog;
using CONVERTit.Core.Ffmpeg;
using CONVERTit.Core.Jobs;
using CONVERTit.Core.Machine;
using CONVERTit.Core.Media;
using CONVERTit.Core.Options;
using CONVERTit.Core.Storage;
using CONVERTit.Engines.Ffmpeg;

namespace CONVERTit.App.ViewModels;

/// <summary>Horizontal line between sidebar sections.</summary>
public sealed class NavDivider;

/// <summary>
/// A running background activity shown in the header (loading files, opening large media, downloading FFmpeg).
/// Progress null = indeterminate (running bar). Dispose ends it. UI thread only.
/// </summary>
public sealed class BusyActivity : IDisposable
{
    private readonly MainViewModel _owner;
    private string _text;
    private double? _progress;

    internal BusyActivity(MainViewModel owner, string text)
    {
        _owner = owner;
        _text = text;
    }

    public string Text
    {
        get => _text;
        set
        {
            _text = value;
            _owner.RefreshActivity();
        }
    }

    /// <summary>0–1, null = unknown.</summary>
    public double? Progress
    {
        get => _progress;
        set
        {
            _progress = value;
            _owner.RefreshActivity();
        }
    }

    public void Dispose() => _owner.EndActivity(this);
}

/// <summary>Shell: services, navigation (groups with sub pages), header, notifications, queue integration.</summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly DispatcherTimer _toastTimer;
    private readonly DispatcherTimer _queueTimer;
    private readonly DispatcherTimer _externalTimer;
    private readonly List<(string Action, List<string> Files)> _external = [];
    private bool _wasBusy;

    public MainViewModel(AppSettings settings, UiHost host)
    {
        Settings = settings;
        Host = host;

        // ------------------------------------------------------------ Services
        Locator = new FfmpegLocator(AppEnvironment.FfmpegDirectory, () => Settings.FfmpegPath, AppEnvironment.FfmpegSharedDirectories,
            AppEnvironment.LegacyFfmpegDirectory);
        Modules = new ModuleLocator(AppEnvironment.ModulesDirectory, AppEnvironment.SharedModulesDirectory,
            id => Settings.ModuleFolders.TryGetValue(id.ToString(), out var folder) ? folder : null);
        Runner = new FfmpegRunner(Locator);
        Probe = new MediaProbe(new FfprobeService(Runner));
        Thumbnails = new ThumbnailService(Runner, Locator);
        _ = Task.Run(ThumbnailService.TrimCache);
        // Opt-in update check: at most once a day, quietly (a toast only when a new version exists).
        if (Settings.CheckForUpdates && (Settings.LastUpdateCheck is not { } last || DateTime.UtcNow - last > TimeSpan.FromHours(20)))
        {
            Dispatcher.UIThread.Post(() => _ = SettingsPage.CheckUpdatesNowCoreAsync(quiet: true), DispatcherPriority.Background);
        }
        Conversion = new ConversionService(Runner, Probe) { Modules = Modules };
        Modules.Changed += () => Dispatcher.UIThread.Post(() =>
        {
            OnPropertyChanged(nameof(ModulesReady));
            OnPropertyChanged(nameof(FfmpegPillText));
            OnPropertyChanged(nameof(FfmpegPillTip));
        });
        Player = new PlayerService();
        // LibVLC is warmed up when the window is shown first (a start in the notification area stays light).
        AudioPreview = new AudioPreview(Player);
        Machine = new SystemMonitor();
        Machine.Changed += OnMachineChanged;
        Conversion.EncodeSettings = () => _encode;
        Queue = new JobQueue(action => Dispatcher.UIThread.Post(action));
        Queue.Changed += OnQueueChanged;
        Queue.Drained += OnQueueDrained;
        if (OperatingSystem.IsWindows())
        {
            var job = TryCreateJobObject();
            ModuleRunner.ProcessStarted = process =>
            {
                job?.Assign(process);
                if (_ecoQos)
                {
                    Platform.Windows.ProcessPower.SetEcoQos(process, true);
                }
            };
            Runner.ProcessStarted = process =>
            {
                job?.Assign(process);
                if (_ecoQos)
                {
                    Platform.Windows.ProcessPower.SetEcoQos(process, true);
                }
            };
        }

        // ------------------------------------------------------------ Pages
        QuickConvert = new QuickConvertViewModel(this);
        Compress = new CompressViewModel(this);
        VideoConvert = new VideoConvertViewModel(this);
        VideoTrim = new TrimViewModel(this, MediaKind.Video);
        Animation = new AnimationViewModel(this);
        Remux = new RemuxViewModel(this);
        ExtractAudio = new ExtractAudioViewModel(this);
        AudioConvert = new AudioConvertViewModel(this);
        AudioTrim = new TrimViewModel(this, MediaKind.Audio);
        GameAudio = new GameAudioViewModel(this);
        Tags = new TagEditorViewModel(this);
        ImageConvert = new ImageConvertViewModel(this, resizeOnly: false);
        ImageResize = new ImageConvertViewModel(this, resizeOnly: true);
        GameTextures = new GameTextureViewModel(this);
        IconPage = new IconViewModel(this);
        AppIcons = new AppIconsViewModel(this);
        Sprites = new SpritesViewModel(this);
        GodotVideo = new GodotVideoViewModel(this);
        Upscale = new UpscaleViewModel(this);
        Denoise = new DenoiseViewModel(this);
        Interpolate = new InterpolateViewModel(this);
        Thumbcache = new ThumbcacheViewModel(this);
        QueuePage = new QueueViewModel(this);
        Dependencies = new DependenciesViewModel(this);
        SettingsPage = new SettingsViewModel(this);
        About = new AboutViewModel(this);

        // ------------------------------------------------------------ Navigation
        _quickNav = new NavItem(QuickConvert);
        _compressNav = new NavItem(Compress);
        _queueNav = new NavItem(QueuePage) { IsAccent = true };
        VideoGroup = new NavGroup(this, "video", "NavVideo", "IconVideo",
            [new NavItem(VideoConvert), new NavItem(VideoTrim), new NavItem(Animation), new NavItem(Remux), new NavItem(ExtractAudio), new NavItem(GodotVideo)]);
        AudioGroup = new NavGroup(this, "audio", "NavAudio", "IconAudio",
            [new NavItem(AudioConvert), new NavItem(AudioTrim), new NavItem(Tags), new NavItem(GameAudio)]);
        ImageGroup = new NavGroup(this, "image", "NavImage", "IconImage",
            [new NavItem(ImageConvert), new NavItem(ImageResize), new NavItem(GameTextures), new NavItem(IconPage), new NavItem(AppIcons), new NavItem(Sprites)]);
        ToolsGroup = new NavGroup(this, "tools", "NavTools", "IconSparkle",
            [new NavItem(Upscale), new NavItem(Denoise), new NavItem(Interpolate), new NavItem(Thumbcache)]);
        // All groups start expanded with the full sidebar (collapsed in the narrow rail); collapsing only lasts for the session.
        foreach (var group in Groups)
        {
            group.IsExpanded = !Settings.SidebarCollapsed;
        }

        TopNav = [_quickNav, _compressNav, new NavDivider(), VideoGroup, AudioGroup, ImageGroup, ToolsGroup, new NavDivider(), _queueNav];
        BottomNav = [new NavItem(Dependencies), new NavItem(SettingsPage), new NavItem(About)];
        _isSidebarCollapsed = Settings.SidebarCollapsed;

        foreach (var page in AllPages)
        {
            page.LoadState(Settings.Pages);
        }

        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _toastTimer.Tick += (_, _) => CloseToast();
        _queueTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _queueTimer.Tick += (_, _) => UpdateQueueStatus();
        // Explorer starts one process per selected file: collect them briefly into one batch.
        _externalTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _externalTimer.Tick += (_, _) => FlushExternal();

        _pillTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _pillTimer.Tick += (_, _) => FfmpegPillExpanded = false;
        _exitTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _exitTimer.Tick += (_, _) =>
        {
            _exitTimer.Stop();
            ExitRequested?.Invoke();
        };

        HideHints = !Settings.ShowHints;
        ApplyRuntimeSettings();
        Navigate(_quickNav);
        Machine.Start();
    }

    // ================================================================ Services

    public AppSettings Settings { get; }

    public UiHost Host { get; }

    public FfmpegLocator Locator { get; }

    /// <summary>Optional modules (upscalers, frame interpolation).</summary>
    public ModuleLocator Modules { get; }

    public FfmpegRunner Runner { get; }

    public MediaProbe Probe { get; }

    /// <summary>Small previews in the file lists (Settings › General › Thumbnails in lists).</summary>
    public ThumbnailService Thumbnails { get; }

    public ConversionService Conversion { get; }

    public PlayerService Player { get; }

    /// <summary>Mini player of the audio file lists.</summary>
    public AudioPreview AudioPreview { get; }

    public JobQueue Queue { get; }

    /// <summary>Settings › General › Show hints off: all "i" hints collapse.</summary>
    [ObservableProperty] private bool _hideHints;

    /// <summary>System detection (CPU, memory, GPUs, power source) for the performance profiles.</summary>
    public SystemMonitor Machine { get; }

    /// <summary>Resource values of the current performance profile.</summary>
    public ResourcePlan Plan { get; private set; } = null!;

    private EncodeEnvironment _encode = EncodeEnvironment.Default;
    private volatile bool _ecoQos;
    private bool _pausedForBattery;

    public string FfmpegVersion { get; private set; } = "";

    /// <summary>FFmpeg found/removed/changed.</summary>
    public event Action? FfmpegChanged;

    /// <summary>Encoder list or hardware test results changed.</summary>
    public event Action? EncodersChanged;

    /// <summary>Queue progress for the window (taskbar): busy, fraction, has errors.</summary>
    public event Action<bool, double, bool>? QueueProgressChanged;

    /// <summary>The display profile changed (the window rescales).</summary>
    public event Action? DisplayProfileChanged;

    /// <summary>Bring the window to the front (Explorer actions).</summary>
    public event Action? ShowWindowRequested;

    /// <summary>Hide the window to the notification area (close/minimize to tray, "continue in background").</summary>
    public event Action? HideToTrayRequested;

    /// <summary>Exit the app (tray menu, background jobs done).</summary>
    public event Action? ExitRequested;

    /// <summary>Queue, profile or language changed: the tray icon updates its menu and tooltip.</summary>
    public event Action? TrayStateChanged;

    /// <summary>Set by the window: true while it is in the foreground.</summary>
    public Func<bool> IsWindowActive { get; set; } = () => true;

    /// <summary>Set by the app: true while the window is hidden in the notification area (or was never shown).</summary>
    public Func<bool> IsWindowHidden { get; set; } = () => false;

    /// <summary>CONVERTit was started only for Explorer background jobs: exit when they are done.</summary>
    public bool ExitWhenDrained { get; set; }

    public void HideToTray() => HideToTrayRequested?.Invoke();

    public void RequestExit() => ExitRequested?.Invoke();

    /// <summary>The window became visible: no automatic exit any more.</summary>
    public void OnWindowShown()
    {
        ExitWhenDrained = false;
        _exitTimer.Stop();
        Player.WarmUp();
    }

    // ================================================================ Pages

    public QuickConvertViewModel QuickConvert { get; }
    public CompressViewModel Compress { get; }
    public VideoConvertViewModel VideoConvert { get; }
    public TrimViewModel VideoTrim { get; }
    public AnimationViewModel Animation { get; }
    public RemuxViewModel Remux { get; }
    public ExtractAudioViewModel ExtractAudio { get; }
    public AudioConvertViewModel AudioConvert { get; }
    public TrimViewModel AudioTrim { get; }
    public GameAudioViewModel GameAudio { get; }
    public TagEditorViewModel Tags { get; }
    public ImageConvertViewModel ImageConvert { get; }
    public ImageConvertViewModel ImageResize { get; }
    public GameTextureViewModel GameTextures { get; }
    public IconViewModel IconPage { get; }
    public AppIconsViewModel AppIcons { get; }
    public SpritesViewModel Sprites { get; }
    public GodotVideoViewModel GodotVideo { get; }
    public UpscaleViewModel Upscale { get; }
    public DenoiseViewModel Denoise { get; }
    public InterpolateViewModel Interpolate { get; }
    public ThumbcacheViewModel Thumbcache { get; }
    public QueueViewModel QueuePage { get; }
    public DependenciesViewModel Dependencies { get; }
    public SettingsViewModel SettingsPage { get; }
    public AboutViewModel About { get; }

    /// <summary>All pages in sidebar order (Ctrl+↑/↓ walks through them).</summary>
    private IReadOnlyList<PageViewModel> AllPages =>
    [
        QuickConvert, Compress, VideoConvert, VideoTrim, Animation, Remux, ExtractAudio, GodotVideo, AudioConvert, AudioTrim, Tags, GameAudio,
        ImageConvert, ImageResize, GameTextures, IconPage, AppIcons, Sprites, Upscale, Denoise, Interpolate, Thumbcache,
        QueuePage, Dependencies, SettingsPage, About
    ];

    /// <summary>Keyboard: previous/next page in sidebar order.</summary>
    public void NavigateRelative(int delta)
    {
        var pages = AllPages;
        var index = CurrentPage is null ? 0 : Math.Max(0, pages.ToList().IndexOf(CurrentPage));
        NavigateTo(pages[(index + delta + pages.Count) % pages.Count]);
    }

    // ================================================================ Navigation

    private readonly NavItem _quickNav;
    private readonly NavItem _compressNav;
    private readonly NavItem _queueNav;

    public NavGroup VideoGroup { get; }
    public NavGroup AudioGroup { get; }
    public NavGroup ToolsGroup { get; }
    public NavGroup ImageGroup { get; }

    private NavGroup[] Groups => [VideoGroup, AudioGroup, ImageGroup, ToolsGroup];

    /// <summary>Quick Convert, Compress, the three groups and the queue (with dividers).</summary>
    public IReadOnlyList<object> TopNav { get; }

    public IReadOnlyList<NavItem> BottomNav { get; }

    private IEnumerable<NavItem> AllNavItems =>
        TopNav.SelectMany(e => e switch
        {
            NavItem item => new[] { item },
            NavGroup group => group.Items.ToArray(),
            _ => Array.Empty<NavItem>()
        }).Concat(BottomNav);

    [ObservableProperty] private PageViewModel? _currentPage;
    [ObservableProperty] private bool _isSidebarCollapsed;

    public double SidebarWidth => IsSidebarCollapsed ? 72 : 256;

    public Geometry? CurrentPageIcon => CurrentPage?.Icon;

    public bool IsDark => Application.Current?.ActualThemeVariant != ThemeVariant.Light;

    partial void OnIsSidebarCollapsedChanged(bool value)
    {
        OnPropertyChanged(nameof(SidebarWidth));
        foreach (var group in Groups)
        {
            group.RefreshLayout();
        }

        Settings.SidebarCollapsed = value;
    }

    partial void OnCurrentPageChanged(PageViewModel? value) => OnPropertyChanged(nameof(CurrentPageIcon));

    [RelayCommand]
    private void Navigate(NavItem? item)
    {
        if (item is null)
        {
            return;
        }

        foreach (var nav in AllNavItems)
        {
            nav.IsSelected = ReferenceEquals(nav, item);
        }

        foreach (var group in Groups)
        {
            group.ContainsSelection = group.Items.Contains(item);
            if (group.ContainsSelection && !group.IsExpanded)
            {
                group.IsExpanded = true;
            }
        }

        // Collapsed sidebar: the group of the page unfolds (accordion closes the others).
        if (IsSidebarCollapsed)
        {
            foreach (var group in Groups.Where(g => g.ContainsSelection && !g.IsRailExpanded))
            {
                ToggleRailGroup(group);
            }
        }

        if (!ReferenceEquals(CurrentPage, item.Page))
        {
            // Leaving a page ends the mini player.
            AudioPreview.Stop();

            // History like a browser: back/forward entries; a new page clears the forward list.
            if (!_historyMove && CurrentPage is not null)
            {
                _back.Add(CurrentPage);
                if (_back.Count > HistoryLimit)
                {
                    _back.RemoveAt(0);
                }

                _forward.Clear();
            }
        }

        CurrentPage = item.Page;
        item.Page.OnActivated();
        GoBackCommand.NotifyCanExecuteChanged();
        GoForwardCommand.NotifyCanExecuteChanged();
    }

    private const int HistoryLimit = 50;
    private readonly List<PageViewModel> _back = [];
    private readonly List<PageViewModel> _forward = [];
    private bool _historyMove;

    private bool CanGoBack() => _back.Count > 0;

    private bool CanGoForward() => _forward.Count > 0;

    /// <summary>Back to the previous page (header arrow, mouse back button, Alt+Left).</summary>
    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void GoBack() => MoveInHistory(_back, _forward);

    /// <summary>Forward again (header arrow, mouse forward button, Alt+Right).</summary>
    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private void GoForward() => MoveInHistory(_forward, _back);

    private void MoveInHistory(List<PageViewModel> from, List<PageViewModel> to)
    {
        if (from.Count == 0)
        {
            return;
        }

        var page = from[^1];
        from.RemoveAt(from.Count - 1);
        if (CurrentPage is not null)
        {
            to.Add(CurrentPage);
        }

        _historyMove = true;
        try
        {
            NavigateTo(page);
        }
        finally
        {
            _historyMove = false;
        }
    }

    public void NavigateTo(PageViewModel page)
    {
        var item = AllNavItems.FirstOrDefault(n => ReferenceEquals(n.Page, page));
        if (item is not null)
        {
            Navigate(item);
        }
    }

    /// <summary>Collapsed sidebar: opens the group's sub pages as icons; accordion closes the other groups.</summary>
    public void ToggleRailGroup(NavGroup group)
    {
        var open = !group.IsRailExpanded;
        if (open && Settings.SidebarAccordion)
        {
            foreach (var other in Groups.Where(g => !ReferenceEquals(g, group)))
            {
                other.IsRailExpanded = false;
            }
        }

        group.IsRailExpanded = open;
    }

    public void ToggleGroup(NavGroup group)
    {
        group.IsExpanded = !group.IsExpanded;
        Settings.CollapsedGroups = Groups.Where(g => !g.IsExpanded).Select(g => g.Key).ToList();
    }

    [RelayCommand]
    private void ToggleSidebar() => IsSidebarCollapsed = !IsSidebarCollapsed;

    [RelayCommand]
    private void ToggleTheme()
    {
        Settings.Theme = IsDark ? ThemeOption.Light : ThemeOption.Dark;
        ApplyTheme(Settings.Theme);
        SettingsPage.Theme = Settings.Theme;
        Settings.Save();
    }

    [RelayCommand]
    private void OpenQueue() => NavigateTo(QueuePage);

    [RelayCommand]
    private void OpenDependenciesPage() => NavigateTo(Dependencies);

    /// <summary>
    /// Header pill: installed → first click unfolds the version, second click opens the dependencies page;
    /// missing → always unfolded, opens the setup dialog.
    /// </summary>
    [RelayCommand]
    private void FfmpegPill()
    {
        if (!Locator.IsAvailable)
        {
            OpenFfmpegSetup();
            return;
        }

        if (!FfmpegPillExpanded)
        {
            FfmpegPillExpanded = true;
            _pillTimer.Stop();
            _pillTimer.Start();
            return;
        }

        FfmpegPillExpanded = false;
        NavigateTo(Dependencies);
    }

    private readonly DispatcherTimer _pillTimer;
    private readonly DispatcherTimer _exitTimer;

    /// <summary>The installed pill shows its text (after a click, for a few seconds).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowFfmpegPillText))]
    private bool _ffmpegPillExpanded;

    /// <summary>Text visible: unfolded, or FFmpeg is missing (the user should act).</summary>
    public bool ShowFfmpegPillText => !Locator.IsAvailable || FfmpegPillExpanded;

    /// <summary>Click somewhere else: fold the pill again.</summary>
    public void CollapseFfmpegPill()
    {
        _pillTimer.Stop();
        FfmpegPillExpanded = false;
    }

    /// <summary>"Download FFmpeg" buttons of the pages: the setup dialog asks for confirmation first.</summary>
    public void OpenDependencies(bool startDownload)
    {
        if (startDownload && !Locator.IsAvailable)
        {
            OpenFfmpegSetup();
            return;
        }

        NavigateTo(Dependencies);
    }

    /// <summary>Files dropped on the window: the current page takes them, otherwise Quick Convert.</summary>
    public void Drop(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
        {
            return;
        }

        if (CurrentPage?.AcceptDrop(paths) == true)
        {
            return;
        }

        NavigateTo(QuickConvert);
        if (!QuickConvert.AcceptDrop(paths))
        {
            ShowToast(Loc.T("ToastUnsupportedDrop"), error: true);
        }
    }

    // ================================================================ FFmpeg setup dialog (first start)

    [ObservableProperty] private bool _showFfmpegSetup;

    /// <summary>First start (or until "Later"): offer the FFmpeg download when it is missing.</summary>
    public void OfferFfmpegSetup()
    {
        if (!Locator.IsAvailable && !Settings.FfmpegSetupDismissed)
        {
            OpenFfmpegSetup();
        }
    }

    public void OpenFfmpegSetup()
    {
        ShowFfmpegSetup = true;
        _ = Dependencies.PrepareSetupAsync();
    }

    [RelayCommand]
    private void CloseFfmpegSetup()
    {
        if (Dependencies.IsBusy)
        {
            return;
        }

        if (!Locator.IsAvailable)
        {
            Settings.FfmpegSetupDismissed = true;
            Settings.Save();
        }

        ShowFfmpegSetup = false;
    }

    // ================================================================ Header

    public bool FfmpegReady => Locator.IsAvailable;

    /// <summary>
    /// Header status of all dependencies: FFmpeg and ffprobe are required (orange when missing), the optional modules
    /// are counted ("modules 1/3").
    /// </summary>
    public string FfmpegPillText => Locator.IsAvailable
        ? Loc.F("PillDependencies", FfmpegVersion.Length > 0 ? FfmpegVersion : "✓", ModulesReady, ModuleCatalog.Optional.Count)
        : Loc.T("PillFfmpegMissing");

    /// <summary>Number of installed optional modules.</summary>
    public int ModulesReady => ModuleCatalog.Optional.Count(m => Modules.IsAvailable(m.Id));

    /// <summary>Tooltip: one line per dependency with ✓ / –.</summary>
    public string FfmpegPillTip
    {
        get
        {
            static string Line(string name, bool ok, string extra = "") => $"{(ok ? "✓" : "–")}  {name}{(extra.Length > 0 ? " " + extra : "")}";
            var lines = new List<string>
            {
                Loc.T("TipDependenciesHeader"),
                Line("ffmpeg", Locator.FfmpegPath is not null, FfmpegVersion),
                Line("ffprobe", Locator.FfprobePath is not null)
            };
            lines.AddRange(ModuleCatalog.Optional.Select(m => Line(m.Name, Modules.IsAvailable(m.Id))));
            lines.Add("");
            lines.Add(Loc.T(Locator.IsAvailable ? "TipFfmpegInstalled" : "TipFfmpegMissing"));
            return string.Join(Environment.NewLine, lines);
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEncoderTest))]
    private bool _isTestingEncoders;

    /// <summary>The encoder test is shown in the status slot when nothing else is running.</summary>
    public bool ShowEncoderTest => IsTestingEncoders && !LineActive;
    [ObservableProperty] private bool _queueBusy;
    [ObservableProperty] private double _queueProgress;
    [ObservableProperty] private string _queueStatusText = "";

    // ================================================================ BusyActivity (header)

    private readonly List<BusyActivity> _activities = [];

    [ObservableProperty] private bool _hasActivity;
    [ObservableProperty] private string _activityText = "";
    [ObservableProperty] private bool _activityIndeterminate = true;
    [ObservableProperty] private double _activityProgress;

    /// <summary>Starts a header activity ("Loading video …"); dispose it when done.</summary>
    public BusyActivity BeginActivity(string text)
    {
        var activity = new BusyActivity(this, text);
        _activities.Add(activity);
        RefreshActivity();
        return activity;
    }

    internal void EndActivity(BusyActivity activity)
    {
        if (_activities.Remove(activity))
        {
            RefreshActivity();
        }
    }

    internal void RefreshActivity()
    {
        var current = _activities.LastOrDefault();
        HasActivity = current is not null;
        ActivityText = current is null ? "" : _activities.Count > 1 ? $"{current.Text} (+{_activities.Count - 1})" : current.Text;
        ActivityIndeterminate = current?.Progress is null;
        ActivityProgress = (current?.Progress ?? 0) * 100;
        RaiseHeaderStatus();
    }

    // ================================================================ Header status slot and progress line

    /// <summary>The line under the header works as progress bar: loading first, then the queue.</summary>
    public bool LineActive => HasActivity || QueueBusy;

    public bool LineIndeterminate => HasActivity ? ActivityIndeterminate : false;

    public double LineValue => HasActivity ? ActivityProgress : QueueProgress;

    /// <summary>Text of the fixed-width status slot (activity or queue).</summary>
    public string HeaderStatusText => HasActivity
        ? ActivityText
        : QueueBusy ? $"{QueueStatusText} · {QueueProgress:0} %" + (Queue.IsPaused ? " · " + Loc.T("StatePaused") : "") : "";

    /// <summary>The slot shows the queue (click opens it).</summary>
    public bool HeaderStatusIsQueue => !HasActivity && QueueBusy;

    public bool HeaderStatusIsActivity => HasActivity;

    private void RaiseHeaderStatus()
    {
        OnPropertyChanged(nameof(LineActive));
        OnPropertyChanged(nameof(LineIndeterminate));
        OnPropertyChanged(nameof(LineValue));
        OnPropertyChanged(nameof(HeaderStatusText));
        OnPropertyChanged(nameof(HeaderStatusIsQueue));
        OnPropertyChanged(nameof(HeaderStatusIsActivity));
        OnPropertyChanged(nameof(ShowEncoderTest));
    }

    // ================================================================ Toast (in-app)

    [ObservableProperty] private string _toast = "";
    [ObservableProperty] private bool _toastIsError;
    [ObservableProperty] private bool _hasToast;

    public void ShowToast(string text, bool error = false)
    {
        if (!Settings.InAppNotifications && !error)
        {
            return;
        }

        Toast = text;
        ToastIsError = error;
        HasToast = true;
        _toastTimer.Stop();
        _toastTimer.Interval = TimeSpan.FromSeconds(error ? 8 : 4);
        _toastTimer.Start();
    }

    [RelayCommand]
    private void CloseToast()
    {
        _toastTimer.Stop();
        HasToast = false;
    }

    // ================================================================ FFmpeg & encoders

    public bool HasEncoder(string encoder) => Settings.Encoders.Count == 0 || Settings.Encoders.Contains(encoder);

    public bool HardwareWorks(string encoder) => Settings.WorkingHardwareEncoders.Contains(encoder);

    /// <summary>Finds FFmpeg, reads its version and (when it changed) tests the encoders.</summary>
    public async Task RefreshFfmpegAsync(bool retestHardware = false)
    {
        Locator.Refresh();
        FfmpegVersion = Locator.IsAvailable ? await Runner.VersionAsync() ?? "" : "";
        RaiseFfmpeg();

        if (!Locator.IsAvailable)
        {
            return;
        }

        if (retestHardware || Settings.EncoderFfmpegVersion != FfmpegVersion || Settings.Encoders.Count == 0)
        {
            IsTestingEncoders = true;
            try
            {
                var report = await HardwareProbe.ProbeAsync(Runner);
                Settings.EncoderFfmpegVersion = report.FfmpegVersion;
                Settings.Encoders = [.. report.Encoders.Order()];
                Settings.WorkingHardwareEncoders = [.. report.WorkingHardware.Order()];
                Settings.Save();
            }
            catch
            {
                // Encoder test failed: everything stays selectable, software encoders are used.
            }
            finally
            {
                IsTestingEncoders = false;
            }

            EncodersChanged?.Invoke();
        }
    }

    private void RaiseFfmpeg()
    {
        OnPropertyChanged(nameof(ModulesReady));
        OnPropertyChanged(nameof(ShowFfmpegPillText));
        OnPropertyChanged(nameof(FfmpegReady));
        OnPropertyChanged(nameof(FfmpegPillText));
        OnPropertyChanged(nameof(FfmpegPillTip));
        FfmpegChanged?.Invoke();
    }

    // ================================================================ Settings

    public void ApplyLanguage(string code)
    {
        Loc.Apply(code);
        foreach (var group in Groups)
        {
            group.RefreshText();
        }

        foreach (var nav in AllNavItems)
        {
            nav.RefreshText();
        }

        foreach (var page in AllPages)
        {
            page.OnLanguageChanged();
        }

        RaiseFfmpeg();
        UpdateQueueStatus();
        TrayStateChanged?.Invoke();

        // The Explorer menu carries translated labels.
        if (Settings.ExplorerMenu)
        {
            ExplorerIntegration.Refresh(Settings);
        }
    }

    public void ApplyTheme(ThemeOption theme)
    {
        if (Application.Current is { } app)
        {
            app.RequestedThemeVariant = theme switch
            {
                ThemeOption.Light => ThemeVariant.Light,
                ThemeOption.System => ThemeVariant.Default,
                _ => ThemeVariant.Dark
            };
        }

        OnPropertyChanged(nameof(IsDark));
        RaiseFfmpeg();
        foreach (var group in Groups)
        {
            group.RefreshLayout();
        }
    }

    public void ApplyDisplayProfile() => DisplayProfileChanged?.Invoke();

    /// <summary>
    /// Applies the performance profile: parallel jobs, priority, EcoQoS and encode settings. Running FFmpeg processes
    /// get the new priority immediately; threads and GPU turbo apply to the next jobs.
    /// </summary>
    public void ApplyRuntimeSettings()
    {
        var customPriority = Settings.Priority switch
        {
            PriorityOption.Normal => JobPriority.Normal,
            PriorityOption.Idle => JobPriority.Idle,
            _ => JobPriority.BelowNormal
        };
        Plan = PerformancePlanner.Plan(Settings.PerformanceProfile, Machine.Info, Settings.MediaParallel, Settings.ImageParallel, customPriority, true);

        Queue.MediaParallel = Plan.MediaParallel;
        Queue.ImageParallel = Plan.ImageParallel;
        var priority = Plan.Priority switch
        {
            JobPriority.Normal => ProcessPriorityClass.Normal,
            JobPriority.Idle => ProcessPriorityClass.Idle,
            _ => ProcessPriorityClass.BelowNormal
        };
        var ecoChanged = _ecoQos != Plan.EcoQos;
        _ecoQos = Plan.EcoQos;
        if (Runner.Priority != priority || ecoChanged)
        {
            Runner.Priority = priority;
            var eco = Plan.EcoQos;
            Runner.ForEachRunning(process =>
            {
                FfmpegRunner.SetPriority(process, priority);
                if (OperatingSystem.IsWindows())
                {
                    Platform.Windows.ProcessPower.SetEcoQos(process, eco);
                }
            });
        }

        _encode = new EncodeEnvironment
        {
            HardwareDecode = Settings.HardwareDecode,
            GpuTurbo = Settings.GpuTurbo && Plan.GpuTurbo,
            Threads = Plan.Threads,
            HdrToSdr = Settings.HdrToSdr,
            AutoDeinterlace = Settings.AutoDeinterlace
        };

        Queue.Pump();
        EncodersChanged?.Invoke();
        UpdatePowerRequest(QueueBusy);
        CheckBattery();
        SettingsPage?.RefreshSystem();
        TrayStateChanged?.Invoke();
    }

    /// <summary>Power source or hardware detection changed (Auto profile switches, battery pause).</summary>
    private void OnMachineChanged() => ApplyRuntimeSettings();

    /// <summary>Eco profile on battery: below 20 % the queue pauses (running jobs finish), on mains it continues.</summary>
    private void CheckBattery()
    {
        var info = Machine.Info;
        var low = Plan.PauseOnLowBattery && info.OnBattery && info.BatteryPercent is >= 0 and < PerformancePlanner.LowBatteryPercent;
        if (low && !Queue.IsPaused && Queue.IsBusy)
        {
            Queue.IsPaused = true;
            _pausedForBattery = true;
            ShowToast(Loc.T("ToastPausedBattery"));
        }
        else if (!low && _pausedForBattery)
        {
            _pausedForBattery = false;
            Queue.IsPaused = false;
            ShowToast(Loc.T("ToastResumedBattery"));
        }
    }

    // ================================================================ Tray menu commands

    /// <summary>Pause/continue the queue (running jobs finish).</summary>
    [RelayCommand]
    private void TogglePause()
    {
        _pausedForBattery = false;
        Queue.IsPaused = !Queue.IsPaused;
        UpdateQueueStatus();
    }

    [RelayCommand]
    private void CancelAllJobs()
    {
        Queue.CancelAll();
        Queue.IsPaused = false;
        UpdateQueueStatus();
    }

    /// <summary>Pages a file can be sent to from a list's right-click menu (all kinds of the selection must fit).</summary>
    public IReadOnlyList<PageViewModel> SendTargets(IReadOnlyCollection<MediaKind> kinds)
    {
        IEnumerable<PageViewModel> For(MediaKind kind) => kind switch
        {
            MediaKind.Video => [VideoConvert, VideoTrim, Compress, Animation, Remux, ExtractAudio, GodotVideo, Upscale, Denoise, Interpolate, Sprites],
            MediaKind.Audio => [AudioConvert, AudioTrim, Tags, GameAudio, Compress, Denoise],
            MediaKind.Image => [ImageConvert, ImageResize, Compress, GameTextures, IconPage, AppIcons, Sprites, Upscale],
            _ => []
        };

        if (kinds.Count == 0)
        {
            return [];
        }

        var result = For(kinds.First()).ToList();
        foreach (var kind in kinds.Skip(1))
        {
            result = result.Intersect(For(kind)).ToList();
        }

        return result;
    }

    /// <summary>Opens a page and hands it the files (like dropping them there).</summary>
    public void SendTo(PageViewModel page, IReadOnlyList<string> paths)
    {
        NavigateTo(page);
        if (!page.AcceptDrop(paths))
        {
            ShowToast(Loc.T("ToastUnsupportedDrop"), error: true);
        }
    }

    /// <summary>Tray submenu: switch the performance profile.</summary>
    public void SetPerformanceProfile(PerformanceProfile profile)
    {
        SettingsPage.PerformanceProfile = profile;
    }

    public void SaveState()
    {
        if (Settings.RememberSettings)
        {
            foreach (var page in AllPages)
            {
                page.SaveState(Settings.Pages);
            }
        }

        Settings.Save();
    }

    // ================================================================ Explorer context menu & later starts

    /// <summary>Files from Explorer ("--action id"), "Open with" or a second start; collected for a moment.</summary>
    public void QueueExternal(string? action, IReadOnlyList<string> files)
    {
        if (files.Count == 0)
        {
            return;
        }

        var key = action ?? ExplorerIntegration.Open;
        var entry = _external.FirstOrDefault(e => e.Action == key);
        if (entry.Files is null)
        {
            _external.Add((key, [.. files]));
        }
        else
        {
            entry.Files.AddRange(files.Where(f => !entry.Files.Contains(f)));
        }

        _externalTimer.Stop();
        _externalTimer.Start();
    }

    private void FlushExternal()
    {
        _externalTimer.Stop();
        var batches = _external.ToList();
        _external.Clear();
        var needsWindow = false;
        foreach (var (action, files) in batches)
        {
            needsWindow |= RunExternal(action, files);
        }

        if (needsWindow)
        {
            ShowWindowRequested?.Invoke();
        }
    }

    /// <summary>Direct Explorer actions (convert, extract) that can run without the window.</summary>
    public static bool IsBackgroundAction(string? action) => action is ExplorerIntegration.VideoMp4 or ExplorerIntegration.ExtractAudio
        or ExplorerIntegration.AudioMp3 or ExplorerIntegration.AudioOgg
        or ExplorerIntegration.ImagePng or ExplorerIntegration.ImageJpg or ExplorerIntegration.ImageWebp or ExplorerIntegration.IcoExtract;

    /// <summary>Runs one Explorer request; returns true when the window has to be shown.</summary>
    private bool RunExternal(string action, List<string> files)
    {
        List<SourceRef> Of(MediaKind kind) =>
            files.Where(f => File.Exists(f) && MediaTypes.Detect(f) == kind).Select(f => new SourceRef(f, null)).ToList();

        var needsFfmpeg = action is ExplorerIntegration.VideoMp4 or ExplorerIntegration.ExtractAudio
            or ExplorerIntegration.AudioMp3 or ExplorerIntegration.AudioOgg;
        if (needsFfmpeg && !Locator.IsAvailable)
        {
            // Without FFmpeg the files wait on Quick Convert; the setup dialog explains why.
            action = ExplorerIntegration.Open;
            OpenFfmpegSetup();
        }

        // Settings › Background: direct actions run without bringing the window to the front.
        var background = Settings.BackgroundExplorerJobs && IsBackgroundAction(action);
        _externalInBackground = background;

        try
        {
            switch (action)
            {
                case ExplorerIntegration.Compress:
                    NavigateTo(Compress);
                    _ = Compress.Sources.AddPathsAsync(files);
                    return true;
                case ExplorerIntegration.EditTags:
                    NavigateTo(Tags);
                    _ = Tags.Sources.AddPathsAsync(files);
                    return true;
                case ExplorerIntegration.VideoTrim or ExplorerIntegration.AudioTrim or ExplorerIntegration.VideoGif
                    or ExplorerIntegration.Remux or ExplorerIntegration.ImageResize or ExplorerIntegration.MakeIcon
                    or ExplorerIntegration.MakeAppIcon or ExplorerIntegration.Thumbcache:
                {
                    // Explorer actions that need settings: open the page with the files.
                    PageViewModel page = action switch
                    {
                        ExplorerIntegration.VideoTrim => VideoTrim,
                        ExplorerIntegration.AudioTrim => AudioTrim,
                        ExplorerIntegration.VideoGif => Animation,
                        ExplorerIntegration.Remux => Remux,
                        ExplorerIntegration.ImageResize => ImageResize,
                        ExplorerIntegration.MakeIcon => IconPage,
                        ExplorerIntegration.MakeAppIcon => AppIcons,
                        _ => Thumbcache
                    };
                    SendTo(page, files);
                    return true;
                }
                case ExplorerIntegration.IcoExtract:
                    _ = ExtractIconsAsync(files.Where(File.Exists).ToList());
                    return !background;
                case ExplorerIntegration.VideoMp4:
                {
                    var options = JsonFile.Clone(VideoConvert.Video.Options);
                    options.Container = VideoContainer.Mp4;
                    if (!FormatCatalog.CodecsFor(VideoContainer.Mp4).Contains(options.Codec))
                    {
                        options.Codec = VideoCodec.H264;
                    }

                    EnqueueExternal(Conversion.VideoConvert(Of(MediaKind.Video), options, VideoConvert.Output.Options, skipSame: false));
                    return !background;
                }
                case ExplorerIntegration.ExtractAudio:
                    _ = ExtractExternalAsync(Of(MediaKind.Video));
                    return !background;
                case ExplorerIntegration.AudioMp3 or ExplorerIntegration.AudioOgg:
                {
                    var options = JsonFile.Clone(AudioConvert.Audio.Options);
                    var format = action == ExplorerIntegration.AudioMp3 ? AudioFormat.Mp3 : AudioFormat.Vorbis;
                    if (options.Format != format)
                    {
                        options.Format = format;
                        options.BitrateKbps = 320;
                        options.VorbisQuality = 6;
                    }

                    EnqueueExternal(Conversion.AudioConvert(Of(MediaKind.Audio), options, AudioConvert.Output.Options, skipSame: false));
                    return !background;
                }
                case ExplorerIntegration.ImagePng or ExplorerIntegration.ImageJpg or ExplorerIntegration.ImageWebp:
                {
                    var options = JsonFile.Clone(ImageConvert.Image.Options);
                    options.Format = action switch
                    {
                        ExplorerIntegration.ImagePng => ImageFormat.Png,
                        ExplorerIntegration.ImageJpg => ImageFormat.Jpeg,
                        _ => ImageFormat.WebP
                    };
                    EnqueueExternal(Conversion.ImageConvert(Of(MediaKind.Image), options, ImageConvert.Output.Options, skipSame: false, "OpConvertImage"));
                    return !background;
                }
                default:
                    // "Open in CONVERTit": always Quick Convert, independent of the page that is open.
                    NavigateTo(QuickConvert);
                    if (!QuickConvert.AcceptDrop(files))
                    {
                        ShowToast(Loc.T("ToastUnsupportedDrop"), error: true);
                    }

                    return true;
            }
        }
        catch (Exception ex)
        {
            ShowToast(ex.Message, error: true);
            return true;
        }
    }

    /// <summary>Explorer "Export images from ICO": every size as PNG into "name_icons" next to the file.</summary>
    private async Task ExtractIconsAsync(List<string> files)
    {
        var count = 0;
        string? folder = null;
        using (BeginActivity(Loc.T("ActivityExtractIcons")))
        {
            foreach (var file in files)
            {
                try
                {
                    folder = Path.Combine(Path.GetDirectoryName(file) ?? "", Path.GetFileNameWithoutExtension(file) + "_icons");
                    var target = folder;
                    count += (await Task.Run(() => Engines.Image.ImageEngine.ExtractIcon(file, target))).Count;
                }
                catch (Exception ex)
                {
                    ShowToast(ex.Message, error: true);
                }
            }
        }

        var text = Loc.F("ToastIconsExtracted", count);
        ShowToast(text);
        if (IsWindowHidden())
        {
            ShowSystemNotification("CONVERTit", text, folder);
            if (ExitWhenDrained && !Queue.IsBusy)
            {
                _exitTimer.Start();
            }
        }
    }

    private async Task ExtractExternalAsync(List<SourceRef> sources)
    {
        try
        {
            EnqueueExternal(await Conversion.ExtractAudioAsync(sources, ExtractAudio.Extract, ExtractAudio.Audio.Options, ExtractAudio.Output.Options,
                CancellationToken.None));
        }
        catch (Exception ex)
        {
            ShowToast(ex.Message, error: true);
        }
    }

    private bool _externalInBackground;

    private void EnqueueExternal(List<ConversionJob> jobs)
    {
        if (jobs.Count == 0)
        {
            ShowToast(Loc.T("ToastUnsupportedDrop"), error: true);
            if (_externalInBackground && IsWindowHidden())
            {
                ShowSystemNotification("CONVERTit", Loc.T("ToastUnsupportedDrop"), null);
                // Started only for this request: nothing to do, so exit again.
                if (ExitWhenDrained && !Queue.IsBusy)
                {
                    _exitTimer.Start();
                }
            }

            return;
        }

        Queue.Enqueue(jobs);
        if (!_externalInBackground)
        {
            NavigateTo(QueuePage);
        }

        ShowToast(Loc.F("ToastQueued", jobs.Count));
    }

    // ================================================================ Queue integration

    private void OnQueueChanged()
    {
        UpdateQueueStatus();
        if (Queue.IsBusy)
        {
            _queueTimer.Start();
        }
    }

    private void UpdateQueueStatus()
    {
        var busy = Queue.IsBusy;
        var active = Queue.ActiveCount;
        QueueBusy = busy;
        QueueProgress = Queue.OverallProgress * 100;
        QueueStatusText = busy ? Loc.F("QueueStatus", active) : "";
        _queueNav.Badge = active > 0 ? active.ToString() : "";
        QueuePage.Refresh();
        RaiseHeaderStatus();
        TrayStateChanged?.Invoke();

        var failed = Queue.Jobs.Any(j => j.State == JobState.Failed && j.FinishedAt > DateTime.Now.AddMinutes(-60));
        QueueProgressChanged?.Invoke(busy, Queue.OverallProgress, failed);

        if (busy != _wasBusy)
        {
            _wasBusy = busy;
            UpdatePowerRequest(busy);
        }

        if (!busy)
        {
            _queueTimer.Stop();
        }
    }

    private void UpdatePowerRequest(bool busy)
    {
        if (OperatingSystem.IsWindows())
        {
            Platform.Windows.PowerRequest.Set(busy && Settings.PreventSleep);
        }
    }

    private void OnQueueDrained(BatchSummary summary)
    {
        UpdateQueueStatus();
        if (summary.Done + summary.Failed + summary.Skipped == 0)
        {
            return;
        }

        var text = Loc.F("BatchDoneText", summary.Done, summary.Failed, summary.Skipped);
        if (summary.SourceBytes > 0 && summary.OutputBytes > 0)
        {
            var percent = (1 - (double)summary.OutputBytes / summary.SourceBytes) * 100;
            text += " · " + Loc.F(percent >= 0 ? "BatchSaved" : "BatchGrew", Units.Bytes(Math.Abs(summary.SourceBytes - summary.OutputBytes)), Math.Abs(percent).ToString("0"));
        }

        ShowToast(text, summary.Failed > 0);
        if ((Settings.SystemNotifications || IsWindowHidden()) && !IsWindowActive())
        {
            ShowSystemNotification(Loc.T(summary.Failed > 0 ? "BatchDoneWithErrors" : "BatchDoneTitle"), text, summary.OutputFolder);
        }

        if (Settings.OpenFolderWhenDone && summary.Done > 0 && summary.OutputFolder is not null)
        {
            AppEnvironment.OpenFolder(summary.OutputFolder);
        }

        // Started only for Explorer background jobs: exit shortly after the notification.
        if (ExitWhenDrained && IsWindowHidden() && !Queue.IsBusy)
        {
            _exitTimer.Start();
        }
    }

    /// <summary>Windows notification; falls back to the in-app toast. Returns the reason when nothing was shown.</summary>
    public string? ShowSystemNotification(string title, string text, string? folder)
    {
        if (!OperatingSystem.IsWindows())
        {
            ShowToast(text);
            return null;
        }

        var result = Platform.Windows.Notifications.Show(title, text, folder, Loc.T("BtnOpenFolder"));
        if (!result.Shown)
        {
            ShowToast(text);
        }

        return result.Error;
    }

    private static Platform.Windows.JobObject? TryCreateJobObject()
    {
        try
        {
            return OperatingSystem.IsWindows() ? new Platform.Windows.JobObject() : null;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        Machine.Dispose();
        Queue.CancelAll();
        VideoTrim.Dispose();
        AudioTrim.Dispose();
        AudioPreview.Dispose();
        Player.Dispose();
        if (OperatingSystem.IsWindows())
        {
            Platform.Windows.PowerRequest.Set(false);
        }
    }
}
