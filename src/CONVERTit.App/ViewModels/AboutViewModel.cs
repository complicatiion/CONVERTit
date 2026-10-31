using CommunityToolkit.Mvvm.Input;
using CONVERTit.App.Localization;
using CONVERTit.App.Services;

namespace CONVERTit.App.ViewModels;

public sealed record ThirdPartyItem(string Name, string Version, string License, string UsageKey, string Url, string LicenseFile)
{
    public string Usage => Loc.T(UsageKey);
}

/// <summary>About page: version, license, components.</summary>
public sealed partial class AboutViewModel(MainViewModel main) : PageViewModel(main)
{
    public override string TitleKey => "NavAbout";

    public override string IconKey => "IconInfo";

    public string Version => "v" + AppEnvironment.Version;

    public string Copyright => "Copyright © 2026 Sven Klocke · sksdesign | complicatiion";

    public IReadOnlyList<ThirdPartyItem> ThirdParty { get; } =
    [
        new("FFmpeg", "download", "LGPL-2.1+ / GPL-3.0 (build)", "UsageFfmpeg", "https://ffmpeg.org", "third-party/FFMPEG.md"),
        new("ImageMagick · Magick.NET", "14.x", "ImageMagick License · Apache-2.0", "UsageMagick", "https://github.com/dlemstra/Magick.NET", "third-party/MAGICK-NET.md"),
        new("LibVLC · LibVLCSharp", "3.0 · 3.9", "LGPL-2.1+", "UsageVlc", "https://code.videolan.org/videolan/LibVLCSharp", "third-party/LIBVLC.md"),
        new("TagLib# (TagLibSharp)", "2.3", "LGPL-2.1", "UsageTaglib", "https://github.com/mono/taglib-sharp", "third-party/TAGLIBSHARP.md"),
        new(".NET", "10.0", "MIT", "UsageDotnet", "https://github.com/dotnet/runtime", "third-party/DOTNET.md"),
        new("Avalonia UI", "11.3", "MIT", "UsageAvalonia", "https://avaloniaui.net", "third-party/AVALONIA.md"),
        new("SkiaSharp / HarfBuzzSharp", "–", "MIT · BSD-3-Clause", "UsageSkia", "https://github.com/mono/SkiaSharp", "third-party/SKIASHARP.md"),
        new("CommunityToolkit.Mvvm", "8.4", "MIT", "UsageMvvm", "https://github.com/CommunityToolkit/dotnet", "third-party/COMMUNITYTOOLKIT-MVVM.md"),
        new("Material Design Icons", "7.4", "Apache-2.0", "UsageIcons", "https://pictogrammers.com/library/mdi/", "third-party/MATERIAL-DESIGN-ICONS.md"),
        new("Inter", "4.x", "SIL OFL 1.1", "UsageFont", "https://rsms.me/inter/", "third-party/INTER-FONT.md")
    ];

    [RelayCommand]
    private void OpenProject() => AppEnvironment.OpenUrl(AppEnvironment.ProjectUrl);

    [RelayCommand]
    private void OpenWebsite() => AppEnvironment.OpenUrl(AppEnvironment.WebsiteUrl);

    [RelayCommand]
    private void OpenMitLicense() => AppEnvironment.OpenUrl(AppEnvironment.MitLicenseUrl);

    [RelayCommand]
    private void OpenLicensesFolder() => AppEnvironment.OpenFolder(AppEnvironment.LicensesDirectory);

    [RelayCommand]
    private void OpenLink(string? url) => AppEnvironment.OpenUrl(url);

    [RelayCommand]
    private Task ShowLicenseAsync(string? file) => ShowFileAsync(Loc.T("HdrLicense"), file ?? "LICENSE.md");

    [RelayCommand]
    private Task ShowThirdPartyAsync(ThirdPartyItem? item) =>
        item is null ? Task.CompletedTask : ShowFileAsync(item.Name, item.LicenseFile);

    [RelayCommand]
    private Task ShowDocumentAsync(string? file) => ShowFileAsync(Path.GetFileNameWithoutExtension(file ?? ""), file ?? "README.md");

    private Task ShowFileAsync(string title, string relativePath)
    {
        var path = Path.Combine(AppEnvironment.LicensesDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
        string text;
        try
        {
            text = File.Exists(path) ? File.ReadAllText(path) : Loc.F("LicenseFileMissing", path);
        }
        catch (Exception ex)
        {
            text = ex.Message;
        }

        return Host.ShowTextAsync(title, text);
    }
}
