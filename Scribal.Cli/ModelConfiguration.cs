namespace Scribal.Cli;

public class ModelConfiguration
{
    public string ModelName { get; set; } = "gpt-4o-mini";
    public string OpenAIApiKey { get; set; } = string.Empty;
    public string DeepSeekApiKey { get; set; } = string.Empty;
    public string AnthropicApiKey { get; set; } = string.Empty;
    public string MistralApiKey { get; set; } = string.Empty;
}
