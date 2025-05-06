using Microsoft.Extensions.Configuration;

namespace Scribal;

public class ModelConfiguration
{
    public ModelConfiguration(IConfiguration configurationManager)
    {
        configurationManager
            .GetRequiredSection($"AIServices:{OpenAIConfig.ConfigSectionName}")
            .Bind(OpenAIConfig);
        
        configurationManager
            .GetRequiredSection($"AIServices:{GeminiConfig.ConfigSectionName}")
            .Bind(GeminiConfig);

        configurationManager
            .GetRequiredSection($"AIServices:{DeepSeekConfig.ConfigSectionName}")
            .Bind(DeepSeekConfig);

    }

    public OpenAIConfig OpenAIConfig { get; private set; } = new();
    public GeminiConfig GeminiConfig { get; private set; } = new();
    public DeepSeekConfig DeepSeekConfig { get; private set; } = new();
}