using Microsoft.Extensions.Configuration;

namespace Scribal;

public class ModelConfiguration
{
    public ModelConfiguration(IConfiguration configurationManager)
    {
        configurationManager
            .GetSection($"AI:{OpenAIConfig.ConfigSectionName}")
            .Bind(OpenAIConfig);
        
        configurationManager
            .GetSection($"AI:{GeminiConfig.ConfigSectionName}")
            .Bind(GeminiConfig);

        configurationManager
            .GetSection($"AI:{DeepSeekConfig.ConfigSectionName}")
            .Bind(DeepSeekConfig);

    }

    public OpenAIConfig OpenAIConfig { get; private set; } = new();
    public GeminiConfig GeminiConfig { get; private set; } = new();
    public DeepSeekConfig DeepSeekConfig { get; private set; } = new();
}