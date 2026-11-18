using System.Collections.ObjectModel;
using System.Text.Json;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CONVERTit.App.Localization;
using CONVERTit.Core.GameDev;
using CONVERTit.Core.Jobs;
using CONVERTit.Core.Media;
using CONVERTit.Core.Options;
using CONVERTit.Engines.Image;

namespace CONVERTit.App.ViewModels;

// ==================================================================== Image › App icons

/// <summary>Preview tile of the app icon editor.</summary>
public sealed record AppIconPreview(string Label, Bitmap Image, double Width, double Height);

/// <summary>
/// App icon generator: one motif → all icon files with exact sizes for Android (incl. adaptive and TV), iOS,
/// macOS, Windows, Web/PWA and Godot. Mini editor: scale, move (drag the preview, mouse wheel = size) and background.
/// </summary>
public sealed partial class AppIconsViewModel : ConvertPageViewModel
{
    private CancellationTokenSource? _previewCts;
    private bool _loading;

    public AppIconsViewModel(MainViewModel main) : base(main, new HashSet<MediaKind> { MediaKind.Image }, MediaKind.Image)
    {
        Output.Options.SubfolderName = "app-icons";
        Sources.SelectionChanged += _ => SchedulePreview();
    }

    public override string TitleKey => "NavAppIcons";

    public override string IconKey => "IconCellphone";

    public override bool NeedsFfmpeg => false;

    [ObservableProperty] private bool _android = true;
    [ObservableProperty] private bool _androidTv;
    [ObservableProperty] private bool _ios = true;
    [ObservableProperty] private bool _macOs;
    [ObservableProperty] private bool _windows;
    [ObservableProperty] private bool _web = true;
    [ObservableProperty] private bool _godot;

    /// <summary>Motif size in percent of the usable area.</summary>
    [ObservableProperty] private int _scale = 100;

    [ObservableProperty] private int _offsetX;
    [ObservableProperty] private int _offsetY;
    [ObservableProperty] private string _background = "#FFFFFF";
    [ObservableProperty] private bool _transparentBackground;
    [ObservableProperty] private string _appName = "App";
    [ObservableProperty] private bool _showSafeZone = true;
    [ObservableProperty] private bool _isRendering;

    public ObservableCollection<AppIconPreview> Previews { get; } = [];

    /// <summary>Color presets next to the color field.</summary>
    public string[] BackgroundPresets { get; } = ["#FFFFFF", "#000000", "#1C1B21", "#BBF236", "#478CBF", "#3DDC84", "#FF5A5F"];

    public AppIconTarget Targets =>
        (Android ? AppIconTarget.Android : 0) | (AndroidTv ? AppIconTarget.AndroidTv : 0) | (Ios ? AppIconTarget.Ios : 0)
        | (MacOs ? AppIconTarget.MacOs : 0) | (Windows ? AppIconTarget.Windows : 0) | (Web ? AppIconTarget.Web : 0)
        | (Godot ? AppIconTarget.Godot : 0);

    /// <summary>"58 files" for the selected platforms.</summary>
    public string FileCountText => Loc.F("AppIconFiles", AppIconSpecs.For(Targets).Count);

    protected override bool ExtraCanStart() => Targets != AppIconTarget.None;

    private AppIconSettings Settings() => new()
    {
        Targets = Targets,
        Scale = Scale,
        OffsetX = OffsetX,
        OffsetY = OffsetY,
        Background = Background,
        TransparentBackground = TransparentBackground,
        AppName = string.IsNullOrWhiteSpace(AppName) ? "App" : AppName.Trim()
    };

    protected override Task<List<ConversionJob>> PlanAsync() =>
        Task.FromResult(Main.Conversion.AppIcons(Sources.Refs.ToList(), Settings(), Output.Options));

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (_loading)
        {
            return;
        }

