using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CONVERTit.Core.GameDev;

/// <summary>Grid: all frames in equal cells (engines slice by rows/columns). Packed: tight shelves, smaller atlas.</summary>
public enum SpriteLayout { Grid, Packed }

/// <summary>Metadata written next to the sprite sheet.</summary>
[Flags]
public enum SpriteMetadata
{
    None = 0,

    /// <summary>JSON in the TexturePacker "hash" format (read by many engines and plugins).</summary>
    Json = 1,

    /// <summary>Godot 4 SpriteFrames resource (.tres) for AnimatedSprite2D.</summary>
    GodotTres = 2
}

/// <summary>A frame to place: name, animation it belongs to, size after filters.</summary>
public sealed record SpriteFrame(string Name, string Animation, int Width, int Height);

/// <summary>Where a frame landed in the atlas.</summary>
public sealed record PlacedFrame(SpriteFrame Frame, int X, int Y);

public sealed record SpriteAtlas(int Width, int Height, IReadOnlyList<PlacedFrame> Frames, int Columns, int Rows);

/// <summary>Sprite sheet layout and metadata (pure, unit-tested).</summary>
public static class SpriteSheet
{
    /// <summary>
    /// Places the frames. Grid: cell = largest frame, <paramref name="columns"/> 0 = about square. Packed: frames
    /// sorted by height on shelves of the given maximum width (0 = automatic). <paramref name="spacing"/> pixels
    /// between frames (against bleeding when the engine filters textures). Optional power-of-two atlas size.
    /// </summary>
    public static SpriteAtlas Layout(IReadOnlyList<SpriteFrame> frames, SpriteLayout layout, int columns = 0, int spacing = 1,
        int maxWidth = 0, bool powerOfTwo = false)
    {
        if (frames.Count == 0)
        {
            return new SpriteAtlas(0, 0, [], 0, 0);
        }

        spacing = Math.Max(0, spacing);
        SpriteAtlas atlas;
        if (layout == SpriteLayout.Grid)
        {
            var cellW = frames.Max(f => f.Width);
            var cellH = frames.Max(f => f.Height);
            var cols = columns > 0 ? Math.Min(columns, frames.Count) : (int)Math.Ceiling(Math.Sqrt(frames.Count));
            var rows = (int)Math.Ceiling(frames.Count / (double)cols);
            var placed = frames.Select((f, i) => new PlacedFrame(f, i % cols * (cellW + spacing), i / cols * (cellH + spacing))).ToList();
            atlas = new SpriteAtlas(cols * cellW + (cols - 1) * spacing, rows * cellH + (rows - 1) * spacing, placed, cols, rows);
        }
        else
        {
            var area = frames.Sum(f => (long)(f.Width + spacing) * (f.Height + spacing));
            var width = maxWidth > 0 ? maxWidth : Math.Max(frames.Max(f => f.Width), (int)Math.Ceiling(Math.Sqrt(area) * 1.1));
            var placed = new List<PlacedFrame>();
            int x = 0, y = 0, shelf = 0, used = 0;
            foreach (var frame in frames.OrderByDescending(f => f.Height).ThenByDescending(f => f.Width))
            {
                if (x > 0 && x + frame.Width > width)
                {
                    x = 0;
                    y += shelf + spacing;
                    shelf = 0;
                }

                placed.Add(new PlacedFrame(frame, x, y));
                used = Math.Max(used, x + frame.Width);
                x += frame.Width + spacing;
                shelf = Math.Max(shelf, frame.Height);
            }

            // Back to the original order (animations stay in sequence in the metadata).
            var order = frames.Select((f, i) => (f, i)).ToDictionary(p => p.f, p => p.i);
            placed = placed.OrderBy(p => order[p.Frame]).ToList();
            atlas = new SpriteAtlas(used, y + shelf, placed, 0, 0);
        }

        return powerOfTwo ? atlas with { Width = NextPowerOfTwo(atlas.Width), Height = NextPowerOfTwo(atlas.Height) } : atlas;
    }

    public static int NextPowerOfTwo(int value)
    {
        var p = 1;
        while (p < value)
        {
            p <<= 1;
        }

        return p;
    }

