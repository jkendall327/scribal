using System.Text.Json;

namespace Scribal;

public static class JsonDefaults
{
    public static JsonSerializerOptions Default { get; } = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };
}