        switch (e.PropertyName)
        {
            case nameof(Android) or nameof(AndroidTv) or nameof(Ios) or nameof(MacOs) or nameof(Windows) or nameof(Web) or nameof(Godot):
                OnPropertyChanged(nameof(FileCountText));
                StartCommand.NotifyCanExecuteChanged();
                SchedulePreview();
                break;
            case nameof(Scale) or nameof(OffsetX) or nameof(OffsetY) or nameof(Background) or nameof(TransparentBackground) or nameof(ShowSafeZone):
                SchedulePreview();
                break;
        }
    }

    // ---------------------------------------------------------------- Mini editor

    /// <summary>Dragging the preview: pixel delta relative to the preview size → offset in percent.</summary>
    public void Nudge(double dxFraction, double dyFraction)
    {
        OffsetX = Math.Clamp(OffsetX + (int)Math.Round(dxFraction * 100), -50, 50);
        OffsetY = Math.Clamp(OffsetY + (int)Math.Round(dyFraction * 100), -50, 50);
    }

    /// <summary>Mouse wheel on the preview: size in 5 % steps.</summary>
    public void Zoom(int steps) => Scale = Math.Clamp(Scale + steps * 5, 10, 200);

    [RelayCommand]
    private void ResetEditor()
    {
        Scale = 100;
        OffsetX = 0;
        OffsetY = 0;
    }

    [RelayCommand]
    private void SetBackground(string? color)
    {
        if (!string.IsNullOrWhiteSpace(color))
        {
            Background = color;
        }
    }

    private void SchedulePreview() => _ = RenderPreviewAsync();

    /// <summary>Previews: Android adaptive (circle, safe zone), iOS, Web/Windows and – if chosen – the TV banner.</summary>
    private async Task RenderPreviewAsync()
    {
        _previewCts?.Cancel();
        var cts = _previewCts = new CancellationTokenSource();
        var item = Sources.SelectedItem ?? Sources.Items.FirstOrDefault();
        if (item is null)
        {
            Previews.Clear();
            return;
        }

        IsRendering = true;
        try
        {
            // Short pause: dragging and typing produce many changes in a row.
            await Task.Delay(120, cts.Token);
            var settings = Settings();
            var safe = ShowSafeZone;
            var tv = AndroidTv;
            var path = item.Path;
            var tiles = await Task.Run(() =>
            {
                var list = new List<(string, byte[], double, double)>
                {
                    (Loc.T("PreviewAndroid"), GameDevEngine.PreviewAppIcon(path, settings, IconLayer.AdaptiveForeground, IconPreviewShape.Circle, 256, safe), 160, 160),
                    (Loc.T("PreviewIos"), GameDevEngine.PreviewAppIcon(path, settings, IconLayer.Full, IconPreviewShape.Squircle, 256, false), 160, 160),
                    (Loc.T("PreviewWeb"), GameDevEngine.PreviewAppIcon(path, settings, IconLayer.Full, IconPreviewShape.Square, 256, false), 160, 160)
                };
                if (tv)
                {
                    list.Add((Loc.T("PreviewTv"), GameDevEngine.PreviewAppIcon(path, settings, IconLayer.Banner, IconPreviewShape.Banner, 320, false), 240, 135));
                }

                return list;
            }, cts.Token);
            if (cts.IsCancellationRequested)
            {
                return;
            }

            Previews.Clear();
            foreach (var (label, bytes, w, h) in tiles)
            {
                Previews.Add(new AppIconPreview(label, new Bitmap(new MemoryStream(bytes)), w, h));
            }
        }
        catch (OperationCanceledException)
        {
            // A newer preview is on its way.
        }
        catch (Exception ex)
        {
            Main.ShowToast(ex.Message, error: true);
        }
        finally
        {
            if (ReferenceEquals(cts, _previewCts))
            {
                IsRendering = false;
            }
        }
    }

    // ---------------------------------------------------------------- State

    /// <summary>Saved editor settings.</summary>
    public sealed class AppIconState
    {
        public AppIconTarget Targets { get; set; } = AppIconTarget.Android | AppIconTarget.Ios | AppIconTarget.Web;
        public int Scale { get; set; } = 100;
        public string Background { get; set; } = "#FFFFFF";
        public bool TransparentBackground { get; set; }
        public string AppName { get; set; } = "App";
    }

    public override void SaveState(Dictionary<string, JsonElement> pages) =>
        Save(pages, StateKey, Output.Options, Sources.Folder, new AppIconState
        {
            Targets = Targets, Scale = Scale, Background = Background, TransparentBackground = TransparentBackground, AppName = AppName
        });

    public override void LoadState(Dictionary<string, JsonElement> pages)
    {
        var state = new AppIconState();
        Load(pages, StateKey, Output.Options, Sources.Folder, state);
        _loading = true;
        Android = state.Targets.HasFlag(AppIconTarget.Android);
        AndroidTv = state.Targets.HasFlag(AppIconTarget.AndroidTv);
        Ios = state.Targets.HasFlag(AppIconTarget.Ios);
        MacOs = state.Targets.HasFlag(AppIconTarget.MacOs);
        Windows = state.Targets.HasFlag(AppIconTarget.Windows);
        Web = state.Targets.HasFlag(AppIconTarget.Web);
        Godot = state.Targets.HasFlag(AppIconTarget.Godot);
        Scale = state.Scale;
        Background = state.Background;
        TransparentBackground = state.TransparentBackground;
        AppName = state.AppName;
        _loading = false;
        OnPropertyChanged(nameof(FileCountText));
    }

    public override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        OnPropertyChanged(nameof(FileCountText));
        SchedulePreview();
    }
}

