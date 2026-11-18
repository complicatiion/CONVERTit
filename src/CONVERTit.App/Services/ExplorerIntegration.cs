using CONVERTit.App.Localization;
using CONVERTit.Core.Media;
using CONVERTit.Platform.Windows;

namespace CONVERTit.App.Services;

/// <summary>
/// "CONVERTit" submenu in the classic Explorer context menu, separate for videos, audio files and images.
/// Action ids are handled by <see cref="ViewModels.MainViewModel.QueueExternal"/>.
/// </summary>
public static class ExplorerIntegration
{
    public const string Open = "open";
    public const string Compress = "compress";
    public const string VideoMp4 = "video-mp4";
    public const string ExtractAudio = "extract-audio";
    public const string AudioMp3 = "audio-mp3";
    public const string AudioOgg = "audio-ogg";
    public const string ImagePng = "image-png";
    public const string ImageJpg = "image-jpg";
    public const string ImageWebp = "image-webp";
    public const string EditTags = "tags";
    public const string VideoTrim = "trim";
    public const string VideoGif = "gif";
    public const string Remux = "remux";
    public const string AudioTrim = "audio-trim";
    public const string ImageResize = "resize";
    public const string MakeIcon = "icon";
    public const string MakeAppIcon = "app-icon";
    public const string IcoExtract = "ico-extract";
    public const string Thumbcache = "thumbcache";

    /// <summary>Thumbs.db and thumbcache_*.db only (Windows Search filter on the .db verbs).</summary>
    private const string ThumbcacheFilter = "System.FileName:~<\"thumbcache_\" OR System.FileName:=\"Thumbs.db\"";

    private static IEnumerable<string> AllExtensions =>
        MediaTypes.VideoExtensions.Concat(MediaTypes.AudioExtensions).Concat(MediaTypes.ImageExtensions).Append("db");

    /// <summary>Applies the setting (also after an update, when the program path or the language changed).</summary>
    public static void Refresh(AppSettings settings)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            if (settings.ExplorerMenu)
            {
                Register();
            }
            else if (AllExtensions.Any(ExplorerMenu.IsRegistered))
            {
                ExplorerMenu.Unregister(AllExtensions);
            }
        }
        catch
        {
            // Registry not writable: the menu stays as it is.
        }
    }

    public static void Register()
    {
        ExplorerVerb Verb(string id, string key) => new(id, Loc.T(key));
        // .ico gets its own submenu (export the sizes it contains); Thumbs.db / thumbcache_*.db open in the viewer.
        var images = MediaTypes.ImageExtensions.Where(e => e != "ico").ToList();
        var groups = new[]
        {
            new ExplorerMenuGroup(MediaTypes.VideoExtensions,
            [
                Verb(Open, "MenuOpen"), Verb(VideoMp4, "MenuVideoMp4"), Verb(Compress, "MenuCompress"), Verb(VideoTrim, "MenuTrim"),
                Verb(VideoGif, "MenuGif"), Verb(Remux, "MenuRemux"), Verb(ExtractAudio, "MenuExtractAudio")
            ]),
            new ExplorerMenuGroup(MediaTypes.AudioExtensions,
            [
                Verb(Open, "MenuOpen"), Verb(AudioMp3, "MenuAudioMp3"), Verb(AudioOgg, "MenuAudioOgg"), Verb(EditTags, "MenuEditTags"),
                Verb(AudioTrim, "MenuTrim"), Verb(Compress, "MenuCompress")
            ]),
            new ExplorerMenuGroup(images,
            [
                Verb(Open, "MenuOpen"), Verb(ImagePng, "MenuImagePng"), Verb(ImageJpg, "MenuImageJpg"), Verb(ImageWebp, "MenuImageWebp"),
                Verb(ImageResize, "MenuResize"), Verb(Compress, "MenuCompress"), Verb(MakeIcon, "MenuMakeIcon"), Verb(MakeAppIcon, "MenuMakeAppIcon")
            ]),
            new ExplorerMenuGroup(["ico"],
            [
                Verb(IcoExtract, "MenuIcoExtract"), Verb(Open, "MenuOpen"), Verb(ImagePng, "MenuImagePng")
            ]),
            new ExplorerMenuGroup(["db"], [Verb(Thumbcache, "MenuThumbcache")], ThumbcacheFilter)
        };
        ExplorerMenu.Register(AppEnvironment.ExecutablePath, Loc.T("MenuRoot"), groups);
    }

    public static void Unregister() => ExplorerMenu.Unregister(AllExtensions);
}
