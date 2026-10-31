using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CONVERTit.App.Localization;
using CONVERTit.App.Services;
using CONVERTit.Core.Media;
using CONVERTit.Core.Storage;
using CONVERTit.Core.Tags;
using CONVERTit.Engines.Image;
using CONVERTit.Engines.Tags;

namespace CONVERTit.App.ViewModels;

/// <summary>One editable tag field; with several files selected, different values show "‹keep›".</summary>
public sealed partial class TagFieldItem(TagField field, string labelKey, bool wide = false, bool numeric = false) : ObservableObject
{
    public TagField Field { get; } = field;

    public string Label => Loc.T(labelKey);

    /// <summary>Full width (comment).</summary>
    public bool Wide { get; } = wide;

    public bool Numeric { get; } = numeric;

    [ObservableProperty] private string _value = "";

    /// <summary>Value when the selection was loaded ("" for different values).</summary>
    public string Original { get; private set; } = "";

    /// <summary>The selected files have different values.</summary>
    public bool Mixed { get; private set; }

    /// <summary>Shown inside the empty box: the field name, or "‹keep›" when the selected files differ.</summary>
    public string Watermark => Mixed ? Loc.T("TagKeep") : Label;

    public bool IsDirty => Value != Original;

    public void Load(string value, bool mixed)
    {
        Mixed = mixed;
        Original = mixed ? "" : value;
        Value = Original;
        OnPropertyChanged(nameof(Mixed));
        OnPropertyChanged(nameof(Watermark));
    }

    public void RefreshText()
    {
        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(Watermark));
    }
}

/// <summary>
/// Audio › Tags: tag editor for MP3 (ID3v2.3/2.4, optional ID3v1), FLAC, OGG, Opus, M4A, WAV and AIFF.
/// Several files can be edited at once; only changed fields are written. Tools: tags from file names,
/// file names from tags, track numbers, title case. Files are changed in place (with a backup during the write).
/// </summary>
public sealed partial class TagEditorViewModel : PageViewModel
{
    private readonly Dictionary<SourceItem, TagData> _tags = new();
    private readonly HashSet<SourceItem> _reading = [];
    private byte[]? _newCover;
    private bool _coverChanged;