    /// <summary>TexturePacker "JSON (hash)" format.</summary>
    public static string TexturePackerJson(SpriteAtlas atlas, string imageName, int fps)
    {
        var frames = new JsonObject();
        foreach (var p in atlas.Frames)
        {
            frames[p.Frame.Name] = new JsonObject
            {
                ["frame"] = new JsonObject { ["x"] = p.X, ["y"] = p.Y, ["w"] = p.Frame.Width, ["h"] = p.Frame.Height },
                ["rotated"] = false,
                ["trimmed"] = false,
                ["spriteSourceSize"] = new JsonObject { ["x"] = 0, ["y"] = 0, ["w"] = p.Frame.Width, ["h"] = p.Frame.Height },
                ["sourceSize"] = new JsonObject { ["w"] = p.Frame.Width, ["h"] = p.Frame.Height }
            };
        }

        var animations = new JsonObject();
        foreach (var group in atlas.Frames.GroupBy(p => p.Frame.Animation))
        {
            animations[group.Key] = new JsonArray(group.Select(p => (JsonNode)JsonValue.Create(p.Frame.Name)!).ToArray());
        }

        var root = new JsonObject
        {
            ["frames"] = frames,
            ["animations"] = animations,
            ["meta"] = new JsonObject
            {
                ["app"] = "CONVERTit",
                ["image"] = imageName,
                ["format"] = "RGBA8888",
                ["size"] = new JsonObject { ["w"] = atlas.Width, ["h"] = atlas.Height },
                ["scale"] = "1",
                ["frameRate"] = fps
            }
        };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// Godot 4 SpriteFrames resource: one AtlasTexture per frame (region in the sheet), one animation per group.
    /// <paramref name="texturePath"/> is the res:// path of the sheet in the Godot project.
    /// </summary>
    public static string GodotSpriteFrames(SpriteAtlas atlas, string texturePath, double fps, bool loop)
    {
        var sb = new StringBuilder();
        var frames = atlas.Frames;
        sb.Append(CultureInfo.InvariantCulture, $"[gd_resource type=\"SpriteFrames\" load_steps={frames.Count + 2} format=3]\n\n");
        sb.Append(CultureInfo.InvariantCulture, $"[ext_resource type=\"Texture2D\" path=\"{texturePath}\" id=\"1_sheet\"]\n\n");
        for (var i = 0; i < frames.Count; i++)
        {
            var p = frames[i];
            sb.Append(CultureInfo.InvariantCulture, $"[sub_resource type=\"AtlasTexture\" id=\"AtlasTexture_{i}\"]\n");
            sb.Append("atlas = ExtResource(\"1_sheet\")\n");
            sb.Append(CultureInfo.InvariantCulture, $"region = Rect2({p.X}, {p.Y}, {p.Frame.Width}, {p.Frame.Height})\n\n");
        }

        sb.Append("[resource]\nanimations = [");
        var groups = frames.Select((p, i) => (p, i)).GroupBy(t => t.p.Frame.Animation).ToList();
        for (var g = 0; g < groups.Count; g++)
        {
            var group = groups[g];
            sb.Append(g == 0 ? "{\n" : ", {\n");
            sb.Append("\"frames\": [");
            var first = true;
            foreach (var (_, index) in group)
            {
                sb.Append(first ? "{\n" : ", {\n");
                sb.Append("\"duration\": 1.0,\n");
                sb.Append(CultureInfo.InvariantCulture, $"\"texture\": SubResource(\"AtlasTexture_{index}\")\n}}");
                first = false;
            }

            sb.Append("],\n");
            sb.Append(loop ? "\"loop\": true,\n" : "\"loop\": false,\n");
            sb.Append(CultureInfo.InvariantCulture, $"\"name\": &\"{Escape(group.Key)}\",\n");
            sb.Append(CultureInfo.InvariantCulture, $"\"speed\": {fps.ToString("0.0##", CultureInfo.InvariantCulture)}\n}}");
        }

        sb.Append("]\n");
        return sb.ToString();
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
