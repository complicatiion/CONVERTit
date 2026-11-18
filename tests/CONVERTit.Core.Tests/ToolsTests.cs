using CONVERTit.Core.Ffmpeg;
using CONVERTit.Core.Options;
using CONVERTit.Engines.Ffmpeg;
using Xunit;

namespace CONVERTit.Core.Tests;

public class ToolsTests
{
    [Fact]
    public void Archive_root_folder_is_stripped_only_when_shared()
    {
        Assert.Equal("pkg/", ModuleInstaller.CommonRoot(["pkg/a.exe", "pkg/models/x.bin"]));
        Assert.Equal("", ModuleInstaller.CommonRoot(["a.exe", "models/x.bin"]));
        Assert.Equal("", ModuleInstaller.CommonRoot(["one/a.exe", "two/b.exe"]));
    }

    [Fact]
    public void Github_digest_and_percent_lines_are_parsed()
    {
        Assert.Equal("abc123", ModuleInstaller.ParseDigest("sha256:ABC123"));
        Assert.Null(ModuleInstaller.ParseDigest("sha1:abc"));
        Assert.Null(ModuleInstaller.ParseDigest(null));
        Assert.Equal(12.5, ModuleRunner.Percent("12.50%"));
        Assert.Equal(7.25, ModuleRunner.Percent(" 7,25% "));
        Assert.Null(ModuleRunner.Percent("[0 NVIDIA GeForce] queue=0"));
    }

    [Fact]
    public void Modules_have_pinned_releases_and_licenses()
    {
        foreach (var module in ModuleCatalog.Optional)
        {
            Assert.True(module.CanDownload);
            Assert.NotEqual("latest", module.Tag);
            Assert.False(string.IsNullOrEmpty(module.License));
            Assert.StartsWith(module.Folder == "realesrgan" ? "realesrgan" : module.Folder, module.Executable);
        }
    }

    [Fact]
    public void Denoise_filters_scale_with_strength()
    {
        Assert.Equal("", FfmpegCommands.DenoiseVideoFilter(VideoDenoise.Off, 5));
        Assert.StartsWith("hqdn3d=", FfmpegCommands.DenoiseVideoFilter(VideoDenoise.Fast, 5));
        Assert.Contains("nlmeans_vulkan", FfmpegCommands.DenoiseVideoFilter(VideoDenoise.QualityGpu, 5));
        Assert.Equal("afftdn=nr=19:nf=-40:tn=1", FfmpegCommands.DenoiseAudioFilter(AudioDenoise.Spectral, 5));
        Assert.Equal(FfmpegCommands.DenoiseAudioFilter(AudioDenoise.Spectral, 10), FfmpegCommands.DenoiseAudioFilter(AudioDenoise.Spectral, 99));
    }

    [Fact]
    public void Frames_are_reassembled_with_source_audio()
    {
        var info = new Media.MediaInfo { Path = "in.mp4", DurationSeconds = 10 };
        var cmd = FfmpegCommands.FromFrames("f/%08d.png", 23.976, null, info, "out.mp4", 18, 10);
        Assert.Contains("23.976", cmd.Args);
        Assert.Contains("libx264", cmd.Args);
        Assert.DoesNotContain("-shortest", cmd.Args);
        Assert.Equal("out.mp4", cmd.Args[^1]);
    }
}

public class ModuleArgsTests
{
    [Fact]
    public void X4_models_run_native_and_waifu2x_skips_x3()
    {
        var o = new UpscaleOptions { Engine = UpscaleEngine.RealEsrganPhoto, Scale = 2 };
        var (module, args, native) = ModuleArgs.Upscale(o, "in.png", "out.png", folder: false);
        Assert.Equal(ModuleId.RealEsrgan, module);
        Assert.Equal(4, native);
        Assert.Contains("realesrgan-x4plus", args);
        o.Engine = UpscaleEngine.RealEsrganVideo;
        Assert.Equal(2, ModuleArgs.Upscale(o, "a", "b", true).NativeScale);
        o.Engine = UpscaleEngine.Waifu2x;
        o.Scale = 3;
        var w = ModuleArgs.Upscale(o, "a", "b", false);
        Assert.Equal(ModuleId.Waifu2x, w.Module);
        Assert.Equal(4, w.NativeScale);
        Assert.Contains("models-cunet", w.Args);
        Assert.Contains("-8", ModuleArgs.Rife("i", "o", 240).Select(a => a == "%08d.png" ? "-8" : a));
    }
}

public class AppUpdaterTests
{
    [Fact]
    public void Tags_and_versions_compare()
    {
        Assert.Equal(new Version(0, 0, 9, 0), AppUpdater.ParseVersion("0.0.9"));
        Assert.Equal(new Version(0, 0, 9, 0), AppUpdater.ParseVersion("v0.0.9"));
        Assert.Null(AppUpdater.ParseVersion("latest"));
        Assert.True(AppUpdater.IsNewer(AppUpdater.ParseVersion("0.0.10")!, "0.0.9"));
        Assert.False(AppUpdater.IsNewer(AppUpdater.ParseVersion("0.0.7")!, "0.0.7"));
        Assert.False(AppUpdater.IsNewer(AppUpdater.ParseVersion("0.0.6")!, "0.0.7"));
    }

    [Fact]
    public void Release_json_finds_setup_portable_and_checksum()
    {
        var json = """
        {"tag_name":"0.0.9","html_url":"https://github.com/x/releases/tag/0.0.9","body":"notes","assets":[
          {"name":"convertit_v0.0.9_setup.exe","browser_download_url":"https://a/setup.exe","digest":"sha256:ABCDEF"},
          {"name":"convertit_v0.0.9_setup.exe.sha256","browser_download_url":"https://a/setup.sha"},
          {"name":"convertit_v0.0.9_portable.zip","browser_download_url":"https://a/p.zip"}]}
        """;
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var r = AppUpdater.Parse(doc.RootElement)!;
        Assert.Equal("https://a/setup.exe", r.SetupUrl);
        Assert.Equal("abcdef", r.SetupDigest);
        Assert.Equal("https://a/setup.sha", r.Sha256SetupUrl);
        Assert.Equal("https://a/p.zip", r.PortableUrl);
        Assert.Null(r.PortableDigest);
        var hash = new string('a', 64);
        Assert.Equal(hash, AppUpdater.ParseShaFile(hash + "  convertit_v0.0.9_setup.exe\n"));
        Assert.Null(AppUpdater.ParseShaFile("nope"));
    }
}
