namespace Scribal.Cli;

public class ModelConfiguration
{
    public const string SectionName = "Model";
    public string Name { get; set; } = "gpt-4o-mini";
    public string OpenAI { get; set; } = string.Empty;
    public string DeepSeek { get; set; } = string.Empty;
    public string Anthropic { get; set; } = string.Empty;
    public string Mistral { get; set; } = string.Empty;
}
