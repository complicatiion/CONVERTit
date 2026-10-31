using System.Text.Json;
using System.Text.Json.Serialization;

namespace CONVERTit.Core.Storage;

/// <summary>JSON settings files: enums as text, indented, written atomically.</summary>
public static class JsonFile
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static T? Read<T>(string path) where T : class
    {
        if (!File.Exists(path))
        {
            return null;
        }

        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<T>(stream, Options);
    }

    public static void Write<T>(string path, T value)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, Options));
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>Deep copy through JSON (snapshot of options when a job is queued).</summary>
    public static T Clone<T>(T value) where T : class =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Options), Options)!;

    public static JsonElement ToElement<T>(T value) => JsonSerializer.SerializeToElement(value, Options);

    /// <summary>Copies all public writable properties of a saved state onto an existing object.</summary>
    public static void Populate<T>(JsonElement element, T target) where T : class
    {
        var source = element.Deserialize<T>(Options);
        if (source is null)
        {
            return;
        }

        foreach (var property in typeof(T).GetProperties())
        {
            if (property.CanRead && property.CanWrite && property.GetIndexParameters().Length == 0)
            {
                property.SetValue(target, property.GetValue(source));
            }
        }
    }
}

/// <summary>Human-readable numbers.</summary>
public static class Units
{
    public static string Bytes(long bytes)
    {
        if (bytes < 0)
        {
            return "–";
        }

        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} B" : $"{value:0.##} {units[unit]}";
    }

    public static string Bitrate(long bitsPerSecond) =>
        bitsPerSecond <= 0 ? "–" : bitsPerSecond >= 1_000_000 ? $"{bitsPerSecond / 1_000_000d:0.##} Mbit/s" : $"{bitsPerSecond / 1000d:0} kbit/s";
}
