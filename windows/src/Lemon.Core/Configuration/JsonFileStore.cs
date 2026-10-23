using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lemon.Core.Configuration;

/// <summary>Reads and writes small JSON documents under %LOCALAPPDATA%\Lemon.</summary>
public static class JsonFileStore
{
    public static string DefaultDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lemon");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static T? Load<T>(string path)
    {
        if (!File.Exists(path))
            return default;
        try
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<T>(stream, Options);
        }
        catch (JsonException)
        {
            // A corrupt file shouldn't stop the app from starting; fall back to defaults.
            return default;
        }
    }

    public static void Save<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, Options));
        File.Move(temp, path, overwrite: true);
    }
}
