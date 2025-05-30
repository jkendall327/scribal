using System.Text.Json.Serialization;

namespace Scribal.Workspace;

public class StoryOutline
{
    [JsonPropertyName("Chapters")] public List<Chapter> Chapters { get; set; } = [];
}

public class Chapter
{
    [JsonPropertyName("ChapterNumber")] public int ChapterNumber { get; set; }

    [JsonPropertyName("Title")] public string Title { get; init; } = string.Empty;

    [JsonPropertyName("Summary")] public string Summary { get; set; } = string.Empty;

    [JsonPropertyName("Beats")] public List<string> Beats { get; } = [];

    [JsonPropertyName("EstimatedWordCount")]
    public int? EstimatedWordCount { get; set; }

    [JsonPropertyName("KeyCharacters")] public List<string> KeyCharacters { get; } = [];
}