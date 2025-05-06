using System.ComponentModel.DataAnnotations;

namespace Scribal;

public sealed class OpenAIConfig
{
    public const string ConfigSectionName = "OpenAI";

    [Required]
    public string ModelId { get; set; } = string.Empty;
    
    [Required]
    public string WeakModelId { get; set; } = "gpt-4o-mini";
    
    [Required]
    public string EmbeddingsModelId { get; set; } = "text-embedding-3-small";

    [Required]
    public string ApiKey { get; set; } = string.Empty;

    [Required]
    public string? OrgId { get; set; } = null;
}