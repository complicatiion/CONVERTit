using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CONVERTit.App.Localization;
using CONVERTit.Core.Catalog;
using CONVERTit.Core.Estimation;
using CONVERTit.Core.Ffmpeg;
using CONVERTit.Core.Jobs;
using CONVERTit.Core.Media;
using CONVERTit.Core.Options;
using CONVERTit.Core.Storage;

namespace CONVERTit.App.ViewModels;

/// <summary>Copies all public read/write properties (option snapshots, profiles, presets).</summary>
public static class OptionCopy
{
    public static void Copy<T>(T source, T target) where T : class
    {
        foreach (var property in typeof(T).GetProperties())
        {
            if (property.CanRead && property.CanWrite && property.GetIndexParameters().Length == 0)
            {
                property.SetValue(target, property.GetValue(source));
            }
        }
    }
}

// ==================================================================== Compress

/// <summary>Entry of the profile list (built-in profiles are translated).</summary>
public sealed class ProfileItem(CompressProfile profile) : ObservableObject
{
    public CompressProfile Profile { get; } = profile;

    public string Label => Profile.NameKey is { } key ? Loc.T(key) : Profile.Name;

    public bool IsBuiltIn => Profile.IsBuiltIn;

    public void Refresh() => OnPropertyChanged(nameof(Label));
}

/// <summary>
/// Compress: smaller videos (target size, share of the original or quality), audio files and images.
/// Profiles: built-in ones plus profiles saved by the user.
/// </summary>
public sealed partial class CompressViewModel : ConvertPageViewModel
{
    private bool _applyingProfile;

    public CompressViewModel(MainViewModel main)
        : base(main, new HashSet<MediaKind> { MediaKind.Video, MediaKind.Audio, MediaKind.Image }, MediaKind.Unknown)
    {
        Output.Options.SubfolderName = "compressed";
        Options.PropertyChanged += OnOptionChanged;
        main.EncodersChanged += RebuildChoices;
        RebuildProfiles();
        RebuildChoices();
    }

    public override string TitleKey => "NavCompress";

    public override string IconKey => "IconCompress";

    /// <summary>Images work without FFmpeg; video/audio jobs check it themselves.</summary>
    public override bool NeedsFfmpeg => false;

    public CompressOptions Options { get; } = new();

