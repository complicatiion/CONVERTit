using System.Collections.ObjectModel;
using System.Text.Json;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CONVERTit.App.Localization;
using CONVERTit.App.Services;
using CONVERTit.Core.Storage;
using CONVERTit.Core.Jobs;
using CONVERTit.Core.Media;
using CONVERTit.Core.Options;
using CONVERTit.Engines.Ffmpeg;
using CONVERTit.Engines.Image;

namespace CONVERTit.App.ViewModels;

// ==================================================================== Tools › Upscale (AI)

/// <summary>
/// AI upscaling with Real-ESRGAN or waifu2x (optional modules, GPU via Vulkan). Images directly, videos frame by
/// frame. Without the module the page shows where to get it.
/// </summary>
public sealed partial class UpscaleViewModel : ConvertPageViewModel
{
    public UpscaleViewModel(MainViewModel main)
        : base(main, new HashSet<MediaKind> { MediaKind.Image, MediaKind.Video }, MediaKind.Image)
    {
        Output.Options.SubfolderName = "upscaled";
        Upscale.PropertyChanged += (_, _) => RaiseModule();
        main.Modules.Changed += () => Dispatcher.UIThread.Post(RaiseModule);
        Sources.Changed += () =>
        {
            OnPropertyChanged(nameof(HasVideos));
            OnPropertyChanged(nameof(ShowFfmpegBanner));
        };
    }

    public override string TitleKey => "NavUpscale";

    public override string IconKey => "IconImageFilterHdr";

    public UpscaleOptions Upscale { get; } = new();

    public UpscaleEngine[] Engines { get; } = Enum.GetValues<UpscaleEngine>();

    public int[] Scales { get; } = [2, 3, 4];

    public int[] NoiseLevels { get; } = [-1, 0, 1, 2, 3];

    public int[] TileSizes { get; } = [0, 512, 256, 128, 64];

    public string[] ImageFormats { get; } = ["png", "jpg", "webp"];

    public bool IsWaifu2x => Upscale.Engine == UpscaleEngine.Waifu2x;

    public bool HasVideos => Sources.Items.Any(i => i.Kind == MediaKind.Video);

    /// <summary>Videos need FFmpeg (frames out and back in); images do not.</summary>
    public override bool NeedsFfmpeg => HasVideos;

    private ModuleDefinition Module => IsWaifu2x ? ModuleCatalog.Waifu2x : ModuleCatalog.RealEsrgan;

    public bool ModuleMissing => !Main.Modules.IsAvailable(Module.Id);

    public string ModuleMissingText => Loc.F("HintModuleMissing", Module.Name);

    protected override bool ExtraCanStart() => !ModuleMissing;

    private void RaiseModule()
    {
        OnPropertyChanged(nameof(IsWaifu2x));
        OnPropertyChanged(nameof(ModuleMissing));
        OnPropertyChanged(nameof(ModuleMissingText));
        StartCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void OpenDependencies() => Main.NavigateTo(Main.Dependencies);

    /// <summary>Opt-in dialog, then download right here.</summary>
    [RelayCommand]
    private async Task InstallModuleAsync()
    {
        await Main.Dependencies.InstallModuleAsync(Module.Id);
        RaiseModule();
    }

    protected override Task<List<ConversionJob>> PlanAsync() =>
        Task.FromResult(Main.Conversion.Upscale(Sources.Refs.ToList(), Upscale, Output.Options));

    public override void SaveState(Dictionary<string, JsonElement> pages) => Save(pages, StateKey, Upscale, Output.Options, Sources.Folder);

    public override void LoadState(Dictionary<string, JsonElement> pages) => Load(pages, StateKey, Upscale, Output.Options, Sources.Folder);

    public override void OnActivated()
    {
        base.OnActivated();
        RaiseModule();
    }
}

// ==================================================================== Tools › Denoise

/// <summary>Noise reduction for videos and audio with FFmpeg filters (no module needed).</summary>
public sealed class DenoiseViewModel : ConvertPageViewModel
{
    public DenoiseViewModel(MainViewModel main)
        : base(main, new HashSet<MediaKind> { MediaKind.Video, MediaKind.Audio }, MediaKind.Video)
    {
        Output.Options.SubfolderName = "denoised";
        Denoise.PropertyChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(VideoOn));
            OnPropertyChanged(nameof(AudioOn));
            OnPropertyChanged(nameof(GpuHint));
            StartCommand.NotifyCanExecuteChanged();
        };
    }

    public override string TitleKey => "NavDenoise";

    public override string IconKey => "IconSparkle";

    public DenoiseOptions Denoise { get; } = new();

    public VideoDenoise[] VideoModes { get; } = Enum.GetValues<VideoDenoise>();

    public AudioDenoise[] AudioModes { get; } = Enum.GetValues<AudioDenoise>();

    public bool VideoOn => Denoise.Video != VideoDenoise.Off;

    public bool AudioOn => Denoise.Audio != AudioDenoise.Off;

    /// <summary>nlmeans_vulkan exists only in FFmpeg builds with Vulkan filters.</summary>
    public bool GpuHint => Denoise.Video == VideoDenoise.QualityGpu;

    protected override bool ExtraCanStart() => VideoOn || AudioOn;

    protected override Task<List<ConversionJob>> PlanAsync() =>
        Task.FromResult(Main.Conversion.Denoise(Sources.Refs.ToList(), Denoise, Output.Options));

    public override void SaveState(Dictionary<string, JsonElement> pages) => Save(pages, StateKey, Denoise, Output.Options, Sources.Folder);

    public override void LoadState(Dictionary<string, JsonElement> pages) => Load(pages, StateKey, Denoise, Output.Options, Sources.Folder);
}

