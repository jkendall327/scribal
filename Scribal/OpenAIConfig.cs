using System.ComponentModel.DataAnnotations;

namespace Scribal;

interface IModelConfig
{
    public string ModelId { get; }
    public string ApiKey { get; }
}

public sealed class OpenAIConfig : IModelConfig
{
    public const string ConfigSectionName = "OpenAI";

    [Required]
    public string ModelId { get; set; } = string.Empty;
    
    [Required]
    public string WeakModelId { get; set; } = string.Empty;
    
    [Required]
    public string EmbeddingsModelId { get; set; } = "text-embedding-3-small";

    [Required]
    public string ApiKey { get; set; } = string.Empty;

    [Required]
    public string? OrgId { get; set; } = null;
}