using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CONVERTit.App.Localization;
using CONVERTit.App.Services;
using CONVERTit.Core.Storage;
using CONVERTit.Engines.Image;

namespace CONVERTit.App.ViewModels;

// ==================================================================== Tools › Thumbnail cache viewer

/// <summary>Entry of the cache picker: one file, or (Path null) all caches of the current user.</summary>
public sealed record CacheChoice(string Label, string? Path)
{
    public override string ToString() => Label;
}

/// <summary>One thumbnail of a cache file. The small preview is decoded only when its page is shown.</summary>
public sealed partial class CacheThumb(ThumbnailData data) : ObservableObject
{
    public ThumbnailData Data { get; } = data;

    [ObservableProperty] private Bitmap? _image;
    [ObservableProperty] private bool _isMarked;

    /// <summary>Other sizes of the same picture hidden by "hide duplicates".</summary>
    public int Variants { get; set; }

    /// <summary>File name (Thumbs.db) or the cache id (thumbcache stores no names).</summary>
    public string Label => Data.Entry.Name ?? Data.Entry.Id;

    public long Pixels => (long)Data.Entry.Width * Data.Entry.Height;

    public string SourceName => Path.GetFileName(Data.Entry.SourceFile);

    public string Details => Data.Entry.Width > 0
        ? $"{Data.Entry.Width} × {Data.Entry.Height} · {Data.Entry.Format.ToUpperInvariant()} · {Units.Bytes(Data.Entry.Length)}"
        : $"{Data.Entry.Format.ToUpperInvariant()} · {Units.Bytes(Data.Entry.Length)}";

    public bool IsDecoded { get; set; }
}

/// <summary>
/// Read-only viewer for thumbcache_*.db (Windows Vista–11) and Thumbs.db (Windows XP, network shares): grid with
/// Explorer-like selection, marks for this session, a mini image viewer, right-click actions and export. The only
/// writing action is "Clean cache …", which resets the Explorer caches after an opt-in dialog.
/// </summary>
public sealed partial class ThumbcacheViewModel : PageViewModel
{
    /// <summary>Tiles shown at once; "Show more" adds the next page (keeps the grid responsive with large caches).</summary>
    public const int PageSize = 300;

    private const int TileDecodeWidth = 160;

    private readonly List<CacheThumb> _all = [];
    private List<CacheThumb> _filtered = [];
    private List<CacheThumb> _selection = [];
    private CancellationTokenSource? _cts;
    private bool _loadedOnce;
    private bool _quietChoice;
    private int _shown;

    public ThumbcacheViewModel(MainViewModel main) : base(main)
    {
    }

    public override string TitleKey => "NavThumbcache";

    public override string IconKey => "IconViewGrid";

    /// <summary>Tiles currently in the grid (first pages of the filtered list).</summary>
    public ObservableCollection<CacheThumb> Visible { get; } = [];

    /// <summary>The view selects or clears all tiles (true = select all).</summary>
    public event Action<bool>? SelectAllRequested;

    [ObservableProperty] private string _sourcePath = "";
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _isCleaning;

    [ObservableProperty] private CacheChoice? _selectedChoice;

    /// <summary>All caches: only the largest version of every picture (same cache id in the 96/256/1024/2560 files).</summary>
    [ObservableProperty] private bool _hideDuplicates = true;

    [ObservableProperty] private bool _onlyMarked;

    /// <summary>Explorer's cache of the current user.</summary>
    public static string OwnCacheFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Explorer");

    /// <summary>Cache files of the current user, largest first.</summary>
    public static IReadOnlyList<string> OwnCacheFiles()
    {
        try
        {
            return Directory.Exists(OwnCacheFolder)
                ? Directory.GetFiles(OwnCacheFolder, "thumbcache_*.db")
                    .Where(f => new FileInfo(f).Length > 64)
                    .OrderByDescending(f => new FileInfo(f).Length)
                    .ToList()
                : [];
        }
        catch
        {
            return [];
        }
    }

    /// <summary>"All caches of this user" first, then every file with its size.</summary>
    public IReadOnlyList<CacheChoice> CacheChoices
    {
        get
        {
            var list = new List<CacheChoice> { new(Loc.T("ThumbcacheAllCaches"), null) };
            foreach (var file in OwnCacheFiles())
            {
                long size = 0;
                try
                {
                    size = new FileInfo(file).Length;
                }
                catch
                {
                    // Size unknown.
                }

                list.Add(new CacheChoice($"{Path.GetFileName(file)} · {Units.Bytes(size)}", file));
            }

            return list;
        }
    }

