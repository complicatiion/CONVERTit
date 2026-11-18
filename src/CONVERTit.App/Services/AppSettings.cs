using System.Text.Json;
using System.Text.Json.Serialization;
using CONVERTit.Core.Options;
using CONVERTit.Core.Storage;

namespace CONVERTit.App.Services;

public enum ThemeOption { Dark, Light, System }

public enum PriorityOption { Normal, BelowNormal, Idle }

/// <summary>UI size: Auto follows the screen; the others are fixed scale steps (tablets … 30″+ monitors).</summary>
public enum DisplayProfile { Auto, ExtraSmall, Small, Medium, Large, ExtraLarge }

/// <summary>Images with real transparency and a JPEG target: keep PNG (default), ask, or convert anyway.</summary>
public enum TransparencyPolicy { KeepPng, Ask, ToJpeg }

/// <summary>Settings of CONVERTit (data folder\settings.json), including the last used options of every page.</summary>
public sealed class AppSettings
{
    public string Language { get; set; } = "en";

    /// <summary>True when no settings file existed (first start): language is detected, FFmpeg setup is offered.</summary>
    [JsonIgnore]
    public bool IsNew { get; private set; }

    public DisplayProfile DisplayProfile { get; set; } = DisplayProfile.Auto;

    /// <summary>Start maximized (default); switch in Settings › Appearance.</summary>
    public bool StartMaximized { get; set; } = true;

    /// <summary>Collapsed sidebar: only one group open at a time (accordion); off = groups open independently.</summary>
    public bool SidebarAccordion { get; set; } = true;

    /// <summary>Audio lists with the mini player switched on (Tags by default).</summary>
    public List<string> MiniPlayerOn { get; set; } = ["Tags"];

    /// <summary>Tag editor: ID3v2 version for MP3/WAV/AIFF (v2.3 default), extra ID3v1, keep file dates, cover size.</summary>
    public Core.Tags.Id3Version Id3Version { get; set; } = Core.Tags.Id3Version.V23;

    public bool WriteId3v1 { get; set; }

    public bool TagsKeepFileDate { get; set; } = true;

    public bool TagsResizeCover { get; set; } = true;

    public string TagPattern { get; set; } = Core.Tags.TagPattern.DefaultPattern;

    /// <summary>"Later" in the FFmpeg setup dialog: do not offer it again on start.</summary>
    public bool FfmpegSetupDismissed { get; set; }

    /// <summary>Classic Explorer context menu ("CONVERTit" submenu) is registered.</summary>
    public bool ExplorerMenu { get; set; }

    /// <summary>Compress profiles saved by the user.</summary>
    public List<CompressProfile> CompressProfiles { get; set; } = [];

    public ThemeOption Theme { get; set; } = ThemeOption.Dark;

    /// <summary>ffmpeg.exe or its folder chosen by the user; empty = automatic.</summary>
    public string FfmpegPath { get; set; } = "";

    /// <summary>Look for a new CONVERTit release on GitHub (opt-in, off by default; at most once a day at start).</summary>
    public bool CheckForUpdates { get; set; }

    /// <summary>Last automatic update check (UTC).</summary>
    public DateTime? LastUpdateCheck { get; set; }

    /// <summary>Own folders of optional modules (key: module id, e.g. "RealEsrgan").</summary>
    public Dictionary<string, string> ModuleFolders { get; set; } = [];

    /// <summary>Parallel FFmpeg jobs (video/audio).</summary>
    public int MediaParallel { get; set; } = 1;

    /// <summary>Parallel image jobs.</summary>
    public int ImageParallel { get; set; } = Math.Clamp(Environment.ProcessorCount / 2, 1, 8);

    public PriorityOption Priority { get; set; } = PriorityOption.BelowNormal;

    /// <summary>Keep the PC awake while the queue runs.</summary>
    public bool PreventSleep { get; set; } = true;

    public bool TaskbarProgress { get; set; } = true;

    /// <summary>Windows notification when a batch is finished (off by default; in-app toasts are always shown).</summary>
    public bool SystemNotifications { get; set; }

    /// <summary>Small notifications in the window (bottom right).</summary>
    public bool InAppNotifications { get; set; } = true;

    /// <summary>Open the output folder when a batch is finished.</summary>
    public bool OpenFolderWhenDone { get; set; }

    /// <summary>Remember the options of every page.</summary>
    public bool RememberSettings { get; set; } = true;

