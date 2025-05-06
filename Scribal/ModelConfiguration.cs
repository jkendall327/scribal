using Microsoft.Extensions.Configuration;

namespace Scribal;

public class ModelConfiguration
{
    private readonly OpenAIConfig _openAIConfig = new();

    public ModelConfiguration(IConfiguration configurationManager)
    {
        configurationManager
            .GetRequiredSection($"AIServices:{OpenAIConfig.ConfigSectionName}")
            .Bind(_openAIConfig);
    }
    
    public OpenAIConfig OpenAIConfig => _openAIConfig;
}