    public TagEditorViewModel(MainViewModel main) : base(main)
    {
        Sources = new SourceListViewModel(main, new HashSet<MediaKind> { MediaKind.Audio }, MediaKind.Audio)
        {
            ProbeMedia = false,
            Accept = TagEngine.Supports,
            MiniPlayerKey = "Tags"
        };
        Sources.Changed += () => _ = LoadNewTagsAsync();
        Sources.SelectionSetChanged += LoadEditor;
        Sources.SelectionChanged += _ => LoadEditor();

        Fields = new TagFieldItem[]
        {
            new(TagField.Title, "TagTitle"),
            new(TagField.Artist, "TagArtist"),
            new(TagField.Album, "TagAlbum"),
            new(TagField.AlbumArtist, "TagAlbumArtist"),
            new(TagField.Year, "TagYear", numeric: true),
            new(TagField.Genre, "TagGenre"),
            new(TagField.Track, "TagTrack", numeric: true),
            new(TagField.TrackCount, "TagTrackCount", numeric: true),
            new(TagField.Disc, "TagDisc", numeric: true),
            new(TagField.DiscCount, "TagDiscCount", numeric: true),
            new(TagField.Composer, "TagComposer"),
            new(TagField.Comment, "TagComment", wide: true)
        };
        foreach (var field in Fields)
        {
            field.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(TagFieldItem.Value))
                {
                    RaiseDirty();
                }
            };
        }

        _id3Version = main.Settings.Id3Version;
        _writeId3v1 = main.Settings.WriteId3v1;
        _keepFileDate = main.Settings.TagsKeepFileDate;
        _resizeCover = main.Settings.TagsResizeCover;
        _pattern = main.Settings.TagPattern;
    }

    public override string TitleKey => "NavTags";

    public override string IconKey => "IconTagMultiple";

    public SourceListViewModel Sources { get; }

    /// <summary>All fields (a real array: the view binds the named properties below).</summary>
    public TagFieldItem[] Fields { get; }

    public TagFieldItem TitleField => Fields[0];

    public TagFieldItem ArtistField => Fields[1];

    public TagFieldItem AlbumField => Fields[2];

    public TagFieldItem AlbumArtistField => Fields[3];

    public TagFieldItem YearField => Fields[4];

    public TagFieldItem GenreField => Fields[5];

    public TagFieldItem TrackField => Fields[6];

    public TagFieldItem TrackCountField => Fields[7];

    public TagFieldItem DiscField => Fields[8];

    public TagFieldItem DiscCountField => Fields[9];

    public TagFieldItem ComposerField => Fields[10];

    public TagFieldItem CommentField => Fields[11];


    public Id3Version[] Id3Versions { get; } = Enum.GetValues<Id3Version>();

    [ObservableProperty] private Id3Version _id3Version;
    [ObservableProperty] private bool _writeId3v1;
    [ObservableProperty] private bool _keepFileDate;
    [ObservableProperty] private bool _resizeCover;
    [ObservableProperty] private string _pattern;
    [ObservableProperty] private Bitmap? _coverImage;
    [ObservableProperty] private string _coverInfo = "";
    [ObservableProperty] private bool _isBusy;

    partial void OnId3VersionChanged(Id3Version value) => Persist(s => s.Id3Version = value);
    partial void OnWriteId3v1Changed(bool value) => Persist(s => s.WriteId3v1 = value);
    partial void OnKeepFileDateChanged(bool value) => Persist(s => s.TagsKeepFileDate = value);
    partial void OnResizeCoverChanged(bool value) => Persist(s => s.TagsResizeCover = value);
    partial void OnPatternChanged(string value) => Persist(s => s.TagPattern = value);

    private void Persist(Action<AppSettings> change)
    {
        change(Main.Settings);
        Main.Settings.Save();
    }

    /// <summary>Files the editor works on: the selection, otherwise the focused file.</summary>
    private List<SourceItem> Targets =>
        Sources.Selection.Count > 0 ? Sources.Selection.Where(Sources.Items.Contains).ToList()
        : Sources.SelectedItem is { } one ? [one] : [];

    public bool HasTargets => Targets.Count > 0;

    public string TargetText => Targets.Count switch
    {
        0 => Loc.T("TagNoSelection"),
        1 => Targets[0].Name,
        var n => Loc.F("TagFilesSelected", n)
    };

    public string TagTypesText => Targets.Count == 1 && _tags.TryGetValue(Targets[0], out var t) ? t.TagTypes : "";

    public bool HasCover => CoverImage is not null;

    public bool IsDirty => _coverChanged || Fields.Any(f => f.IsDirty);

    public bool IsMp3Selected => Targets.Any(t => t.Extension is "MP3" or "WAV" or "AIF" or "AIFF");

    partial void OnCoverImageChanged(Bitmap? value) => OnPropertyChanged(nameof(HasCover));

    private void RaiseDirty()
    {
        OnPropertyChanged(nameof(IsDirty));
        SaveCommand.NotifyCanExecuteChanged();
        RevertCommand.NotifyCanExecuteChanged();
    }

    private void RaiseTargets()
    {
        OnPropertyChanged(nameof(HasTargets));
        OnPropertyChanged(nameof(TargetText));
        OnPropertyChanged(nameof(TagTypesText));
        OnPropertyChanged(nameof(IsMp3Selected));
        foreach (var command in new IRelayCommand[]
                 {
                     SaveCommand, ChooseCoverCommand, RemoveCoverCommand, ExportCoverCommand, TagsFromNamesCommand,
                     RenameFromTagsCommand, NumberTracksCommand, TitleCaseCommand
                 })
        {
            command.NotifyCanExecuteChanged();
        }
    }

    // ---------------------------------------------------------------- Reading

    private async Task LoadNewTagsAsync()
    {
        foreach (var gone in _tags.Keys.Where(k => !Sources.Items.Contains(k)).ToList())
        {
            _tags.Remove(gone);
        }

        var pending = Sources.Items.Where(i => !_tags.ContainsKey(i) && _reading.Add(i)).ToList();
        if (pending.Count == 0)
        {
            LoadEditor();
            return;
        }

        using var activity = Main.BeginActivity(Loc.F("ActivityReadingTags", pending.Count));
        foreach (var item in pending)
        {
            try
            {
                var data = await Task.Run(() => TagEngine.Read(item.Path));
                _tags[item] = data;
                item.Summary = Summary(data);
            }
            catch (Exception ex)
            {
                _tags[item] = new TagData();
                item.Error = ex.Message;
                item.Summary = Loc.T("TagReadFailed");
            }
            finally
            {
                _reading.Remove(item);
            }
        }

        LoadEditor();
    }

    private static string Summary(TagData data)
    {
        var artist = data.Get(TagField.Artist);
        var title = data.Get(TagField.Title);
        var text = artist.Length > 0 && title.Length > 0 ? $"{artist} – {title}" : title.Length > 0 ? title : Loc.T("TagEmpty");
        return data.Cover is not null ? text + " · ♪ " + Loc.T("TagHasCover") : text;
    }

    /// <summary>Fills the editor from the selected files (same value → shown, different → "‹keep›").</summary>
    private void LoadEditor()
    {
        var targets = Targets.Where(_tags.ContainsKey).ToList();
        foreach (var field in Fields)
        {
            var values = targets.Select(t => _tags[t].Get(field.Field)).Distinct().ToList();
            field.Load(values.Count == 1 ? values[0] : "", values.Count > 1);
        }

        _newCover = null;
        _coverChanged = false;
        var covers = targets.Select(t => _tags[t].Cover).ToList();
        var first = covers.FirstOrDefault();
        var same = covers.Count > 0 && covers.All(c => c is null ? first is null : first is not null && c.AsSpan().SequenceEqual(first));
        ShowCover(same ? first : null, mixed: !same);
        RaiseTargets();
        RaiseDirty();
    }

    private void ShowCover(byte[]? data, bool mixed = false)
    {
        try
        {
            CoverImage = data is { Length: > 0 } ? new Bitmap(new MemoryStream(data)) : null;
        }
        catch
        {
            CoverImage = null;
        }

        CoverInfo = mixed ? Loc.T("TagCoverMixed")
            : data is { Length: > 0 } ? $"{ImageEngine.Describe(data)} · {Units.Bytes(data.Length)}"
            : Loc.T("TagNoCover");
    }

    // ---------------------------------------------------------------- Cover

    private bool CanEdit() => HasTargets && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task ChooseCoverAsync()
    {
        var files = await Host.PickFilesAsync(Loc.T("PickCover"), ["jpg", "jpeg", "png", "webp", "bmp"], multiple: false);
        if (files.Count == 0)
        {
            return;
        }

        try
        {
            var bytes = await File.ReadAllBytesAsync(files[0]);
            var resize = ResizeCover;
            // JPEG keeps the tags small; PNG stays PNG unless it is resized.
            _newCover = await Task.Run(() => resize || !files[0].EndsWith(".png", StringComparison.OrdinalIgnoreCase) && !files[0].EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) && !files[0].EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
                ? ImageEngine.CoverJpeg(bytes, resize ? 1000 : 0)
                : bytes);
            _coverChanged = true;
            ShowCover(_newCover);
            RaiseDirty();
        }
        catch (Exception ex)
        {
            Main.ShowToast(ex.Message, error: true);
        }
    }

    /// <summary>Cover dropped on the cover area.</summary>
    public async Task SetCoverFromFileAsync(string path)
    {
        if (!HasTargets)
        {
            return;
        }

        try
        {
            var bytes = await File.ReadAllBytesAsync(path);
            var resize = ResizeCover;
            _newCover = await Task.Run(() => ImageEngine.CoverJpeg(bytes, resize ? 1000 : 0));
            _coverChanged = true;
            ShowCover(_newCover);
            RaiseDirty();
        }
        catch (Exception ex)
        {
            Main.ShowToast(ex.Message, error: true);
        }
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void RemoveCover()
    {
        _newCover = null;
        _coverChanged = true;
        ShowCover(null);
        RaiseDirty();
    }

    /// <summary>Saves the shown cover next to the first file as cover.jpg/png and shows it in Explorer.</summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task ExportCoverAsync()
    {
        var first = Targets.FirstOrDefault();
        var data = _coverChanged ? _newCover : first is not null && _tags.TryGetValue(first, out var t) ? t.Cover : null;
        if (first is null || data is not { Length: > 0 })
        {
            Main.ShowToast(Loc.T("TagNoCover"), error: true);
            return;
        }

        var png = data.Length > 8 && data[0] == 0x89 && data[1] == 0x50;
        var path = Path.Combine(Path.GetDirectoryName(first.Path) ?? "", png ? "cover.png" : "cover.jpg");
        await File.WriteAllBytesAsync(path, data);
        AppEnvironment.ShowInExplorer(path);
    }

    // ---------------------------------------------------------------- Saving

    private bool CanSave() => CanEdit() && IsDirty;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        var edit = new TagEdit { CoverChanged = _coverChanged, Cover = _newCover };
        if (_newCover is { Length: > 8 } c && c[0] == 0x89 && c[1] == 0x50)
        {
            edit.CoverMime = "image/png";
        }

        foreach (var field in Fields.Where(f => f.IsDirty))
        {
            edit.Values[field.Field] = field.Value;
        }

        await WriteAsync(Targets, _ => edit);
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Revert() => LoadEditor();

    private TagWriteOptions WriteOptions => new()
    {
        Id3Version = Id3Version == Id3Version.V24 ? 4 : 3,
        WriteId3v1 = WriteId3v1,
        KeepFileDate = KeepFileDate
    };

    /// <summary>Writes per-file edits on a worker thread; progress in the header. Returns the number of written files.</summary>
    private async Task<int> WriteAsync(IReadOnlyList<SourceItem> targets, Func<SourceItem, TagEdit?> editFor)
    {
        if (targets.Count == 0)
        {
            return 0;
        }

        // A playing file is partly locked by Windows: stop the mini player first.
        Main.AudioPreview.StopIf(targets.Contains);

        IsBusy = true;
        RaiseTargets();
        var options = WriteOptions;
        var failed = 0;
        var done = 0;
        var written = 0;
        using var activity = Main.BeginActivity(Loc.F("ActivityWritingTags", targets.Count));
        try
        {
            foreach (var item in targets)
            {
                var edit = editFor(item);
                if (edit is not null)
                {
                    try
                    {
                        await Task.Run(() => TagEngine.Write(item.Path, edit, options));
                        _tags[item] = await Task.Run(() => TagEngine.Read(item.Path));
                        item.Summary = Summary(_tags[item]);
                        item.Error = "";
                        written++;
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        item.Error = ex.Message;
                    }
                }

                done++;
                activity.Progress = (double)done / targets.Count;
            }
        }
        finally
        {
            IsBusy = false;
        }

        LoadEditor();
        if (written > 0 || failed > 0)
        {
            Main.ShowToast(failed == 0 ? Loc.F("ToastTagsSaved", written) : Loc.F("ToastTagsFailed", failed, written + failed), failed > 0);
        }

        return written;
    }

    // ---------------------------------------------------------------- Tools

    /// <summary>"%track% - %artist% - %title%" → tags of every selected file.</summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task TagsFromNamesAsync()
    {
        var written = await WriteAsync(Targets, item =>
        {
            var values = TagPattern.Parse(Path.GetFileNameWithoutExtension(item.Path), Pattern);
            if (values.Count == 0)
            {
                return null;
            }

            var edit = new TagEdit();
            foreach (var (field, value) in values)
            {
                edit.Values[field] = value;
            }

            return edit;
        });
        if (written == 0)
        {
            Main.ShowToast(Loc.T("ToastPatternNoMatch"), error: true);
        }
    }

    /// <summary>Renames the selected files from their tags with the pattern (the extension stays).</summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task RenameFromTagsAsync()
    {
        var targets = Targets;
        Main.AudioPreview.StopIf(targets.Contains);
        var renamed = new List<string>();
        var done = new List<SourceItem>();
        foreach (var item in targets)
        {
            if (!_tags.TryGetValue(item, out var data))
            {
                continue;
            }

            var name = TagPattern.Format(Pattern, data.Values);
            if (name.Length == 0)
            {
                continue;
            }

            var dir = Path.GetDirectoryName(item.Path) ?? "";
            var ext = Path.GetExtension(item.Path);
            var target = Path.Combine(dir, name + ext);
            if (string.Equals(target, item.Path, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            for (var n = 2; File.Exists(target); n++)
            {
                target = Path.Combine(dir, $"{name} ({n}){ext}");
            }

            try
            {
                File.Move(item.Path, target);
                renamed.Add(target);
                done.Add(item);
            }
            catch (Exception ex)
            {
                item.Error = ex.Message;
            }
        }

        if (done.Count > 0)
        {
            Sources.RemoveItems(done);
            await Sources.AddPathsAsync(renamed);
        }

        Main.ShowToast(Loc.F("ToastRenamed", done.Count));
    }

    /// <summary>Numbers the selected files 1…n in list order and sets the total.</summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task NumberTracksAsync()
    {
        var ordered = Sources.Items.Where(Targets.Contains).ToList();
        await WriteAsync(ordered, item =>
        {
            var edit = new TagEdit();
            edit.Values[TagField.Track] = (ordered.IndexOf(item) + 1).ToString();
            edit.Values[TagField.TrackCount] = ordered.Count.ToString();
            return edit;
        });
    }

    /// <summary>Title, artist, album and album artist in title case, spaces cleaned.</summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task TitleCaseAsync() => await WriteAsync(Targets, item =>
    {
        if (!_tags.TryGetValue(item, out var data))
        {
            return null;
        }

        var edit = new TagEdit();
        foreach (var field in new[] { TagField.Title, TagField.Artist, TagField.Album, TagField.AlbumArtist })
        {
            var value = data.Get(field);
            var fixedValue = TagPattern.TitleCase(TagPattern.CleanSpaces(value));
            if (value.Length > 0 && fixedValue != value)
            {
                edit.Values[field] = fixedValue;
            }
        }

        return edit.IsEmpty ? null : edit;
    });

    // ---------------------------------------------------------------- Page

    public override bool AcceptDrop(IReadOnlyList<string> paths)
    {
        // One image dropped while files are selected: it becomes the new cover.
        if (paths.Count == 1 && MediaTypes.Detect(paths[0]) == MediaKind.Image && HasTargets)
        {
            _ = SetCoverFromFileAsync(paths[0]);
            return true;
        }

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
        foreach (var field in Fields)
        {
            field.RefreshText();
        }

        Sources.RefreshTexts();
        LoadEditor();
    }
}