    public bool HasItems => _filtered.Count > 0;

    public bool IsEmpty => _filtered.Count == 0 && !IsLoading;

    public int SelectedCount => _selection.Count;

    public bool HasSelection => _selection.Count > 0;

    /// <summary>Every shown tile is selected (the select-all icon turns to the accent color).</summary>
    public bool AllSelected => Visible.Count > 0 && _selection.Count >= Visible.Count;

    public int MarkedCount => _all.Count(i => i.IsMarked);

    public bool HasMarks => MarkedCount > 0;

    public bool HasMore => _shown < _filtered.Count;

    public string MoreText => Loc.F("ThumbcacheShowMore", Math.Min(PageSize, _filtered.Count - _shown), _filtered.Count - _shown);

    public bool IsAllCaches => SelectedChoice?.Path is null && SourcePath.Length == 0;

    public string CountText
    {
        get
        {
            var text = Loc.F("ThumbcacheCount", _filtered.Count);
            if (_selection.Count > 0)
            {
                text += " · " + Loc.F("ThumbcacheSelected", _selection.Count);
            }

            if (MarkedCount > 0)
            {
                text += " · " + Loc.F("ThumbcacheMarked", MarkedCount);
            }

            return text;
        }
    }

    public override void OnActivated()
    {
        base.OnActivated();
        OnPropertyChanged(nameof(CacheChoices));
        if (!_loadedOnce)
        {
            _loadedOnce = true;
            SelectedChoice = CacheChoices[0];
        }
    }

    partial void OnSelectedChoiceChanged(CacheChoice? value)
    {
        if (value is not null && !_quietChoice)
        {
            _ = LoadAsync(value.Path);
        }
    }

    partial void OnHideDuplicatesChanged(bool value) => ApplyFilter();

