using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CONVERTit.App.Localization;
using CONVERTit.App.Services;
using CONVERTit.Core.Catalog;
using CONVERTit.Core.Ffmpeg;
using CONVERTit.Core.Files;
using CONVERTit.Core.Media;
using CONVERTit.Core.Options;
using CONVERTit.Core.Storage;
using CONVERTit.Engines.Image;

namespace CONVERTit.App.ViewModels;

/// <summary>A source file in a page's list.</summary>
public sealed partial class SourceItem : ObservableObject
{
    public SourceItem(string path, string? root)
    {
        Path = path;
        Root = root;
        Kind = MediaTypes.Detect(path);
        try
        {
            Size = new FileInfo(path).Length;
        }
        catch
        {
            Size = 0;
        }
    }

    public string Path { get; }

    /// <summary>Root folder when the file came from a folder batch.</summary>
    public string? Root { get; }

    public string Name => System.IO.Path.GetFileName(Path);

    public string Folder => Root is not null
        ? System.IO.Path.GetRelativePath(Root, System.IO.Path.GetDirectoryName(Path) ?? Root) is var rel && rel != "." ? rel : System.IO.Path.GetFileName(Root)
        : System.IO.Path.GetDirectoryName(Path) ?? "";

    public MediaKind Kind { get; }

    public long Size { get; }

    public string Extension => MediaTypes.Extension(Path).ToUpperInvariant();

    [ObservableProperty] private MediaInfo? _info;
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private bool _isProbing = true;
    [ObservableProperty] private string _error = "";

    public bool HasError => Error.Length > 0;

    public double Duration => Info?.DurationSeconds ?? 0;

    partial void OnErrorChanged(string value) => OnPropertyChanged(nameof(HasError));

    partial void OnInfoChanged(MediaInfo? value) => OnPropertyChanged(nameof(Duration));

    public SourceRef ToRef() => new(Path, Root);

    // ---------------------------------------------------------------- Mini player (audio lists)

    private bool _fromPlayer;

    /// <summary>This row is loaded in the mini player (shows the progress bar).</summary>
    [ObservableProperty] private bool _isPlayerActive;

    [ObservableProperty] private bool _isPlaying;

    /// <summary>Seconds; set by the player, and by the user when dragging the bar (seek).</summary>
    [ObservableProperty] private double _playPosition;

    [ObservableProperty] private double _playDuration;

    internal Services.AudioPreview? Preview { get; set; }

    public string PlayTimeText => $"{Timecode.Format(PlayPosition, false)} / {Timecode.Format(PlayDuration, false)}";

    internal void SetPlayPosition(double position, double duration)
    {
        _fromPlayer = true;
        PlayDuration = duration;
        PlayPosition = position;
        _fromPlayer = false;
    }

    partial void OnPlayPositionChanged(double value)
    {
        OnPropertyChanged(nameof(PlayTimeText));
        if (!_fromPlayer)
        {
            Preview?.Seek(this, value);
        }
    }

    partial void OnPlayDurationChanged(double value) => OnPropertyChanged(nameof(PlayTimeText));
}

/// <summary>One line of the info panel.</summary>
public sealed record InfoRow(string Label, string Value);

