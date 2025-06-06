using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.Memory;
using Scribal.Agency;
using Scribal.Config;
using DiffEditor = Scribal.Agency.DiffEditor;

namespace Scribal;

#pragma warning disable SKEXP0050
#pragma warning disable SKEXP0001
#pragma warning disable SKEXP0020
#pragma warning disable SKEXP0070 // experimental attribute until GA
#pragma warning disable SKEXP0010 // “other OpenAI-style” endpoint

public static class ScribalModelServiceCollectionExtensions
{
    private const string AiSectionPath = "AI";

    public static void AddScribalAi(this IServiceCollection services)
    {
        // Register everything that implements IModelProvider.
        IModelProvider.AddModelProviders(services);

        // Register services in the main DI container that the kernel will pull back later.
        services.AddSingleton<FileReader>();
        services.AddSingleton<DiffEditor>();
        services.AddSingleton<GitCommitFilter>();

        AddRag(services);

        SetUpIOptions(services);

        services.AddSingleton<Kernel>(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<AiSettings>>().Value;
            var providers = sp.GetServices<IModelProvider>().ToList();

            var kb = Kernel.CreateBuilder();

            RegisterModelSlotsInKernel(settings, kb, providers);

            AddToolsToKernel(sp, kb, settings);

            return kb.Build();
        });
    }

    private static void SetUpIOptions(IServiceCollection services)
    {
        var err = string.Empty;

        services.AddOptions<AiSettings>()
                .BindConfiguration(AiSectionPath,
                    options =>
                    {
                        options.BindNonPublicProperties = false;
                        options.ErrorOnUnknownConfiguration = true;
                    })
                .Validate(settings =>
                    {
                        var result = SlotsValidator.ValidateSlots(settings, out var error);
                        err = error;

                        return result;
                    },
                    err);
    }

    private static void RegisterModelSlotsInKernel(AiSettings settings, IKernelBuilder kb, List<IModelProvider> providers)
    {
        // See if we have a model provider who can fill each slot; if so, let them do it.
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
    }
    
    private static void Register(IKernelBuilder kb,
        ModelSlot slot,
        string suffix,
        IEnumerable<IModelProvider> providers)
    {
        var p = providers.Single(x => x.Name.Equals(slot.Provider, StringComparison.OrdinalIgnoreCase));

        p.RegisterServices(kb, slot, suffix);
    }

    private static void AddToolsToKernel(IServiceProvider sp, IKernelBuilder kb, AiSettings settings)
    {
        // TODO: This is obviously not very robust.
        // Need a model info file probably.
        var supportsToolUse = settings.Primary?.Provider switch
        {
            "deepseek" => false,
            _ => true
        };

        if (!supportsToolUse)
        {
            return;
        }
        
        // Pull back those services we registered in the main DI container and put them into the kernel.
        JsonSerializerOptions options = new();

        var diffEditor = sp.GetRequiredService<DiffEditor>();
        var fileReader = sp.GetRequiredService<FileReader>();

        kb.Plugins.AddFromFunctions(nameof(FileReader),
            [KernelFunctionFactory.CreateFromMethod(fileReader.ReadFileContentAsync, options)]);

        kb.Plugins.AddFromFunctions(nameof(DiffEditor),
            [KernelFunctionFactory.CreateFromMethod(diffEditor.ApplyUnifiedDiffAsync, options)]);

        var appConfig = sp.GetRequiredService<IOptions<AppConfig>>().Value;

        if (appConfig.IngestContent)
        {
            var vectorSearch = sp.GetRequiredService<VectorSearch>();

            kb.Plugins.AddFromFunctions(nameof(VectorSearch),
                [KernelFunctionFactory.CreateFromMethod(vectorSearch.SearchAsync, options)]);
        }

        kb.Services.AddSingleton<IFunctionInvocationFilter>(sp.GetRequiredService<GitCommitFilter>());
    }

    private static void AddRag(IServiceCollection services)
    {
        services.AddSingleton<ISemanticTextMemory>(sp =>
        {
            var appConfig = sp.GetRequiredService<IOptions<AppConfig>>().Value;
            var embeddings = sp.GetRequiredService<IOptions<AiSettings>>().Value.Embeddings;

            if (embeddings?.ApiKey is null)
            {
                throw new NotImplementedException("Figure out how to make RAG optional again...?");
            }

            var store = appConfig.DryRun ? new() : new VolatileMemoryStore();

            var memory = new MemoryBuilder().WithMemoryStore(store)
                                            .WithOpenAITextEmbeddingGeneration(embeddings.ModelId, embeddings.ApiKey)
                                            .Build();

            return memory;
        });
    }
}