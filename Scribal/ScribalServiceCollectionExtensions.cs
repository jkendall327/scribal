using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Scribal.Cli;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Scribal;

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

            if (!string.IsNullOrEmpty(oaiKey))
            {
                kb.AddOpenAIChatCompletion(modelId: cfg["OpenAI:Model"] ?? "gpt-4o-mini", apiKey: oaiKey, serviceId: "openai");
            }

            if (!string.IsNullOrEmpty(geminiKey))
            {
#pragma warning disable SKEXP0070 // experimental attribute until GA
                kb.AddGoogleAIGeminiChatCompletion(modelId: cfg["Gemini:Model"] ?? "gemini-1.5-pro",
                    apiKey: geminiKey,
                    serviceId: "gemini");
#pragma warning restore SKEXP0070
            }

            if (!string.IsNullOrEmpty(deepseekKey))
            {
#pragma warning disable SKEXP0010 // “other OpenAI-style” endpoint
                kb.AddOpenAIChatCompletion(modelId: cfg["DeepSeek:Model"] ?? "deepseek-chat",
                    apiKey: deepseekKey,
                    endpoint: new Uri("https://api.deepseek.com"),
                    serviceId: "deepseek");
#pragma warning restore SKEXP0010
            }

            kb.Plugins.AddFromType<FileReader>("FileReader");
            kb.Plugins.AddFromType<DiffService>("DiffEditor");

            return kb.Build();
        });

        return services;
    }
}