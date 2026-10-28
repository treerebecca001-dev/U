using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SceneWorkbench.Core;

public static class JsonFile
{
    public static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    public static T Read<T>(string path) =>
        JsonSerializer.Deserialize<T>(File.ReadAllBytes(path), Options)
        ?? throw new InvalidDataException($"無法讀取 JSON：{path}");

    public static void WriteNew<T>(string path, T value)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(stream, value, Options);
        stream.Flush(true);
    }

    public static void WriteAtomic<T>(string path, T value)
    {
        var temporary = path + "." + System.Guid.NewGuid().ToString("N") + ".tmp";
        WriteNew(temporary, value);
        File.Move(temporary, path, true);
    }

    public static void WriteTextNew(string path, string text)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, true))
            writer.Write(text);
        stream.Flush(true);
    }
}