// ==================================================================== Image › Sprite sheet

/// <summary>
/// Sprite sheet generator: images (one animation), GIF/WebP and videos (one animation each) → one sheet with
/// filters (trim, scale, pixel art, palette, outline) and metadata for Godot (.tres) and TexturePacker (JSON).
/// </summary>
public sealed partial class SpritesViewModel : ConvertPageViewModel
{
    public SpritesViewModel(MainViewModel main) : base(main, new HashSet<MediaKind> { MediaKind.Image, MediaKind.Video }, MediaKind.Image)
    {
        Output.Options.SubfolderName = "sprites";
    }

    public override string TitleKey => "NavSprites";

    public override string IconKey => "IconViewGrid";

    /// <summary>Images only need ImageMagick; videos are split by FFmpeg (checked per job).</summary>
    public override bool NeedsFfmpeg => false;

    [ObservableProperty] private string _sheetName = "spritesheet";
    [ObservableProperty] private string _animationName = "default";
    [ObservableProperty] private int _videoFps = 12;

    // Observable mirrors of the engine settings (two-way bound).
    [ObservableProperty] private SpriteLayout _layout = SpriteLayout.Grid;
    [ObservableProperty] private int _columns;
    [ObservableProperty] private int _spacing = 2;
    [ObservableProperty] private bool _powerOfTwo;
    [ObservableProperty] private bool _trimTransparent = true;
    [ObservableProperty] private double _scale = 1;
    [ObservableProperty] private bool _pixelArt;
    [ObservableProperty] private int _paletteColors;
    [ObservableProperty] private int _outline;
    [ObservableProperty] private string _outlineColor = "#000000";
    [ObservableProperty] private bool _writeJson = true;
    [ObservableProperty] private bool _writeGodot = true;
    [ObservableProperty] private int _fps = 12;
    [ObservableProperty] private bool _loop = true;
    [ObservableProperty] private string _godotFolder = "res://sprites/";

    public SpriteLayout[] Layouts { get; } = Enum.GetValues<SpriteLayout>();

    public int[] PaletteChoices { get; } = [0, 8, 16, 32, 64, 128, 256];

    public double[] ScaleChoices { get; } = [0.25, 0.5, 0.75, 1, 1.5, 2, 3, 4];

    public bool IsGrid => Layout == SpriteLayout.Grid;

    partial void OnLayoutChanged(SpriteLayout value) => OnPropertyChanged(nameof(IsGrid));

    protected override bool ExtraCanStart() => Sources.Items.Count > 0;

    private SpriteSettings Build() => new()
    {
        Layout = Layout,
        Columns = Columns,
        Spacing = Spacing,
        PowerOfTwo = PowerOfTwo,
        TrimTransparent = TrimTransparent,
        Scale = Scale,
        PixelArt = PixelArt,
        PaletteColors = PaletteColors,
        Outline = Outline,
        OutlineColor = OutlineColor,
        Metadata = (WriteJson ? SpriteMetadata.Json : 0) | (WriteGodot ? SpriteMetadata.GodotTres : 0),
        Fps = Fps,
        Loop = Loop,
        GodotFolder = GodotFolder
    };

    protected override Task<List<ConversionJob>> PlanAsync()
    {
        var sources = Sources.Refs.ToList();
        if (sources.Count == 0)
        {
            return Task.FromResult(new List<ConversionJob>());
        }

        if (sources.Any(s => MediaTypes.Detect(s.Path) == MediaKind.Video) && !Main.Locator.IsAvailable)
        {
            throw new Core.Ffmpeg.ConversionException("ErrFfmpegMissing");
        }

        var name = string.Join("_", (string.IsNullOrWhiteSpace(SheetName) ? "spritesheet" : SheetName.Trim()).Split(Path.GetInvalidFileNameChars()));
        return Task.FromResult(new List<ConversionJob>
        {
            Main.Conversion.SpriteSheet(sources, name, AnimationName.Trim(), Build(), Math.Clamp(VideoFps, 1, 60), Output.Options)
        });
    }

