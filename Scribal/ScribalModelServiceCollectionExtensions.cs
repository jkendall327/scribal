using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.InMemory;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.Connectors.Qdrant;
using Microsoft.SemanticKernel.Data;
using Microsoft.SemanticKernel.Memory;
using Qdrant.Client;
using Scribal.Agency;
using Scribal.Context;
using DiffEditor = Scribal.Agency.DiffEditor;

#pragma warning disable SKEXP0050
#pragma warning disable SKEXP0001
#pragma warning disable SKEXP0020

namespace Scribal;

#pragma warning disable SKEXP0070 // experimental attribute until GA
#pragma warning disable SKEXP0010 // “other OpenAI-style” endpoint

public static class ScribalModelServiceCollectionExtensions
{
    public static IServiceCollection AddScribalAi(this IServiceCollection services,
        IConfiguration cfg,
        ModelConfiguration modelConfiguration)
    {
        var kb = services.AddKernel();

        AddModels(cfg, kb, modelConfiguration);

        AddPlugins(cfg, kb);

        AddRag(cfg, kb, modelConfiguration);

        AddFilters(kb);

        return services;
    }

    private static void AddModels(IConfiguration cfg, IKernelBuilder kb, ModelConfiguration modelConfiguration)
    {
        var oaiKey = modelConfiguration.OpenAIConfig.ApiKey;
        var geminiKey = modelConfiguration.GeminiConfig.ApiKey;
        var deepseekKey = modelConfiguration.DeepSeekConfig.ApiKey;

        // string?[] all = [oaiKey, geminiKey, deepseekKey];
        //
        // var present = all.Count(s => !string.IsNullOrEmpty(s));
        //
        // if (present is 0)
        // {
        //     throw new InvalidOperationException("No API key has been supplied for any provider.");
        // }
        //
        // if (present > 1)
        // {
        //     throw new InvalidOperationException("You have multiple active API keys. Please set the 'Provider' flag to set who you will actually use.");
        // }

        var oaiModel = modelConfiguration.OpenAIConfig.ModelId;
        var geminiModel = modelConfiguration.GeminiConfig.ModelId;
        var deepseekModel = modelConfiguration.DeepSeekConfig.ModelId;

        if (!string.IsNullOrEmpty(oaiKey))
        {
            kb.AddOpenAIChatCompletion(modelId: oaiModel, apiKey: oaiKey, serviceId: "openai");

            kb.AddOpenAIChatCompletion(modelId: modelConfiguration.OpenAIConfig.WeakModelId,
                apiKey: oaiKey,
                serviceId: "openai-weak");
        }

        if (!string.IsNullOrEmpty(geminiKey))
        {
            kb.AddGoogleAIGeminiChatCompletion(geminiModel, apiKey: geminiKey, serviceId: "gemini");

            kb.AddGoogleAIGeminiChatCompletion(modelConfiguration.GeminiConfig.WeakModelId,
                apiKey: geminiKey,
                serviceId: "gemini-weak");
        }

        if (!string.IsNullOrEmpty(deepseekKey))
        {
            kb.AddOpenAIChatCompletion(modelId: deepseekModel,
                apiKey: deepseekKey,
                endpoint: new("https://api.deepseek.com"),
                serviceId: "deepseek");

            kb.AddOpenAIChatCompletion(modelId: modelConfiguration.DeepSeekConfig.WeakModelId,
                apiKey: deepseekKey,
                endpoint: new("https://api.deepseek.com"),
                serviceId: "deepseek-weak");
        }
    }

    private static void AddRag(IConfiguration cfg, IKernelBuilder kb, ModelConfiguration modelConfiguration)
    {
        var apiKey = modelConfiguration.OpenAIConfig.ApiKey;
        var dry = cfg.GetValue<bool>("DryRun");

        var store = dry ? new VolatileMemoryStore() : new VolatileMemoryStore();

        var memory = new MemoryBuilder().WithMemoryStore(store)
            .WithOpenAITextEmbeddingGeneration(modelConfiguration.OpenAIConfig.EmbeddingsModelId, apiKey)
            .Build();

        kb.Services.AddSingleton(memory);
    }

    private static void AddPlugins(IConfiguration cfg, IKernelBuilder kb)
    {
        kb.Plugins.AddFromType<FileReader>(nameof(FileReader));
        kb.Plugins.AddFromType<DiffEditor>(nameof(DiffEditor));

        var ingest = cfg.GetValue<bool>("IngestContent");

        if (ingest)
        {
            kb.Plugins.AddFromType<VectorSearch>(nameof(VectorSearch));
        }
    }

    private static void AddFilters(IKernelBuilder kb)
    {
        kb.Services.AddSingleton<IFunctionInvocationFilter, GitCommitFilter>();
    }
}