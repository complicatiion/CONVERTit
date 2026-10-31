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
using CONVERTit.Core.Jobs;
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
        Locator = new FfmpegLocator(AppEnvironment.FfmpegDirectory, () => Settings.FfmpegPath);
        Runner = new FfmpegRunner(Locator);
        Probe = new MediaProbe(new FfprobeService(Runner));
        Conversion = new ConversionService(Runner, Probe);
        Player = new PlayerService();
        Player.WarmUp();
        AudioPreview = new AudioPreview(Player);
        Queue = new JobQueue(action => Dispatcher.UIThread.Post(action));
        Queue.Changed += OnQueueChanged;
        Queue.Drained += OnQueueDrained;
        if (OperatingSystem.IsWindows())
        {
            var job = TryCreateJobObject();
            Runner.ProcessStarted = process => job?.Assign(process);
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
        QueuePage = new QueueViewModel(this);
        Dependencies = new DependenciesViewModel(this);
        SettingsPage = new SettingsViewModel(this);
        About = new AboutViewModel(this);

        // ------------------------------------------------------------ Navigation
        _quickNav = new NavItem(QuickConvert);
        _compressNav = new NavItem(Compress);
        _queueNav = new NavItem(QueuePage);
        VideoGroup = new NavGroup(this, "video", "NavVideo", "IconVideo",
            [new NavItem(VideoConvert), new NavItem(VideoTrim), new NavItem(Animation), new NavItem(Remux), new NavItem(ExtractAudio)]);
        AudioGroup = new NavGroup(this, "audio", "NavAudio", "IconAudio",
            [new NavItem(AudioConvert), new NavItem(AudioTrim), new NavItem(Tags), new NavItem(GameAudio)]);
        ImageGroup = new NavGroup(this, "image", "NavImage", "IconImage",
            [new NavItem(ImageConvert), new NavItem(ImageResize), new NavItem(GameTextures), new NavItem(IconPage)]);
        // Video, Audio and Image always start expanded; collapsing only lasts for the session.
        foreach (var group in Groups)
        {
            group.IsExpanded = true;
        }

        TopNav = [_quickNav, _compressNav, new NavDivider(), VideoGroup, AudioGroup, ImageGroup, new NavDivider(), _queueNav];
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

        ApplyRuntimeSettings();
        Navigate(_quickNav);
    }

    // ================================================================ Services

    public AppSettings Settings { get; }

    public UiHost Host { get; }

    public FfmpegLocator Locator { get; }

    public FfmpegRunner Runner { get; }

    public MediaProbe Probe { get; }

    public ConversionService Conversion { get; }

    public PlayerService Player { get; }

    /// <summary>Mini player of the audio file lists.</summary>
    public AudioPreview AudioPreview { get; }

    public JobQueue Queue { get; }

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

    /// <summary>Set by the window: true while it is in the foreground.</summary>
    public Func<bool> IsWindowActive { get; set; } = () => true;

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
    public QueueViewModel QueuePage { get; }
    public DependenciesViewModel Dependencies { get; }
    public SettingsViewModel SettingsPage { get; }
    public AboutViewModel About { get; }

    private IEnumerable<PageViewModel> AllPages =>
    [
        QuickConvert, Compress, VideoConvert, VideoTrim, Animation, Remux, ExtractAudio, AudioConvert, AudioTrim, Tags, GameAudio,
        ImageConvert, ImageResize, GameTextures, IconPage, QueuePage, Dependencies, SettingsPage, About
    ];

    // ================================================================ Navigation

    private readonly NavItem _quickNav;
    private readonly NavItem _compressNav;
    private readonly NavItem _queueNav;

    public NavGroup VideoGroup { get; }
    public NavGroup AudioGroup { get; }
    public NavGroup ImageGroup { get; }

    private NavGroup[] Groups => [VideoGroup, AudioGroup, ImageGroup];

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

        // Leaving a page ends the mini player.
        if (!ReferenceEquals(CurrentPage, item.Page))
        {
            AudioPreview.Stop();
        }

        CurrentPage = item.Page;
        item.Page.OnActivated();
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

    /// <summary>Header pill: installed → dependencies page, missing → setup dialog.</summary>
    [RelayCommand]
    private void FfmpegPill()
    {
        if (Locator.IsAvailable)
        {
            NavigateTo(Dependencies);
        }
        else
        {
            OpenFfmpegSetup();
        }
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

    public string FfmpegPillText => Locator.IsAvailable
        ? "FFmpeg " + (FfmpegVersion.Length > 0 ? FfmpegVersion : "")
        : Loc.T("PillFfmpegMissing");

    public string FfmpegPillTip => Loc.T(Locator.IsAvailable ? "TipFfmpegInstalled" : "TipFfmpegMissing");

    [ObservableProperty] private bool _isTestingEncoders;
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

    /// <summary>Applies parallelism and priority to the queue and runner.</summary>
    public void ApplyRuntimeSettings()
    {
        Queue.MediaParallel = Math.Clamp(Settings.MediaParallel, 1, 4);
        Queue.ImageParallel = Math.Clamp(Settings.ImageParallel, 1, 16);
        Runner.Priority = Settings.Priority switch
        {
            PriorityOption.Normal => ProcessPriorityClass.Normal,
            PriorityOption.Idle => ProcessPriorityClass.Idle,
            _ => ProcessPriorityClass.BelowNormal
        };
        Queue.Pump();
        EncodersChanged?.Invoke();
        UpdatePowerRequest(QueueBusy);
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
        foreach (var (action, files) in batches)
        {
            RunExternal(action, files);
        }

        ShowWindowRequested?.Invoke();
    }

    private void RunExternal(string action, List<string> files)
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

        try
        {
            switch (action)
            {
                case ExplorerIntegration.Compress:
                    NavigateTo(Compress);
                    _ = Compress.Sources.AddPathsAsync(files);
                    return;
                case ExplorerIntegration.EditTags:
                    NavigateTo(Tags);
                    _ = Tags.Sources.AddPathsAsync(files);
                    return;
                case ExplorerIntegration.VideoMp4:
                {
                    var options = JsonFile.Clone(VideoConvert.Video.Options);
                    options.Container = VideoContainer.Mp4;
                    if (!FormatCatalog.CodecsFor(VideoContainer.Mp4).Contains(options.Codec))
                    {
                        options.Codec = VideoCodec.H264;
                    }

                    EnqueueExternal(Conversion.VideoConvert(Of(MediaKind.Video), options, VideoConvert.Output.Options, skipSame: false));
                    return;
                }
                case ExplorerIntegration.ExtractAudio:
                    _ = ExtractExternalAsync(Of(MediaKind.Video));
                    return;
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
                    return;
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
                    return;
                }
                default:
                    // "Open in CONVERTit": always Quick Convert, independent of the page that is open.
                    NavigateTo(QuickConvert);
                    if (!QuickConvert.AcceptDrop(files))
                    {
                        ShowToast(Loc.T("ToastUnsupportedDrop"), error: true);
                    }

                    return;
            }
        }
        catch (Exception ex)
        {
            ShowToast(ex.Message, error: true);
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

    private void EnqueueExternal(List<ConversionJob> jobs)
    {
        if (jobs.Count == 0)
        {
            ShowToast(Loc.T("ToastUnsupportedDrop"), error: true);
            return;
        }

        Queue.Enqueue(jobs);
        NavigateTo(QueuePage);
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
        if (Settings.SystemNotifications && !IsWindowActive())
        {
            ShowSystemNotification(Loc.T(summary.Failed > 0 ? "BatchDoneWithErrors" : "BatchDoneTitle"), text, summary.OutputFolder);
        }

        if (Settings.OpenFolderWhenDone && summary.Done > 0 && summary.OutputFolder is not null)
        {
            AppEnvironment.OpenFolder(summary.OutputFolder);
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