    public ObservableCollection<ProfileItem> Profiles { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteProfileCommand))]
    private ProfileItem? _selectedProfile;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveProfileCommand))]
    private string _newProfileName = "";

    public CompressMode[] Modes => Choices.CompressModes;
    public CompressStrength[] Strengths => Choices.CompressStrengths;
    public ObservableCollection<VideoCodec> Codecs { get; } = [];
    public ObservableCollection<Acceleration> Accelerations { get; } = [];
    public ResolutionPreset[] MaxResolutions => Choices.MaxResolutions;
    public FrameRatePreset[] MaxFrameRates => Choices.MaxFrameRates;
    public int[] TargetSizes => Choices.TargetSizesMb;
    public int[] Percents => Choices.Percents;
    public int[] VideoAudioBitrates => Choices.CompressAudioKbps;
    public AudioFormat[] AudioFormats { get; } = [AudioFormat.Mp3, AudioFormat.Aac, AudioFormat.Opus];
    public int[] AudioBitrates { get; } = [256, 192, 160, 128, 96, 64];
    public CompressImageFormat[] ImageFormats => Choices.CompressImageFormats;
    public int[] ImageMaxSides => Choices.ImageMaxSides;

    public bool IsTargetSize => Options.Mode == CompressMode.TargetSize;
    public bool IsPercent => Options.Mode == CompressMode.Percent;
    public bool IsQuality => Options.Mode == CompressMode.Quality;
    public bool HasHardware => Accelerations.Count > 1;

    private void OnOptionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CompressOptions.Codec))
        {
            RebuildChoices();
        }

        if (e.PropertyName == nameof(CompressOptions.Mode))
        {
            OnPropertyChanged(nameof(IsTargetSize));
            OnPropertyChanged(nameof(IsPercent));
            OnPropertyChanged(nameof(IsQuality));
        }

        UpdateEstimate();
    }

    /// <summary>Codecs with an encoder in this FFmpeg build; hardware encoders that passed the test.</summary>
    private void RebuildChoices()
    {
        var codecs = new[] { VideoCodec.H264, VideoCodec.H265, VideoCodec.Av1 }
            .Where(c => Main.HasEncoder(FormatCatalog.VideoEncoder(c, Acceleration.Off)!)).ToList();
        Choices.Sync(Codecs, codecs.Count > 0 ? codecs : [VideoCodec.H264]);
        if (!Codecs.Contains(Options.Codec))
        {
            Options.Codec = Codecs[0];
        }

        var accelerations = Enum.GetValues<Acceleration>()
            .Where(a => a == Acceleration.Off || (Main.Settings.UseHardwareEncoders
                && FormatCatalog.VideoEncoder(Options.Codec, a) is { } enc && Main.HardwareWorks(enc)))
            .ToList();
        Choices.Sync(Accelerations, accelerations);
        if (!Accelerations.Contains(Options.Acceleration))
        {
            Options.Acceleration = Acceleration.Off;
        }

        OnPropertyChanged(nameof(HasHardware));
    }

    private void RebuildProfiles()
    {
        var selected = SelectedProfile?.Profile.Name;
        Profiles.Clear();
        foreach (var profile in CompressProfile.BuiltIn.Concat(Main.Settings.CompressProfiles))
        {
            Profiles.Add(new ProfileItem(profile));
        }

        _applyingProfile = true;
        SelectedProfile = Profiles.FirstOrDefault(p => p.Profile.Name == selected);
        _applyingProfile = false;
    }

    partial void OnSelectedProfileChanged(ProfileItem? value)
    {
        if (_applyingProfile || value is null)
        {
            return;
        }

        OptionCopy.Copy(value.Profile.Options, Options);
        if (!value.IsBuiltIn)
        {
            NewProfileName = value.Profile.Name;
        }
    }

    private bool CanSaveProfile() => NewProfileName.Trim().Length > 0;

    [RelayCommand(CanExecute = nameof(CanSaveProfile))]
    private void SaveProfile()
    {
        var name = NewProfileName.Trim();
        var profile = new CompressProfile { Name = name, Options = JsonFile.Clone(Options) };
        Main.Settings.CompressProfiles.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        Main.Settings.CompressProfiles.Add(profile);
        Main.Settings.Save();
        RebuildProfiles();
        _applyingProfile = true;
        SelectedProfile = Profiles.FirstOrDefault(p => !p.IsBuiltIn && p.Profile.Name == name);
        _applyingProfile = false;
        Main.ShowToast(Loc.F("ToastProfileSaved", name));
    }

    private bool CanDeleteProfile() => SelectedProfile is { IsBuiltIn: false };

    [RelayCommand(CanExecute = nameof(CanDeleteProfile))]
    private void DeleteProfile()
    {
        if (SelectedProfile is not { IsBuiltIn: false } item)
        {
            return;
        }

        Main.Settings.CompressProfiles.Remove(item.Profile);
        Main.Settings.Save();
        SelectedProfile = null;
        NewProfileName = "";
        RebuildProfiles();
        Main.ShowToast(Loc.F("ToastProfileDeleted", item.Profile.Name));
    }

    protected override Task<List<ConversionJob>> PlanAsync() =>
        Task.FromResult(Main.Conversion.Compress(Sources.Refs.ToList(), Options, Output.Options));

    protected override long? Estimate(SourceItem item) => item.Info is { } info ? CompressPlanner.Estimate(info, Options) : null;

    public override void SaveState(Dictionary<string, JsonElement> pages) => Save(pages, StateKey, Options, Output.Options, Sources.Folder);

    public override void LoadState(Dictionary<string, JsonElement> pages)
    {
        Load(pages, StateKey, Options, Output.Options, Sources.Folder);
        RebuildChoices();
    }

    public override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        foreach (var profile in Profiles)
        {
            profile.Refresh();
        }
    }
}

