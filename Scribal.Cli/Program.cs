using System.IO.Abstractions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenAI;
using Scribal.Cli;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddUserSecrets<Program>();

// Configure model settings
var modelConfig = new ModelConfiguration();
builder.Configuration.GetSection("ModelConfiguration").Bind(modelConfig);

// Allow command-line overrides for model and API keys
if (args.Length > 0)
{
    for (int i = 0; i < args.Length; i++)
    {
        if (args[i] == "--model" && i + 1 < args.Length)
        {
            modelConfig.ModelName = args[i + 1];
            i++;
        }
        else if (args[i] == "--openai-key" && i + 1 < args.Length)
        {
            modelConfig.OpenAIApiKey = args[i + 1];
            i++;
        }
        else if (args[i] == "--deepseek-key" && i + 1 < args.Length)
        {
            modelConfig.DeepSeekApiKey = args[i + 1];
            i++;
        }
        else if (args[i] == "--anthropic-key" && i + 1 < args.Length)
        {
            modelConfig.AnthropicApiKey = args[i + 1];
            i++;
        }
        else if (args[i] == "--mistral-key" && i + 1 < args.Length)
        {
            modelConfig.MistralApiKey = args[i + 1];
            i++;
        }
    }
}

// Also check environment variables if keys are still empty
if (string.IsNullOrEmpty(modelConfig.OpenAIApiKey))
{
    modelConfig.OpenAIApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? "";
}

var filesystem = new FileSystem();

builder.Services.AddSingleton<IFileSystem>(filesystem);
builder.Services.AddSingleton<CommandService>();
builder.Services.AddSingleton<PromptBuilder>();
builder.Services.AddSingleton<IDocumentScanService, DocumentScanService>();
builder.Services.AddSingleton<InterfaceManager>();
builder.Services.AddSingleton<DiffService>();
builder.Services.AddSingleton<IConversationStore, ConversationStore>();
builder.Services.AddSingleton<IModelClient, ModelClient>();
builder.Services.AddSingleton<IGitService, GitService>(s => new(Directory.GetCurrentDirectory()));
builder.Services.AddSingleton(modelConfig);

// Use the configured API key
builder.Services.AddSingleton(new OpenAIClient(modelConfig.OpenAIApiKey));

builder.Services
    .AddChatClient(services => services.GetRequiredService<OpenAIClient>().AsChatClient(modelConfig.ModelName))
    .UseLogging()
    .UseFunctionInvocation();

var app = builder.Build();

var manager = app.Services.GetRequiredService<InterfaceManager>();

await manager.DisplayWelcome();
await manager.RunMainLoop();