/// <summary>
/// Source list of a page: add files or folders (drag &amp; drop, pickers), folder batch options, probing,
/// and the info panel of the selected file.
/// </summary>
public sealed partial class SourceListViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly IReadOnlySet<MediaKind> _kinds;
    private readonly MediaKind _folderKind;
    private CancellationTokenSource? _previewCts;
    private readonly List<SourceItem> _probing = [];
    private BusyActivity? _probeActivity;

    public SourceListViewModel(MainViewModel main, IReadOnlySet<MediaKind> kinds, MediaKind folderKind, bool singleFile = false)
    {
        _main = main;
        _kinds = kinds;
        _folderKind = folderKind;
        SingleFile = singleFile;
        Items.CollectionChanged += (_, _) => RaiseChanged();
    }

    public ObservableCollection<SourceItem> Items { get; } = [];

    public SourceOptions Folder { get; } = new();

    public bool SingleFile { get; }

    /// <summary>Read the files with ffprobe/ImageMagick (off for the tag editor, which reads tags itself).</summary>
    public bool ProbeMedia { get; init; } = true;

    /// <summary>Extra filter for files (e.g. only formats the tag editor can write).</summary>
    public Func<string, bool>? Accept { get; init; }

    /// <summary>Audio lists: key under which the mini player switch is remembered (null = no mini player).</summary>
    public string? MiniPlayerKey
    {
        get => _miniPlayerKey;
        init
        {
            _miniPlayerKey = value;
            _showMiniPlayer = value is not null && _main.Settings.MiniPlayerOn.Contains(value);
        }
    }

    private readonly string? _miniPlayerKey;

    public bool HasMiniPlayer => MiniPlayerKey is not null;

    /// <summary>Play/pause and folder buttons per row, progress bar for the playing row.</summary>
    [ObservableProperty] private bool _showMiniPlayer;

    partial void OnShowMiniPlayerChanged(bool value)
    {
        if (MiniPlayerKey is null)
        {
            return;
        }

        _main.Settings.MiniPlayerOn.Remove(MiniPlayerKey);
        if (value)
        {
            _main.Settings.MiniPlayerOn.Add(MiniPlayerKey);
        }
        else
        {
            _main.AudioPreview.StopIf(Items.Contains);
        }

        _main.Settings.Save();
    }

    [RelayCommand]
    private void ToggleMiniPlayer() => ShowMiniPlayer = !ShowMiniPlayer;

    [RelayCommand]
    private void TogglePlay(SourceItem? item)
    {
        if (item is not null)
        {
            _main.AudioPreview.Toggle(item);
        }
    }

    public bool MultiFile => !SingleFile;

    /// <summary>Output folders to leave out when scanning (the batch's own results).</summary>
    public Func<string, IEnumerable<string>>? ExcludedFolders { get; set; }

    /// <summary>Raised when files were added/removed or probed (estimates, summaries).</summary>
    public event Action? Changed;

    /// <summary>Raised when the selected file changes (trim player).</summary>
    public event Action<SourceItem?>? SelectionChanged;

    [ObservableProperty] private SourceItem? _selectedItem;
    [ObservableProperty] private Bitmap? _preview;
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private bool _showFolderOptions;

    public IReadOnlyList<InfoRow> InfoRows { get; private set; } = [];

    public bool HasItems => Items.Count > 0;

    public bool IsEmpty => Items.Count == 0;

    public bool HasSelection => SelectedItem is not null;

    public string CountText => Loc.F("SourceCount", Items.Count, Units.Bytes(Items.Sum(i => i.Size)));

    public double TotalDuration => Items.Sum(i => i.Duration);

    public IEnumerable<SourceRef> Refs => ActiveItems.Select(i => i.ToRef());

    // ---------------------------------------------------------------- Multi-selection (Ctrl / Shift)

    private IReadOnlyList<SourceItem> _selection = [];

    /// <summary>All selected rows (the list selects with Ctrl and Shift).</summary>
    public IReadOnlyList<SourceItem> Selection => _selection;

    public int SelectedCount => _selection.Count;

    /// <summary>Some, but not all files are selected: "Add selected" appears next to "Add to queue".</summary>
    public bool HasPartialSelection => MultiFile && _selection.Count > 0 && _selection.Count < Items.Count;

    public string AddSelectedText => Loc.F("BtnAddSelected", _selection.Count);

    public string SelectionText => Loc.F("SelectionCount", _selection.Count);

    /// <summary>While set, <see cref="Refs"/> and <see cref="ActiveItems"/> only cover the selection.</summary>
    public bool RestrictToSelection { get; set; }

    /// <summary>The files a job plan uses: all, or the selection while <see cref="RestrictToSelection"/> is set.</summary>
    public IEnumerable<SourceItem> ActiveItems => RestrictToSelection ? _selection.Where(Items.Contains) : Items;

    /// <summary>The list asks the view to select all rows (button, Ctrl+A works natively).</summary>
    public event Action? SelectAllRequested;

    /// <summary>Called by the view whenever the selected rows change.</summary>
    public void SetSelection(IEnumerable<SourceItem> items)
    {
        _selection = items.ToList();
        RaiseSelection();
    }

    private void RaiseSelection()
    {
        OnPropertyChanged(nameof(Selection));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(HasPartialSelection));
        OnPropertyChanged(nameof(AddSelectedText));
        OnPropertyChanged(nameof(SelectionText));
        SelectionSetChanged?.Invoke();
    }

    /// <summary>Raised when the multi-selection changes (enables "Add selected").</summary>
    public event Action? SelectionSetChanged;

    [RelayCommand]
    private void SelectAll() => SelectAllRequested?.Invoke();

    /// <summary>Removes the given files (e.g. after "Add selected" queued them).</summary>
    public void RemoveItems(IEnumerable<SourceItem> items)
    {
        var gone = items.ToList();
        _main.AudioPreview.StopIf(gone.Contains);
        items = gone;
        foreach (var item in items.ToList())
        {
            Items.Remove(item);
        }

        if (SelectedItem is not null && !Items.Contains(SelectedItem))
        {
            SelectedItem = Items.FirstOrDefault();
        }
    }

    partial void OnSelectedItemChanged(SourceItem? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        UpdateInfo();
        _ = LoadPreviewAsync(value);
        SelectionChanged?.Invoke(value);
    }

    [RelayCommand]
    private async Task AddFilesAsync()
    {
        var extensions = _kinds.SelectMany(MediaTypes.ExtensionsOf).Distinct().ToList();
        var files = await _main.Host.PickFilesAsync(Loc.T(SingleFile ? "PickFile" : "PickFiles"), extensions, !SingleFile, _main.Settings.LastFolder);
        if (files.Count > 0)
        {
            _main.Settings.LastFolder = System.IO.Path.GetDirectoryName(files[0]) ?? "";
            await AddPathsAsync(files);
        }
    }

    [RelayCommand]
    private async Task AddFolderAsync()
    {
        var folder = await _main.Host.PickFolderAsync(Loc.T("PickFolder"), _main.Settings.LastFolder);
        if (!string.IsNullOrEmpty(folder))
        {
            _main.Settings.LastFolder = folder;
            await AddPathsAsync([folder]);
        }
    }

    [RelayCommand]
    private void RemoveSelected()
    {
        var remove = new List<SourceItem>(_selection);
        if (remove.Count == 0 && SelectedItem is { } one)
        {
            remove.Add(one);
        }

        if (remove.Count == 0)
        {
            return;
        }

        var index = Items.IndexOf(remove[0]);
        _main.AudioPreview.StopIf(remove.Contains);
        foreach (var item in remove)
        {
            Items.Remove(item);
        }

        SelectedItem = Items.Count > 0 ? Items[Math.Clamp(index, 0, Items.Count - 1)] : null;
    }

    [RelayCommand]
    private void Remove(SourceItem? item)
    {
        if (item is not null)
        {
            _main.AudioPreview.StopIf(i => ReferenceEquals(i, item));
            Items.Remove(item);
            if (ReferenceEquals(SelectedItem, item))
            {
                SelectedItem = Items.FirstOrDefault();
            }
        }
    }

    [RelayCommand]
    private void Clear()
    {
        _main.AudioPreview.StopIf(Items.Contains);
        Items.Clear();
        SelectedItem = null;
    }

    [RelayCommand]
    private void ToggleFolderOptions() => ShowFolderOptions = !ShowFolderOptions;

    [RelayCommand]
    private void ShowInExplorer(SourceItem? item) => AppEnvironment.ShowInExplorer((item ?? SelectedItem)?.Path);

    /// <summary>Adds files and folders (folders are scanned with the folder options). Returns the number added.</summary>
    public async Task<int> AddPathsAsync(IEnumerable<string> paths)
    {
        // Show the header indicator at once and let it render before any work starts (large files, slow drives).
        var list = paths.ToList();
        using var adding = _main.BeginActivity(list.Count == 1 ? Loc.F("ActivityLoadingFile", System.IO.Path.GetFileName(list[0].TrimEnd('\\', '/'))) : Loc.F("ActivityLoadingFiles", list.Count));
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        paths = list;

        var added = new List<SourceItem>();
        var existing = new HashSet<string>(Items.Select(i => i.Path), StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                if (SingleFile)
                {
                    continue;
                }

                IsScanning = true;
                using var scanActivity = _main.BeginActivity(Loc.F("ActivityScanning", System.IO.Path.GetFileName(path.TrimEnd('\\', '/'))));
                try
                {
                    var root = System.IO.Path.GetFullPath(path);
                    var excluded = ExcludedFolders?.Invoke(root) ?? [];
                    var options = JsonFile.Clone(Folder);
                    var files = await Task.Run(() => FileScanner.Scan(root, _folderKind, options, excluded));
                    foreach (var file in files.Where(f => _kinds.Contains(MediaTypes.Detect(f)) && (Accept?.Invoke(f) ?? true) && existing.Add(f)))
                    {
                        added.Add(new SourceItem(file, root));
                    }
                }
                finally
                {
                    IsScanning = false;
                }
            }
            else if (File.Exists(path) && _kinds.Contains(MediaTypes.Detect(path)) && (Accept?.Invoke(path) ?? true) && !FileScanner.IsTempFile(path))
            {
                var full = System.IO.Path.GetFullPath(path);
                if (existing.Add(full))
                {
                    added.Add(new SourceItem(full, null));
                }
            }
        }

        if (added.Count == 0)
        {
            return 0;
        }

        if (SingleFile)
        {
            Items.Clear();
            added = [added[0]];
        }

        foreach (var item in added)
        {
            Items.Add(item);
        }

        SelectedItem ??= added[0];
        if (SingleFile)
        {
            SelectedItem = added[0];
        }

        foreach (var item in added)
        {
            if (ProbeMedia)
            {
                _ = ProbeAsync(item);
            }
            else
            {
                item.IsProbing = false;
            }
        }

        RaiseChanged();

        return added.Count;
    }

    /// <summary>Accepts dropped paths of the page's kinds.</summary>
    public bool CanAccept(IEnumerable<string> paths) =>
        paths.Any(p => Directory.Exists(p) ? !SingleFile : _kinds.Contains(MediaTypes.Detect(p)) && (Accept?.Invoke(p) ?? true));

    /// <summary>Header activity while files are read (large videos take a few seconds).</summary>
    private void UpdateProbeActivity()
    {
        if (_probing.Count == 0)
        {
            _probeActivity?.Dispose();
            _probeActivity = null;
            return;
        }

        var text = _probing.Count == 1 ? Loc.F("ActivityLoadingFile", _probing[0].Name) : Loc.F("ActivityLoadingFiles", _probing.Count);
        if (_probeActivity is null)
        {
            _probeActivity = _main.BeginActivity(text);
        }
        else
        {
            _probeActivity.Text = text;
        }
    }

    private async Task ProbeAsync(SourceItem item)
    {
        _probing.Add(item);
        UpdateProbeActivity();
        try
        {
            if (item.Kind != MediaKind.Image && !_main.Locator.IsAvailable)
            {
                item.Summary = Loc.T("NeedsFfmpeg");
                return;
            }

            var info = await _main.Probe.GetAsync(item.Path);
            item.Info = info;
            item.Summary = Summarize(info);
        }
        catch (Exception ex)
        {
            item.Error = ex is ConversionException ce ? Loc.Error(ce.Key) : ex.Message;
            item.Summary = Loc.T("ProbeFailed");
        }
        finally
        {
            _probing.Remove(item);
            UpdateProbeActivity();
            item.IsProbing = false;
            if (ReferenceEquals(item, SelectedItem))
            {
                UpdateInfo();
                _ = LoadPreviewAsync(item);
                SelectionChanged?.Invoke(item);
            }

            RaiseChanged();
        }
    }

    /// <summary>Short line under the file name: "1920×1080 · 00:02:00 · H264" / "320 kbps · 03:12" / "4000×3000 PNG".</summary>
    public static string Summarize(MediaInfo info)
    {
        var parts = new List<string>();
        if (info.Image is { } img)
        {
            parts.Add($"{img.Width}×{img.Height}");
            if (img.HasAlpha)
            {
                parts.Add(Loc.T("Alpha"));
            }

            if (img.Frames > 1)
            {
                parts.Add(Loc.F("Frames", img.Frames));
            }
        }
        else
        {
            if (info.MainVideo is { } v)
            {
                var (w, h) = v.DisplaySize;
                parts.Add($"{w}×{h}");
                if (v.Fps > 0)
                {
                    parts.Add(v.Fps.ToString("0.##", CultureInfo.InvariantCulture) + " fps");
                }

                parts.Add(v.Codec.ToUpperInvariant());
            }
            else if (info.MainAudio is { } a)
            {
                parts.Add(a.Codec.ToUpperInvariant());
                var bits = a.Bitrate > 0 ? a.Bitrate : info.Bitrate;
                if (bits > 0)
                {
                    parts.Add($"{bits / 1000} kbps");
                }
            }

            if (info.DurationSeconds > 0)
            {
                parts.Add(Timecode.Format(info.DurationSeconds, false));
            }
        }

        return string.Join(" · ", parts);
    }

    private void UpdateInfo()
    {
        var item = SelectedItem;
        var rows = new List<InfoRow>();
        if (item is not null)
        {
            rows.Add(new InfoRow(Loc.T("InfoFile"), item.Name));
            rows.Add(new InfoRow(Loc.T("InfoSize"), Units.Bytes(item.Size)));
            if (item.Info is { } info)
            {
                if (info.Format.Length > 0)
                {
                    rows.Add(new InfoRow(Loc.T("InfoFormat"), info.Format));
                }

                if (info.DurationSeconds > 0)
                {
                    rows.Add(new InfoRow(Loc.T("InfoDuration"), Timecode.Format(info.DurationSeconds)));
                }

                if (info.Bitrate > 0)
                {
                    rows.Add(new InfoRow(Loc.T("InfoBitrate"), Units.Bitrate(info.Bitrate)));
                }

                if (info.MainVideo is { } v)
                {
                    var (w, h) = v.DisplaySize;
                    rows.Add(new InfoRow(Loc.T("InfoVideo"), $"{v.Codec.ToUpperInvariant()} · {w}×{h}" + (v.Rotation != 0 ? $" · {v.Rotation}°" : "")));
                    rows.Add(new InfoRow(Loc.T("InfoFrameRate"), v.Fps > 0 ? v.Fps.ToString("0.###", CultureInfo.InvariantCulture) + " fps" : "–"));
                    rows.Add(new InfoRow(Loc.T("InfoPixelFormat"), v.PixelFormat.Length > 0 ? v.PixelFormat : "–"));
                    if (v.Bitrate > 0)
                    {
                        rows.Add(new InfoRow(Loc.T("InfoVideoBitrate"), Units.Bitrate(v.Bitrate)));
                    }
                }

                var n = 1;
                foreach (var a in info.Audio)
                {
                    var label = info.Audio.Count > 1 ? Loc.F("InfoAudioN", n++) : Loc.T("InfoAudio");
                    var details = new List<string> { a.Codec.ToUpperInvariant() };
                    if (a.SampleRate > 0) details.Add($"{a.SampleRate / 1000d:0.#} kHz");
                    if (a.Channels > 0) details.Add(a.ChannelLayout.Length > 0 ? a.ChannelLayout : $"{a.Channels} ch");
                    if (a.Bitrate > 0) details.Add($"{a.Bitrate / 1000} kbps");
                    if (a.Language.Length > 0) details.Add(a.Language);
                    rows.Add(new InfoRow(label, string.Join(" · ", details)));
                }

                if (info.Subtitles.Count > 0)
                {
                    rows.Add(new InfoRow(Loc.T("InfoSubtitles"), string.Join(", ", info.Subtitles.Select(s => s.Codec + (s.Language.Length > 0 ? $" ({s.Language})" : "")))));
                }

                if (info.CoverArt is not null)
                {
                    rows.Add(new InfoRow(Loc.T("InfoCover"), Loc.T("Yes")));
                }

                if (info.Image is { } img)
                {
                    rows.Add(new InfoRow(Loc.T("InfoDimensions"), $"{img.Width} × {img.Height} px"));
                    rows.Add(new InfoRow(Loc.T("InfoAlpha"), Loc.T(img.HasAlpha ? "Yes" : "No")));
                    rows.Add(new InfoRow(Loc.T("InfoColor"), $"{img.ColorSpace} · {img.BitDepth} bit"));
                    if (img.Frames > 1)
                    {
                        rows.Add(new InfoRow(Loc.T("InfoFrames"), img.Frames.ToString(CultureInfo.InvariantCulture)));
                    }

                    if (img.DpiX > 0)
                    {
                        rows.Add(new InfoRow("DPI", img.DpiX.ToString("0", CultureInfo.InvariantCulture)));
                    }
                }
                else
                {
                    rows.Add(new InfoRow(Loc.T("InfoAlpha"), Loc.T(info.HasAlpha ? "Yes" : "No")));
                }
            }
            else if (item.HasError)
            {
                rows.Add(new InfoRow(Loc.T("InfoError"), item.Error));
            }
        }

        InfoRows = rows;
        OnPropertyChanged(nameof(InfoRows));
    }

    private async Task LoadPreviewAsync(SourceItem? item)
    {
        _previewCts?.Cancel();
        var cts = _previewCts = new CancellationTokenSource();
        if (item is null)
        {
            Preview = null;
            return;
        }

        try
        {
            byte[]? bytes = null;
            if (item.Kind == MediaKind.Image)
            {
                bytes = await Task.Run(() => ImageEngine.Preview(item.Path, 480), cts.Token);
            }
            else if (item.Kind == MediaKind.Video && _main.Locator.IsAvailable)
            {
                var at = item.Duration > 0 ? Math.Min(item.Duration * 0.1, 30) : 1;
                bytes = await _main.Runner.CaptureBytesAsync(FfmpegCommands.FrameGrab(item.Path, at, 270), TimeSpan.FromSeconds(15), cts.Token);
            }

            if (cts.IsCancellationRequested)
            {
                return;
            }

            Preview = bytes is { Length: > 0 } ? new Bitmap(new MemoryStream(bytes)) : null;
        }
        catch
        {
            if (!cts.IsCancellationRequested)
            {
                Preview = null;
            }
        }
    }

    public void RefreshTexts()
    {
        foreach (var item in Items.Where(i => i.Info is not null))
        {
            item.Summary = Summarize(item.Info!);
        }

        UpdateInfo();
        OnPropertyChanged(nameof(CountText));
    }

    private void RaiseChanged()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(RaiseChanged);
            return;
        }

        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(TotalDuration));
        OnPropertyChanged(nameof(HasPartialSelection));
        Changed?.Invoke();
    }
}