// ==================================================================== Tools › Interpolate frames

/// <summary>Smoother motion (2–4× frame rate) or slow motion: RIFE module or FFmpeg minterpolate.</summary>
public sealed partial class InterpolateViewModel : ConvertPageViewModel
{
    public InterpolateViewModel(MainViewModel main) : base(main, new HashSet<MediaKind> { MediaKind.Video }, MediaKind.Video)
    {
        Output.Options.SubfolderName = "interpolated";
        Interpolate.PropertyChanged += (_, _) => RaiseModule();
        main.Modules.Changed += () => Dispatcher.UIThread.Post(RaiseModule);
        Sources.Changed += () => OnPropertyChanged(nameof(ResultText));
    }

    public override string TitleKey => "NavInterpolate";

    public override string IconKey => "IconFps";

    public InterpolateOptions Interpolate { get; } = new();

    public InterpolationEngine[] Engines { get; } = Enum.GetValues<InterpolationEngine>();

    public int[] Factors { get; } = [2, 3, 4];

    public bool ModuleMissing => Interpolate.Engine == InterpolationEngine.Rife && !Main.Modules.IsAvailable(ModuleId.Rife);

    public string ModuleMissingText => Loc.F("HintModuleMissing", ModuleCatalog.Rife.Name);

    /// <summary>"30 fps → 60 fps" for the first video.</summary>
    public string ResultText
    {
        get
        {
            var fps = Sources.Items.FirstOrDefault()?.Info?.MainVideo?.Fps ?? 0;
            if (fps <= 0)
            {
                return "";
            }

            var factor = Math.Clamp(Interpolate.Factor, 2, 4);
            return Interpolate.SlowMotion
                ? Loc.F("InterpolateSlowResult", factor, $"{fps:0.##}")
                : Loc.F("InterpolateFpsResult", $"{fps:0.##}", $"{fps * factor:0.##}");
        }
    }

    protected override bool ExtraCanStart() => !ModuleMissing;

    private void RaiseModule()
    {
        OnPropertyChanged(nameof(ModuleMissing));
        OnPropertyChanged(nameof(ModuleMissingText));
        OnPropertyChanged(nameof(ResultText));
        StartCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void OpenDependencies() => Main.NavigateTo(Main.Dependencies);

    [RelayCommand]
    private async Task InstallModuleAsync()
    {
        await Main.Dependencies.InstallModuleAsync(ModuleId.Rife);
        RaiseModule();
    }

    protected override Task<List<ConversionJob>> PlanAsync() =>
        Task.FromResult(Main.Conversion.Interpolate(Sources.Refs.ToList(), Interpolate, Output.Options));

    public override void SaveState(Dictionary<string, JsonElement> pages) => Save(pages, StateKey, Interpolate, Output.Options, Sources.Folder);

    public override void LoadState(Dictionary<string, JsonElement> pages) => Load(pages, StateKey, Interpolate, Output.Options, Sources.Folder);

    public override void OnActivated()
    {
        base.OnActivated();
        RaiseModule();
    }
}
