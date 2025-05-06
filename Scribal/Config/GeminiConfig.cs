using System.ComponentModel.DataAnnotations;

namespace Scribal;

public sealed class GeminiConfig
{
    public const string ConfigSectionName = "Gemini";

    [Required]
    public string ModelId { get; set; } = "gemini-1.5-pro";
    
    [Required]
    public string WeakModelId { get; set; } = "gemini-1.5-pro";
    
    [Required]
    public string? EmbeddingsModelId { get; set; }

    [Required]
    public string ApiKey { get; set; } = string.Empty;
}