// ==================================================================== Image › Game textures

/// <summary>Saved preset of the texture page.</summary>
public sealed class TextureState
{
    public TexturePreset Preset { get; set; } = TexturePreset.Unity;

    public int MaxSize { get; set; }
}

/// <summary>Textures for Unity, Godot and DirectX engines: TGA, DDS (BC1/BC3, mipmaps), PNG, power-of-two sizes.</summary>
public sealed partial class GameTextureViewModel : ConvertPageViewModel
{
    private bool _applying;

    public GameTextureViewModel(MainViewModel main) : base(main, new HashSet<MediaKind> { MediaKind.Image }, MediaKind.Image)
    {
        Image = new ImageOptionsViewModel(allowKeep: false);
        Output.Options.SubfolderName = "textures";
        Image.Changed += () =>
        {
            if (!_applying && Preset != TexturePreset.Custom)
            {
                _applying = true;
                Preset = TexturePreset.Custom;
                _applying = false;
            }
        };
        ApplyPreset(TexturePreset.Unity);
    }

    public override string TitleKey => "NavTextures";

    public override string IconKey => "IconTexture";

    public override bool NeedsFfmpeg => false;

    public ImageOptionsViewModel Image { get; }

    public TexturePreset[] Presets => Choices.TexturePresets;

    public int[] MaxSizes => Choices.TextureSizes;

    [ObservableProperty] private TexturePreset _preset = TexturePreset.Unity;

    /// <summary>Longest side in pixels (0 = keep).</summary>
    [ObservableProperty] private int _maxSize;

    partial void OnPresetChanged(TexturePreset value)
    {
        if (!_applying && value != TexturePreset.Custom)
        {
            ApplyPreset(value);
        }
    }

    partial void OnMaxSizeChanged(int value)
    {
        _applying = true;
        Image.Options.Resize = value > 0 ? ResizeMode.MaxSize : ResizeMode.Off;
        Image.Options.Width = value;
        Image.Options.Height = value;
        Image.Options.NoUpscale = true;
        _applying = false;
    }

    private void ApplyPreset(TexturePreset preset)
    {
        _applying = true;
        var o = Image.Options;
        o.KeepTransparency = true;
        o.StripMetadata = true;
        o.AutoOrient = true;
        switch (preset)
        {
            case TexturePreset.Unity:
                o.Format = ImageFormat.Tga;
                o.TgaRle = true;
                o.PowerOfTwo = PowerOfTwo.Nearest;
                break;
            case TexturePreset.Unreal:
                // Unreal imports TGA/PNG and builds its own compression and mipmaps; power-of-two sizes are required for mips and streaming.
                o.Format = ImageFormat.Tga;
                o.TgaRle = false;
                o.PowerOfTwo = PowerOfTwo.Nearest;
                break;
            case TexturePreset.Godot:
                o.Format = ImageFormat.Png;
                o.PngCompression = 9;
                o.PngPalette = false;
                o.PowerOfTwo = PowerOfTwo.Off;
                break;
            case TexturePreset.DirectX:
                o.Format = ImageFormat.Dds;
                o.DdsCompression = DdsCompression.Bc3;
                o.DdsMipmaps = true;
                o.PowerOfTwo = PowerOfTwo.Nearest;
                break;
            case TexturePreset.Legacy:
                o.Format = ImageFormat.Dds;
                o.DdsCompression = DdsCompression.Bc1;
                o.DdsMipmaps = true;
                o.PowerOfTwo = PowerOfTwo.Nearest;
                break;
        }

        _applying = false;
    }

    protected override Task<List<ConversionJob>> PlanAsync() =>
        Task.FromResult(Main.Conversion.ImageConvert(Sources.Refs.ToList(), Image.Options, Output.Options, skipSame: false, "OpTexture"));

    public override void SaveState(Dictionary<string, JsonElement> pages) =>
        Save(pages, StateKey, Image.Options, Output.Options, Sources.Folder, new TextureState { Preset = Preset, MaxSize = MaxSize });

