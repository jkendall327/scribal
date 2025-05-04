using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Scribal.Cli;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Scribal;

#pragma warning disable SKEXP0070 // experimental attribute until GA
#pragma warning disable SKEXP0010 // “other OpenAI-style” endpoint

public static class ScribalServiceCollectionExtensions
{
    public static IServiceCollection AddScribalAi(this IServiceCollection services, IConfiguration cfg)
    {
        // Store the IServiceCollection so we can populate the kernel's service collection with it in the lambda.
        services.AddSingleton(services);

        services.AddSingleton<Kernel>(sp =>
        {
            var kb = Kernel.CreateBuilder();

            var existingServices = sp.GetRequiredService<IServiceCollection>();

            kb.Services.Add(existingServices);

            var oaiKey = cfg["OPENAI_API_KEY"];
            var geminiKey = cfg["GEMINI_API_KEY"];
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
            }

            if (!string.IsNullOrEmpty(geminiKey))
            {
                kb.AddGoogleAIGeminiChatCompletion(geminiModel,
                    apiKey: geminiKey,
                    serviceId: "gemini");
            }

            if (!string.IsNullOrEmpty(deepseekKey))
            {
                kb.AddOpenAIChatCompletion(modelId: deepseekModel,
                    apiKey: deepseekKey,
                    endpoint: new("https://api.deepseek.com"),
                    serviceId: "deepseek");
            }

            kb.Plugins.AddFromType<FileReader>("FileReader");
            kb.Plugins.AddFromType<DiffService>("DiffEditor");

            return kb.Build();
        });

        return services;
    }
}