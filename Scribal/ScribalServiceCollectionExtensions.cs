using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Scribal.Agency;
using Scribal.Cli;

namespace Scribal;

#pragma warning disable SKEXP0070 // experimental attribute until GA
#pragma warning disable SKEXP0010 // “other OpenAI-style” endpoint

public static class ScribalServiceCollectionExtensions
{
    public static IServiceCollection AddScribalAi(this IServiceCollection services, IConfiguration cfg)
    {
        var kb = services.AddKernel();
        
        AddModels(cfg, kb);

        AddPlugins(kb);

        AddFilters(kb);
        
        return services;
    }

    private static void AddModels(IConfiguration cfg, IKernelBuilder kb)
    {
        var oaiKey = cfg["OpenAI:ApiKey"];
        var geminiKey = cfg["Gemini:ApiKey"];
        var deepseekKey = cfg["DeepSeek:ApiKey"];

        if (string.IsNullOrEmpty(oaiKey) && string.IsNullOrEmpty(geminiKey) && string.IsNullOrEmpty(deepseekKey))
        {
            throw new InvalidOperationException("No API key has been supplied for any provider.");
        }

        var oaiModel = cfg["OpenAI:Model"] ?? "gpt-4o-mini";
        var geminiModel = cfg["Gemini:Model"] ?? "gemini-1.5-pro";
        var deepseekModel = cfg["DeepSeek:Model"] ?? "deepseek-chat";

        if (!string.IsNullOrEmpty(oaiKey))
        {
            kb.AddOpenAIChatCompletion(modelId: oaiModel, apiKey: oaiKey, serviceId: "openai");
            kb.AddOpenAIChatCompletion(modelId: "gpt-4o-mini", apiKey: oaiKey, serviceId: "openai-weak");
        }

        if (!string.IsNullOrEmpty(geminiKey))
        {
            kb.AddGoogleAIGeminiChatCompletion(geminiModel,
                apiKey: geminiKey,
                serviceId: "gemini");
                
            kb.AddGoogleAIGeminiChatCompletion("gemini-1.5-pro",
                apiKey: geminiKey,
                serviceId: "gemini-weak");
        }

        if (!string.IsNullOrEmpty(deepseekKey))
        {
            kb.AddOpenAIChatCompletion(modelId: deepseekModel,
                apiKey: deepseekKey,
                endpoint: new("https://api.deepseek.com"),
                serviceId: "deepseek");
                
            kb.AddOpenAIChatCompletion(modelId: "deepseek-chat",
                apiKey: deepseekKey,
                endpoint: new("https://api.deepseek.com"),
                serviceId: "deepseek-weak");
        }
    }

    private static void AddPlugins(IKernelBuilder kb)
    {
        kb.Plugins.AddFromType<FileReader>(nameof(FileReader));
        kb.Plugins.AddFromType<DiffEditor>(nameof(DiffEditor));
    }
    
    private static void AddFilters(IKernelBuilder kb)
    {
        kb.Services.AddSingleton<IFunctionInvocationFilter, GitCommitFilter>();
    }
}