using Spectre.Console;

namespace Scribal.Cli;

public class ModelOnboarder
{
    public async Task PerformOnboardingExperience(ModelState state, ModelConfiguration modelConfiguration)
    {
        // apply configuration to model as best as we can
        var oaiKey = modelConfiguration.OpenAIConfig.ApiKey;
        var geminiKey = modelConfiguration.GeminiConfig.ApiKey;
        var deepseekKey = modelConfiguration.DeepSeekConfig.ApiKey;

        string?[] all = [oaiKey, geminiKey, deepseekKey];
        
        var present = all.Count(s => !string.IsNullOrEmpty(s));
        
        if (present is 0)
        {
            throw new InvalidOperationException("No API key has been supplied for any provider.");
        }
        
        if (present > 1)
        {
            throw new InvalidOperationException("You have multiple active API keys. Please set the 'Provider' flag to set who you will actually use.");
        }
        
        // if not enough, do the GUI experience

        var ok = await AnsiConsole.ConfirmAsync("Your AI model details weren't found. Set them up now?");

        if (!ok)
        {
            return;
        }

        var choices = ModelSelector.BeginConfiguration();

        // this is wrong
        state.ModelServiceId = choices.Model;
        state.EmbeddingModelServiceId = choices.WeakModel;
    }
}