using System.Text.Json;
using System.Text.Json.Serialization;
using Scribal.Workspace;

namespace Scribal;

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(StoryOutline))]
public partial class ScribalJsonContext : JsonSerializerContext;

public static class JsonDefaults
{
    public static JsonSerializerOptions Default { get; } = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };
    
    public static ScribalJsonContext Context { get; } = new(Default);
}