    /// <summary>Saved settings (mirrors of the observable properties).</summary>
    public sealed class SpriteState
    {
        public SpriteLayout Layout { get; set; } = SpriteLayout.Grid;
        public int Columns { get; set; }
        public int Spacing { get; set; } = 2;
        public bool PowerOfTwo { get; set; }
        public bool TrimTransparent { get; set; } = true;
        public double Scale { get; set; } = 1;
        public bool PixelArt { get; set; }
        public int PaletteColors { get; set; }
        public int Outline { get; set; }
        public string OutlineColor { get; set; } = "#000000";
        public bool WriteJson { get; set; } = true;
        public bool WriteGodot { get; set; } = true;
        public int Fps { get; set; } = 12;
        public bool Loop { get; set; } = true;
        public string GodotFolder { get; set; } = "res://sprites/";
        public int VideoFps { get; set; } = 12;
    }

    public override void SaveState(Dictionary<string, JsonElement> pages) =>
        Save(pages, StateKey, Output.Options, Sources.Folder, new SpriteState
        {
            Layout = Layout, Columns = Columns, Spacing = Spacing, PowerOfTwo = PowerOfTwo, TrimTransparent = TrimTransparent, Scale = Scale,
            PixelArt = PixelArt, PaletteColors = PaletteColors, Outline = Outline, OutlineColor = OutlineColor, WriteJson = WriteJson,
            WriteGodot = WriteGodot, Fps = Fps, Loop = Loop, GodotFolder = GodotFolder, VideoFps = VideoFps
        });

    public override void LoadState(Dictionary<string, JsonElement> pages)
    {
        var s = new SpriteState();
        Load(pages, StateKey, Output.Options, Sources.Folder, s);
        Layout = s.Layout;
        Columns = s.Columns;
        Spacing = s.Spacing;
        PowerOfTwo = s.PowerOfTwo;
        TrimTransparent = s.TrimTransparent;
        Scale = s.Scale;
        PixelArt = s.PixelArt;
        PaletteColors = s.PaletteColors;
        Outline = s.Outline;
        OutlineColor = s.OutlineColor;
        WriteJson = s.WriteJson;
        WriteGodot = s.WriteGodot;
        Fps = s.Fps;
        Loop = s.Loop;
        GodotFolder = s.GodotFolder;
        VideoFps = s.VideoFps;
    }
}

// ==================================================================== Video › Godot video

/// <summary>Videos for Godot: Ogg Theora (.ogv), the only format Godot's VideoStreamPlayer plays.</summary>
public sealed class GodotVideoViewModel : ConvertPageViewModel
{
    public GodotVideoViewModel(MainViewModel main) : base(main, new HashSet<MediaKind> { MediaKind.Video }, MediaKind.Video)
    {
        Output.Options.SubfolderName = "godot";
    }

    public override string TitleKey => "NavGodotVideo";

    public override string IconKey => "IconGamepad";

    public GodotVideoOptions Godot { get; } = new();

    public int[] Heights { get; } = [0, 2160, 1440, 1080, 720, 540, 480, 360];

    public int[] Rates { get; } = [0, 60, 30, 25, 24];

    public int[] AudioQualities { get; } = [-1, 3, 5, 7, 9];

    /// <summary>Theora encoder missing in the FFmpeg build: shown as a warning.</summary>
    public bool TheoraMissing => Main.Locator.IsAvailable && Main.Settings.Encoders is { Count: > 0 } list && !list.Contains("libtheora");

    protected override Task<List<ConversionJob>> PlanAsync() =>
        Task.FromResult(Main.Conversion.GodotVideo(Sources.Refs.ToList(), Godot, Output.Options));

    public override void SaveState(Dictionary<string, JsonElement> pages) => Save(pages, StateKey, Godot, Output.Options, Sources.Folder);

    public override void LoadState(Dictionary<string, JsonElement> pages) => Load(pages, StateKey, Godot, Output.Options, Sources.Folder);

    public override void OnActivated()
    {
        base.OnActivated();
        OnPropertyChanged(nameof(TheoraMissing));
    }
}
