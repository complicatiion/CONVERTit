using System.Text;
using CONVERTit.Core.GameDev;
using Xunit;

namespace CONVERTit.Core.Tests;

public class GameDevTests
{
    [Fact]
    public void Android_set_has_all_densities_and_adaptive_layers()
    {
        var files = AppIconSpecs.For(AppIconTarget.Android);
        Assert.Contains(files, f => f.Path == "android/res/mipmap-xxxhdpi/ic_launcher.png" && f.Width == 192);
        Assert.Contains(files, f => f.Path == "android/res/mipmap-mdpi/ic_launcher.png" && f.Width == 48);
        Assert.Contains(files, f => f.Path == "android/res/mipmap-hdpi/ic_launcher_foreground.png" && f.Width == 162);
        Assert.Contains(files, f => f.Path == "android/res/mipmap-xxxhdpi/ic_launcher_foreground.png" && f.Width == 432);
        Assert.Contains(files, f => f.Path == "android/playstore-icon.png" && f.Width == 512);
        Assert.DoesNotContain(files, f => f.Path.Contains("banner"));
    }

    [Fact]
    public void Tv_banner_and_ios_sizes_are_exact()
    {
        var files = AppIconSpecs.For(AppIconTarget.AndroidTv | AppIconTarget.Ios);
        Assert.Contains(files, f => f.Path.EndsWith("banner.png") && f.Width == 320 && f.Height == 180);
        Assert.Contains(files, f => f.Path.EndsWith("icon-ipad-83.5@2x.png") && f.Width == 167);
        Assert.Contains(files, f => f.Path.EndsWith("icon-iphone-60@3x.png") && f.Width == 180);
        Assert.Contains(files, f => f.Path.EndsWith("icon-1024.png") && f.Opaque);
        Assert.Contains("\"83.5x83.5\"", AppIconSpecs.IosContentsJson());
    }

    [Fact]
    public void Godot_set_matches_export_preset_sizes()
    {
        var files = AppIconSpecs.For(AppIconTarget.Godot);
        Assert.Contains(files, f => f.Path == "godot/android/adaptive_foreground_432x432.png" && f.Layer == IconLayer.AdaptiveForeground);
        Assert.Contains(files, f => f.Path == "godot/ios/app_store_1024x1024.png" && f.Width == 1024);
        Assert.Contains(files, f => f.Path == "godot/windows/icon.ico" && f.Container == IconContainer.Ico);
        Assert.Equal(72.0 / 108.0, AppIconSpecs.SafeFactor(IconLayer.AdaptiveForeground), 6);
    }

    [Fact]
    public void Icns_has_header_length_and_entries()
    {
        var png = new byte[] { 1, 2, 3 };
        var icns = IcnsWriter.Build([(16, png), (1024, png)]);
        Assert.Equal("icns", Encoding.ASCII.GetString(icns, 0, 4));
        Assert.Equal(icns.Length, (icns[4] << 24) | (icns[5] << 16) | (icns[6] << 8) | icns[7]);
        Assert.Equal("icp4", Encoding.ASCII.GetString(icns, 8, 4));
        Assert.Equal("ic10", Encoding.ASCII.GetString(icns, 8 + 11, 4));
    }

    [Fact]
    public void Grid_layout_uses_equal_cells()
    {
        var frames = Enumerable.Range(0, 5).Select(i => new SpriteFrame($"f{i}", "run", 32, 16)).ToList();
        var atlas = SpriteSheet.Layout(frames, SpriteLayout.Grid, columns: 3, spacing: 2);
        Assert.Equal(3, atlas.Columns);
        Assert.Equal(2, atlas.Rows);
        Assert.Equal(3 * 32 + 2 * 2, atlas.Width);
        Assert.Equal(2 * 16 + 2, atlas.Height);
        Assert.Equal(34, atlas.Frames[1].X);
        Assert.Equal(18, atlas.Frames[3].Y);
    }

    [Fact]
    public void Packed_layout_keeps_frames_apart_and_order()
    {
        var frames = new List<SpriteFrame> { new("a", "x", 10, 40), new("b", "x", 30, 10), new("c", "x", 20, 20) };
        var atlas = SpriteSheet.Layout(frames, SpriteLayout.Packed, spacing: 1, maxWidth: 40);
        Assert.True(atlas.Frames.Select(f => f.Frame.Name).SequenceEqual(["a", "b", "c"]));
        foreach (var p in atlas.Frames)
        {
            foreach (var q in atlas.Frames.Where(q => q != p))
            {
                var overlap = p.X < q.X + q.Frame.Width && q.X < p.X + p.Frame.Width && p.Y < q.Y + q.Frame.Height && q.Y < p.Y + p.Frame.Height;
                Assert.False(overlap);
            }
        }

        Assert.Equal(32, SpriteSheet.Layout(frames, SpriteLayout.Packed, maxWidth: 40, powerOfTwo: true).Width);
    }

    [Fact]
    public void Godot_tres_has_atlas_textures_and_animations()
    {
        var frames = new List<SpriteFrame> { new("w0", "walk", 16, 16), new("w1", "walk", 16, 16), new("i0", "idle", 16, 16) };
        var atlas = SpriteSheet.Layout(frames, SpriteLayout.Grid, columns: 3, spacing: 0);
        var tres = SpriteSheet.GodotSpriteFrames(atlas, "res://sprites/hero.png", 12, true);
        Assert.StartsWith("[gd_resource type=\"SpriteFrames\" load_steps=5 format=3]", tres);
        Assert.Contains("region = Rect2(16, 0, 16, 16)", tres);
        Assert.Contains("\"name\": &\"walk\"", tres);
        Assert.Contains("\"name\": &\"idle\"", tres);
        Assert.Contains("\"speed\": 12.0", tres);
        Assert.Contains("SubResource(\"AtlasTexture_2\")", tres);
        var json = SpriteSheet.TexturePackerJson(atlas, "hero.png", 12);
        Assert.Contains("\"w1\"", json);
        Assert.Contains("\"image\": \"hero.png\"", json);
    }
}
