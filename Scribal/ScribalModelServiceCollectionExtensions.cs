using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.Memory;
using Scribal.Agency;
using DiffEditor = Scribal.Agency.DiffEditor;

namespace Scribal;

#pragma warning disable SKEXP0050
#pragma warning disable SKEXP0001
#pragma warning disable SKEXP0020
#pragma warning disable SKEXP0070 // experimental attribute until GA
#pragma warning disable SKEXP0010 // “other OpenAI-style” endpoint

public static class ScribalModelServiceCollectionExtensions
{
    public static IServiceCollection AddScribalAi(this IServiceCollection services, IConfiguration cfg)
    {
        services.AddSingleton<IModelProvider, OpenAIModelProvider>();
        services.AddSingleton<IModelProvider, GeminiModelProvider>();
        services.AddSingleton<IModelProvider, DeepSeekModelProvider>();

        services.AddSingleton<FileReader>();
        services.AddSingleton<DiffEditor>();
        services.AddSingleton<GitCommitFilter>();

        AddRag(services);
        
        var err = string.Empty;

        services.AddOptions<AiSettings>()
            .Bind(cfg.GetSection("AI"))
            .ValidateDataAnnotations()
            .Validate(settings =>
                {
                    var result = SlotsValidator.ValidateSlots(settings, out var error);
                    err = error;
                    return result;
                },
                err);

        services.AddSingleton<Kernel>(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<AiSettings>>().Value;
            var providers = sp.GetServices<IModelProvider>().ToList();

            var kb = Kernel.CreateBuilder();
            
            if (settings.Primary is not null)
            {
                Register(kb, settings.Primary, string.Empty, providers);
            }
            
            if (settings.Weak is not null)
            {
                Register(kb, settings.Weak, "-weak", providers);
            }

            if (settings.Embeddings is not null)
            {
                Register(kb, settings.Embeddings, "-embed", providers);
            }

            kb.Plugins.AddFromObject(sp.GetRequiredService<FileReader>(), nameof(FileReader));
            kb.Plugins.AddFromObject(sp.GetRequiredService<DiffEditor>(), nameof(DiffEditor));

            var appConfig = sp.GetRequiredService<IOptions<AppConfig>>().Value;

            if (appConfig.IngestContent)
            {
                kb.Plugins.AddFromObject(sp.GetRequiredService<VectorSearch>(), nameof(VectorSearch));
            }

            kb.Services.AddSingleton<IFunctionInvocationFilter>(sp.GetRequiredService<GitCommitFilter>());

            return kb.Build();
        });

        return services;
    }

    private static void Register(IKernelBuilder kb,
        ModelSlot slot,
        string suffix,
        IEnumerable<IModelProvider> providers)
    {
        var p = providers.Single(x => x.Name.Equals(slot.Provider, StringComparison.OrdinalIgnoreCase));

        p.RegisterServices(kb, slot, suffix);
    }

    private static void AddRag(IServiceCollection services)
    {
        services.AddSingleton<ISemanticTextMemory>(sp =>
        {
            var appConfig = sp.GetRequiredService<IOptions<AppConfig>>().Value;
            var embeddings = sp.GetRequiredService<IOptions<AiSettings>>().Value.Embeddings;

            if (embeddings is null)
            {
                throw new NotImplementedException("Figure out how to make RAG optional again...?");
            }
            
            var store = appConfig.DryRun ? new VolatileMemoryStore() : new VolatileMemoryStore();

            var memory = new MemoryBuilder().WithMemoryStore(store)
                .WithOpenAITextEmbeddingGeneration(embeddings.ModelId, embeddings.ApiKey)
                .Build();

            return memory;
        });
    }
}