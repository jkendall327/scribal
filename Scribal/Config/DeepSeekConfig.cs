using System.ComponentModel.DataAnnotations;

namespace Scribal;

public sealed class DeepSeekConfig
{
    public const string ConfigSectionName = "DeepSeek";

    [Required]
    public string ModelId { get; set; } = "deepseek-chat";
    
    [Required]
    public string WeakModelId { get; set; } = string.Empty;
    
    [Required]
    public string ApiKey { get; set; } = string.Empty;
}