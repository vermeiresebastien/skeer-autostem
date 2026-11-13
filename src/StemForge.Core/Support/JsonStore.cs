using System.Text.Json;
using System.Text.Json.Serialization;

namespace StemForge.Core.Support;

/// <summary>Atomic JSON persistence: never leaves a half-written file behind.</summary>
public static class JsonStore
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static T? Load<T>(string path) where T : class
    {
        if (!File.Exists(path)) return null;
        try
        {
            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json)) return null;
            return JsonSerializer.Deserialize<T>(json, Options);
        }
        catch (JsonException)
        {
            // Corrupt file: keep it for inspection rather than silently discarding the user's queue.
            var backup = path + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            try { File.Move(path, backup, overwrite: false); } catch { /* best effort */ }
            return null;
        }
    }

    public static void Save<T>(string path, T value)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(value, Options);
        AtomicFile.WriteAllText(path, json);
    }
}

/// <summary>Write-then-rename helper so readers never observe a partial file.</summary>
public static class AtomicFile
{
    public static void WriteAllText(string path, string contents)
    {
        var dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!;
        Directory.CreateDirectory(dir);
        var temp = System.IO.Path.Combine(dir, "." + System.IO.Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        File.WriteAllText(temp, contents);
        File.Move(temp, path, overwrite: true);
    }

    public static void WriteAllBytes(string path, byte[] contents)
    {
        var dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!;
        Directory.CreateDirectory(dir);
        var temp = System.IO.Path.Combine(dir, "." + System.IO.Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        File.WriteAllBytes(temp, contents);
        File.Move(temp, path, overwrite: true);
    }
}