    partial void OnOnlyMarkedChanged(bool value) => ApplyFilter();

    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(IsEmpty));

    public override bool AcceptDrop(IReadOnlyList<string> paths)
    {
        var file = paths.FirstOrDefault(IsCacheFile);
        if (file is null)
        {
            return false;
        }

        // A file from Explorer or drag & drop wins over the automatic "all caches" of the first visit.
        _loadedOnce = true;
        _ = LoadAsync(file);
        return true;
    }

    public static bool IsCacheFile(string path)
    {
        var name = Path.GetFileName(path);
        return File.Exists(path) && (name.Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase) || name.Equals("ehthumbs.db", StringComparison.OrdinalIgnoreCase)
            || (name.StartsWith("thumbcache_", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".db", StringComparison.OrdinalIgnoreCase)));
    }

    [RelayCommand]
    private async Task PickFileAsync()
    {
        var files = await Host.PickFilesAsync(Loc.T("PickThumbcache"), ["db"], multiple: false, startFolder: OwnCacheFolder);
        if (files.Count > 0)
        {
            await LoadAsync(files[0]);
        }
    }

    [RelayCommand]
    private void OpenCacheFolder() => AppEnvironment.OpenFolder(OwnCacheFolder);

    /// <summary>Loads one file, or all caches of this user (<paramref name="path"/> null).</summary>
    public async Task LoadAsync(string? path)
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        SourcePath = path ?? "";
        if (path is not null && !ReferenceEquals(SelectedChoice?.Path, path))
        {
            // Show the file in the picker without loading it again.
            var match = CacheChoices.FirstOrDefault(c => string.Equals(c.Path, path, StringComparison.OrdinalIgnoreCase));
            if (match is not null && match != SelectedChoice)
            {
                _quietChoice = true;
                SelectedChoice = match;
                _quietChoice = false;
            }
        }

        CloseViewer();
        _all.Clear();
        _filtered = [];
        Visible.Clear();
        _shown = 0;
        _selection = [];
        IsLoading = true;
        StatusText = "";
        RaiseItems();
        try
        {
            IReadOnlyList<string> files = path is null ? OwnCacheFiles() : [path];
            var list = await Task.Run(() =>
            {
                var result = new List<CacheThumb>();
                foreach (var file in files)
                {
                    cts.Token.ThrowIfCancellationRequested();
                    try
                    {
                        result.AddRange(ThumbsDbReader.Read(file, cts.Token).Select(d => new CacheThumb(d)));
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException && files.Count > 1)
                    {
                        // One unreadable cache file must not hide the others.
                    }
                }

                return result;
            }, cts.Token);
            if (cts.IsCancellationRequested)
            {
                return;
            }

            _all.AddRange(list);
            if (list.Count == 0)
            {
                StatusText = Loc.T(files.Count == 0 ? "ThumbcacheNoOwnCache" : "ThumbcacheEmpty");
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            StatusText = Loc.F("ThumbcacheError", ex.Message);
        }
        finally
        {
            if (ReferenceEquals(_cts, cts))
            {
                IsLoading = false;
            }
        }

        ApplyFilter();
    }

    /// <summary>Duplicates (same cache id, several size files) → largest version; optionally only marked tiles.</summary>
    private void ApplyFilter()
    {
        IEnumerable<CacheThumb> items = _all;
        foreach (var item in _all)
        {
            item.Variants = 0;
        }

        var thumbcache = _all.Any(i => i.SourceName.StartsWith("thumbcache_", StringComparison.OrdinalIgnoreCase));
        if (HideDuplicates && thumbcache)
        {
            items = _all.GroupBy(i => i.Data.Entry.Id, StringComparer.OrdinalIgnoreCase)
                .Select(g =>
                {
                    var best = g.OrderByDescending(i => i.Pixels).ThenByDescending(i => i.Data.Entry.Length).First();
                    best.Variants = g.Count() - 1;
                    return best;
                });
        }

        if (OnlyMarked)
        {
            items = items.Where(i => i.IsMarked);
        }

        _filtered = items.ToList();
        Visible.Clear();
        _shown = 0;
        _selection = [];
        ShowMore();
    }

    [RelayCommand]
    private void ShowMore()
    {
        var next = _filtered.Skip(_shown).Take(PageSize).ToList();
        foreach (var item in next)
        {
            Visible.Add(item);
        }

        _shown += next.Count;
        RaiseItems();
        _ = DecodeAsync(next.Where(i => !i.IsDecoded).ToList());
    }

    /// <summary>Small previews for the tiles of one page (in the background, BMP via ImageMagick).</summary>
    private static async Task DecodeAsync(List<CacheThumb> items)
    {
        foreach (var chunk in items.Chunk(24))
        {
            var decoded = await Task.Run(() => chunk.Select(i => (i, Decode(i.Data.Data, TileDecodeWidth))).ToList());
            foreach (var (item, bitmap) in decoded)
            {
                item.IsDecoded = true;
                item.Image = bitmap;
            }
        }
    }

    private static Bitmap? Decode(byte[] data, int width)
    {
        try
        {
            var bytes = IsBmp(data) ? ImageEngine.ThumbnailFromBytes(data, width) : data;
            using var stream = new MemoryStream(bytes);
            return Bitmap.DecodeToWidth(stream, width);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Full-size bitmap for the viewer (BMP converted to PNG first).</summary>
    private static Bitmap? DecodeFull(byte[] data)
    {
        try
        {
            using var stream = new MemoryStream(IsBmp(data) ? ImageBytes.ToPng(data) : data);
            return new Bitmap(stream);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsBmp(byte[] data) => data.Length > 1 && data[0] == 0x42 && data[1] == 0x4D;

    // ------------------------------------------------------------ Selection & marks

    /// <summary>Called by the view when the grid selection changes (Ctrl/Shift click, Ctrl+A, arrows).</summary>
    public void SetSelection(IEnumerable<CacheThumb> items)
    {
        _selection = items.ToList();
        RaiseSelection();
    }

    public IReadOnlyList<CacheThumb> Selection => _selection;

    [RelayCommand]
    private void ToggleSelectAll() => SelectAllRequested?.Invoke(!AllSelected);

    /// <summary>Marks the selection (or unmarks it when all of it is marked already).</summary>
    [RelayCommand]
    private void ToggleMark(CacheThumb? item)
    {
        List<CacheThumb> items = item is not null && !_selection.Contains(item) ? [item] : _selection.ToList();
        if (items.Count == 0)
        {
            return;
        }

        var mark = !items.All(i => i.IsMarked);
        foreach (var i in items)
        {
            i.IsMarked = mark;
        }

        if (OnlyMarked && !mark)
        {
            ApplyFilter();
        }

        RaiseSelection();
    }

    [RelayCommand]
    private void ClearMarks()
    {
        foreach (var item in _all)
        {
            item.IsMarked = false;
        }

        if (OnlyMarked)
        {
            OnlyMarked = false;
        }

        RaiseSelection();
    }

    // ------------------------------------------------------------ Export, copy, send

    [RelayCommand]
    private Task ExportAllAsync() => ExportAsync(_filtered);

    [RelayCommand]
    private Task ExportSelectedAsync() => ExportAsync(_selection);

    [RelayCommand]
    private Task ExportMarkedAsync() => ExportAsync(_all.Where(i => i.IsMarked).ToList());

    [RelayCommand]
    private Task ExportItemAsync(CacheThumb? item) => item is null ? Task.CompletedTask : ExportAsync([item]);

    private async Task ExportAsync(IReadOnlyList<CacheThumb> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        var folder = await Host.PickFolderAsync(Loc.T("PickExportFolder"));
        if (folder is null)
        {
            return;
        }

        var name = SourcePath.Length > 0 ? Path.GetFileNameWithoutExtension(SourcePath) : "thumbcache";
        var target = Path.Combine(folder, name + "_export");
        var written = await Task.Run(() => WriteFiles(items, target));
        Main.ShowToast(Loc.F("ToastThumbsExported", written.Count));
        AppEnvironment.OpenFolder(target);
    }

    private static List<string> WriteFiles(IReadOnlyList<CacheThumb> items, string target)
    {
        Directory.CreateDirectory(target);
        var paths = new List<string>();
        foreach (var item in items)
        {
            var stem = string.Join("_", Path.GetFileNameWithoutExtension(item.Label).Split(Path.GetInvalidFileNameChars()));
            var path = Path.Combine(target, $"{stem}.{item.Data.Entry.Format}");
            for (var i = 2; File.Exists(path); i++)
            {
                path = Path.Combine(target, $"{stem} ({i}).{item.Data.Entry.Format}");
            }

            File.WriteAllBytes(path, item.Data.Data);
            paths.Add(path);
        }

        return paths;
    }

    [RelayCommand]
    private async Task CopyImageAsync(CacheThumb? item)
    {
        item ??= ViewerItem ?? _selection.FirstOrDefault();
        if (item is null || !OperatingSystem.IsWindows())
        {
            return;
        }

        var ok = await Task.Run(() =>
        {
            try
            {
                var (bmp, png) = ImageBytes.ForClipboard(item.Data.Data);
                return Platform.Windows.ClipboardImage.Set(bmp, png);
            }
            catch
            {
                return false;
            }
        });
        Main.ShowToast(Loc.T(ok ? "ToastImageCopied" : "ToastImageCopyFailed"), error: !ok);
    }

    [RelayCommand]
    private async Task CopyIdAsync(CacheThumb? item)
    {
        List<CacheThumb> items = item is not null && !_selection.Contains(item) ? [item] : _selection.ToList();
        if (items.Count > 0)
        {
            await Host.CopyTextAsync(string.Join(Environment.NewLine, items.Select(i => i.Label)));
            Main.ShowToast(Loc.T("ToastCopied"));
        }
    }

    /// <summary>Exports to a temp folder and opens the AI upscaler with these images.</summary>
    [RelayCommand]
    private async Task SendToUpscaleAsync(CacheThumb? item)
    {
        List<CacheThumb> items = item is not null && !_selection.Contains(item) ? [item] : _selection.ToList();
        if (items.Count == 0)
        {
            return;
        }

        var target = Path.Combine(Path.GetTempPath(), "CONVERTit-thumbcache", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        var paths = await Task.Run(() => WriteFiles(items, target));
        Main.SendTo(Main.Upscale, paths);
    }

    // ------------------------------------------------------------ Mini viewer

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsViewerOpen), nameof(ViewerPosition), nameof(ViewerInfo), nameof(ViewerTitle))]
    private CacheThumb? _viewerItem;

    [ObservableProperty] private Bitmap? _viewerImage;

    /// <summary>0 = fit to the window, otherwise a factor (1 = 100 %).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ViewerFit), nameof(ViewerWidth), nameof(ViewerHeight), nameof(ZoomText))]
    private double _viewerZoom;

    public bool IsViewerOpen => ViewerItem is not null;

    public bool ViewerFit => ViewerZoom <= 0;

    public double ViewerWidth => ViewerImage is null ? 0 : ViewerImage.PixelSize.Width * Math.Max(ViewerZoom, 0.05);

    public double ViewerHeight => ViewerImage is null ? 0 : ViewerImage.PixelSize.Height * Math.Max(ViewerZoom, 0.05);

    public string ZoomText => ViewerFit ? Loc.T("ZoomFit") : $"{ViewerZoom * 100:0} %";

    public string ViewerTitle => ViewerItem?.Label ?? "";

    public string ViewerPosition => ViewerItem is null ? "" : $"{_filtered.IndexOf(ViewerItem) + 1} / {_filtered.Count}";

    public string ViewerInfo => ViewerItem is null
        ? ""
        : ViewerItem.Details + " · " + ViewerItem.SourceName
          + (ViewerItem.Variants > 0 ? " · " + Loc.F("ThumbcacheVariants", ViewerItem.Variants) : "");

    partial void OnViewerImageChanged(Bitmap? value)
    {
        OnPropertyChanged(nameof(ViewerWidth));
        OnPropertyChanged(nameof(ViewerHeight));
    }

    [RelayCommand]
    private void OpenViewer(CacheThumb? item)
    {
        item ??= _selection.FirstOrDefault() ?? Visible.FirstOrDefault();
        if (item is null)
        {
            return;
        }

        ViewerZoom = 0;
        ShowInViewer(item);
    }

    private void ShowInViewer(CacheThumb item)
    {
        ViewerItem = item;
        var old = ViewerImage;
        ViewerImage = DecodeFull(item.Data.Data);
        old?.Dispose();
    }

    [RelayCommand]
    private void CloseViewer()
    {
        ViewerItem = null;
        var old = ViewerImage;
        ViewerImage = null;
        old?.Dispose();
    }

    [RelayCommand]
    private void ViewerNext() => Step(1);

    [RelayCommand]
    private void ViewerPrevious() => Step(-1);

    private void Step(int delta)
    {
        if (ViewerItem is null || _filtered.Count == 0)
        {
            return;
        }

        var index = (_filtered.IndexOf(ViewerItem) + delta + _filtered.Count) % _filtered.Count;
        // Keep the grid in step when the viewer walks past the loaded pages.
        while (index >= _shown && HasMore)
        {
            ShowMore();
        }

        ShowInViewer(_filtered[index]);
    }

    [RelayCommand]
    private void ZoomIn() => ViewerZoom = Math.Min(8, (ViewerFit ? 1 : ViewerZoom) * 1.25);

    [RelayCommand]
    private void ZoomOut() => ViewerZoom = Math.Max(0.1, (ViewerFit ? 1 : ViewerZoom) / 1.25);

    [RelayCommand]
    private void ZoomFit() => ViewerZoom = 0;

    [RelayCommand]
    private void ZoomActual() => ViewerZoom = 1;

    // ------------------------------------------------------------ Clean cache

    /// <summary>Opt-in dialog, then reset the chosen caches (optionally with a short Explorer restart).</summary>
    [RelayCommand]
    private async Task CleanCacheAsync()
    {
        if (!OperatingSystem.IsWindows() || IsCleaning)
        {
            return;
        }

        var choice = await Host.CleanCacheAsync();
        if (choice is null)
        {
            return;
        }

        IsCleaning = true;
        _cts?.Cancel();
        CloseViewer();
        _all.Clear();
        ApplyFilter();
        try
        {
            var kinds = new List<Platform.Windows.ShellCacheKind>();
            if (choice.Thumbnails)
            {
                kinds.Add(Platform.Windows.ShellCacheKind.Thumbnails);
            }

            if (choice.Icons)
            {
                kinds.Add(Platform.Windows.ShellCacheKind.Icons);
            }

            var result = await Task.Run(() => Platform.Windows.ShellCacheCleaner.Clean(kinds, choice.RestartExplorer));
            if (choice.OwnPreviews)
            {
                ThumbnailService.ClearCache();
            }

            var text = Loc.F("ToastCacheCleaned", result.Deleted, Units.Bytes(result.FreedBytes));
            if (result.Locked > 0)
            {
                text += " " + Loc.F("ToastCacheLocked", result.Locked);
            }

            Main.ShowToast(text, error: result.Locked > 0 && result.Deleted == 0);
        }
        catch (Exception ex)
        {
            Main.ShowToast(ex.Message, error: true);
        }
        finally
        {
            IsCleaning = false;
        }

        OnPropertyChanged(nameof(CacheChoices));
        StatusText = Loc.T("ThumbcacheCleanedHint");
        _quietChoice = true;
        SelectedChoice = null;
        _quietChoice = false;
    }

    // ------------------------------------------------------------

    private void RaiseItems()
    {
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasMore));
        OnPropertyChanged(nameof(MoreText));
        OnPropertyChanged(nameof(IsAllCaches));
        RaiseSelection();
    }

    private void RaiseSelection()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(AllSelected));
        OnPropertyChanged(nameof(MarkedCount));
        OnPropertyChanged(nameof(HasMarks));
        OnPropertyChanged(nameof(CountText));
    }
}
