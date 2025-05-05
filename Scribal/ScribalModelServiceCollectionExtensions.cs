using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Data;
using Scribal.Agency;
using Scribal.Context;
using DiffEditor = Scribal.Agency.DiffEditor;

namespace Scribal;

#pragma warning disable SKEXP0070 // experimental attribute until GA
#pragma warning disable SKEXP0010 // “other OpenAI-style” endpoint

public static class ScribalModelServiceCollectionExtensions
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

        string?[] all = [oaiKey, geminiKey, deepseekKey];

        var present = all.Count(s => !string.IsNullOrEmpty(s));

        if (present is 0)
        {
            throw new InvalidOperationException("No API key has been supplied for any provider.");
        }

        if (present > 1)
        {
            var preference = cfg["Provider"];

            if (string.IsNullOrEmpty(preference))
            {
                throw new InvalidOperationException(
                    "You have multiple active API keys. Please set the 'Provider' flag to set who you will actually use.");
            }
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
            kb.AddGoogleAIGeminiChatCompletion(geminiModel, apiKey: geminiKey, serviceId: "gemini");

            kb.AddGoogleAIGeminiChatCompletion("gemini-1.5-pro", apiKey: geminiKey, serviceId: "gemini-weak");
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

    private static void AddRag(IConfiguration cfg, IKernelBuilder kb)
    {
        kb.AddOpenAITextEmbeddingGeneration("embeddings-small", cfg["OpenAI:ApiKey"]);

        kb.AddInMemoryVectorStoreRecordCollection<string, TextSnippet<string>>("collection-name");

        kb.AddVectorStoreTextSearch<TextSnippet<string>>(
            new TextSearchStringMapper(result => (result as TextSnippet<string>)!.Text!),
            new TextSearchResultMapper(result =>
            {
                // Create a mapping from the Vector Store data type to the data type returned by the Text Search.
                // This text search will ultimately be used in a plugin and this TextSearchResult will be returned to the prompt template
                // when the plugin is invoked from the prompt template.
                var castResult = result as TextSnippet<string>;
                return new(value: castResult!.Text!)
                {
                    Name = castResult.ReferenceDescription,
                    Link = castResult.ReferenceLink
                };
            }));
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