    public override void LoadState(Dictionary<string, JsonElement> pages)
    {
        var state = new TextureState();
        _applying = true;
        Load(pages, StateKey, Image.Options, Output.Options, Sources.Folder, state);
        Preset = state.Preset;
        MaxSize = state.MaxSize;
        _applying = false;
    }
}

// ==================================================================== Audio › Game audio

/// <summary>Saved preset of the game audio page.</summary>
public sealed class GameAudioState
{
    public GameAudioPreset Preset { get; set; } = GameAudioPreset.UnitySfx;
}

/// <summary>Sound effects and music for Unity and Godot: WAV/OGG, mono, sample rate, loudness, silence trimming.</summary>
public sealed partial class GameAudioViewModel : ConvertPageViewModel
{
    private bool _applying;

    public GameAudioViewModel(MainViewModel main)
        : base(main, new HashSet<MediaKind> { MediaKind.Audio, MediaKind.Video }, MediaKind.Audio, miniPlayerKey: "GameAudio")
    {
        Audio = new AudioOptionsViewModel(main);
        Output.Options.SubfolderName = "game-audio";
        Audio.Changed += () =>
        {
            UpdateEstimate();
            if (!_applying && Preset != GameAudioPreset.Custom)
            {
                _applying = true;
                Preset = GameAudioPreset.Custom;
                _applying = false;
            }
        };
        ApplyPreset(GameAudioPreset.UnitySfx);
    }

    public override string TitleKey => "NavGameAudio";

    public override string IconKey => "IconGamepad";

    public AudioOptionsViewModel Audio { get; }

    public GameAudioPreset[] Presets => Choices.GameAudioPresets;

    [ObservableProperty] private GameAudioPreset _preset = GameAudioPreset.UnitySfx;

    partial void OnPresetChanged(GameAudioPreset value)
    {
        if (!_applying && value != GameAudioPreset.Custom)
        {
            ApplyPreset(value);
        }
    }

    private void ApplyPreset(GameAudioPreset preset)
    {
        _applying = true;
        var o = Audio.Options;
        var music = preset is GameAudioPreset.UnityMusic or GameAudioPreset.GodotMusic or GameAudioPreset.UnrealMusic;
        var unreal = preset is GameAudioPreset.UnrealSfx or GameAudioPreset.UnrealMusic;
        // Unreal imports WAV only (it compresses itself); music stays stereo WAV at 48 kHz.
        o.Format = unreal || !music ? AudioFormat.Wav : AudioFormat.Vorbis;
        o.WavDepth = WavDepth.Pcm16;
        o.VorbisQuality = 6;
        o.SampleRate = unreal ? 48000 : 44100;
        o.Channels = music ? ChannelMode.Stereo : ChannelMode.Mono;
        o.Loudness = music ? LoudnessTarget.Lufs16 : LoudnessTarget.Off;
        o.TrimSilence = !music;
        o.KeepMetadata = false;
        o.KeepCover = false;
        Audio.Rebuild();
        _applying = false;
    }

    protected override Task<List<ConversionJob>> PlanAsync() =>
        Task.FromResult(Main.Conversion.AudioConvert(Sources.Refs.ToList(), Audio.Options, Output.Options, skipSame: false));

    protected override long? Estimate(SourceItem item) =>
        item.Info is { } info ? SizeEstimator.Audio(info, Audio.Options) : null;

    public override void SaveState(Dictionary<string, JsonElement> pages) =>
        Save(pages, StateKey, Audio.Options, Output.Options, Sources.Folder, new GameAudioState { Preset = Preset });

    public override void LoadState(Dictionary<string, JsonElement> pages)
    {
        var state = new GameAudioState();
        _applying = true;
        Load(pages, StateKey, Audio.Options, Output.Options, Sources.Folder, state);
        Preset = state.Preset;
        Audio.Rebuild();
        _applying = false;
    }

    public override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        Audio.RefreshTexts();
    }
}