    /// <summary>File types CONVERTit registers for ("Open with", Default apps).</summary>
    public AssociationMode Associations { get; set; } = AssociationMode.Off;

    /// <summary>Own list for <see cref="AssociationMode.Custom"/>, e.g. "mkv, flac, webp".</summary>
    public string CustomAssociations { get; set; } = "";

    /// <summary>Small "i" hints below options (Settings › General; installer option for new users).</summary>
    public bool ShowHints { get; set; } = true;

    /// <summary>Quick Convert / image targets: what happens with images that have real transparency.</summary>
    public TransparencyPolicy TransparentImages { get; set; } = TransparencyPolicy.KeepPng;

    /// <summary>Thumbnails in the file lists (Windows thumbnail cache, own cache as fallback).</summary>
    public bool ListThumbnails { get; set; } = true;

    /// <summary>Performance profile (Auto follows power source and hardware).</summary>
    public Core.Machine.PerformanceProfile PerformanceProfile { get; set; } = Core.Machine.PerformanceProfile.Auto;

    /// <summary>Decode video sources on the GPU ("-hwaccel auto").</summary>
    public bool HardwareDecode { get; set; } = true;

    /// <summary>NVIDIA/Intel: whole pipeline on the GPU for plain jobs (Balanced and Maximum profiles).</summary>
    public bool GpuTurbo { get; set; } = true;

    /// <summary>HDR sources are tone mapped to SDR (unless the target keeps 10-bit HDR).</summary>
    public bool HdrToSdr { get; set; } = true;

    /// <summary>Interlaced sources are deinterlaced.</summary>
    public bool AutoDeinterlace { get; set; } = true;

    // ---------------------------------------------------------------- Background & notification area (tray)

    /// <summary>Minimizing hides the window to the notification area.</summary>
    public bool MinimizeToTray { get; set; }

    /// <summary>Closing hides the window to the notification area instead of exiting (off by default).</summary>
    public bool CloseToTray { get; set; }

    /// <summary>Started with Windows: stay in the notification area instead of opening the window.</summary>
    public bool StartInTray { get; set; } = true;

    /// <summary>Explorer actions like "Convert to MP3" run without opening the window.</summary>
    public bool BackgroundExplorerJobs { get; set; } = true;

    /// <summary>Exit when the background jobs are done (only when CONVERTit was started for them).</summary>
    public bool ExitAfterBackgroundJobs { get; set; } = true;

    /// <summary>The "keeps running in the notification area" hint was shown once.</summary>
    public bool TrayHintShown { get; set; }

    /// <summary>Hardware encoders: off by default; only tested encoders can be chosen.</summary>
    public bool UseHardwareEncoders { get; set; } = true;

    public bool SidebarCollapsed { get; set; }

    public List<string> CollapsedGroups { get; set; } = [];

    // Encoder test cache (re-run when the FFmpeg version changes).
    public string EncoderFfmpegVersion { get; set; } = "";

    public List<string> Encoders { get; set; } = [];

    public List<string> WorkingHardwareEncoders { get; set; } = [];

    public string LastFolder { get; set; } = "";

    /// <summary>Saved options per page ("VideoConvert" → JSON).</summary>
    public Dictionary<string, JsonElement> Pages { get; set; } = [];

    public static AppSettings Load()
    {
        try
        {
            return JsonFile.Read<AppSettings>(AppEnvironment.SettingsPath) ?? FromInstallerDefaults();
        }
        catch
        {
            // Unreadable file: start with defaults, but it is not a first start.
            return new AppSettings();
        }
    }

    /// <summary>
    /// First start: the installer can leave defaults.json next to CONVERTit.exe (e.g. "ShowHints": false); every new
    /// user starts with these values. Missing or unreadable → plain defaults.
    /// </summary>
    private static AppSettings FromInstallerDefaults()
    {
        AppSettings? defaults = null;
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "defaults.json");
            if (File.Exists(path))
            {
                defaults = JsonFile.Read<AppSettings>(path);
            }
        }
        catch
        {
            // Ignore broken defaults.
        }

        defaults ??= new AppSettings();
        defaults.IsNew = true;
        return defaults;
    }

    public void Save()
    {
        try
        {
            JsonFile.Write(AppEnvironment.SettingsPath, this);
        }
        catch
        {
            // Not critical.
        }
    }
}
