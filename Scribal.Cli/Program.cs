using System.IO.Abstractions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenAI;
using Scribal.Cli;
using Spectre.Console;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddUserSecrets<Program>();

var modelConfig = new ModelConfiguration();
builder.Configuration.GetSection(ModelConfiguration.SectionName).Bind(modelConfig);

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

// Check for API keys and configure the appropriate client
if (!string.IsNullOrEmpty(modelConfig.OpenAI))
{
    // Use OpenAI client
    builder.Services.AddSingleton(new OpenAIClient(modelConfig.OpenAI));
    builder.Services
        .AddChatClient(services => services.GetRequiredService<OpenAIClient>().AsChatClient(modelConfig.Name))
        .UseLogging()
        .UseFunctionInvocation();
}
else if (!string.IsNullOrEmpty(modelConfig.DeepSeek))
{
    // Use Ollama client with DeepSeek endpoint
    var ollamaClient = new OllamaChatClient("https://api.deepseek.com", modelConfig.Name);
    builder.Services
        .AddSingleton(ollamaClient)
        .AddChatClient(services => services.GetRequiredService<OllamaChatClient>())
        .UseLogging()
        .UseFunctionInvocation();
}
else if (!string.IsNullOrEmpty(modelConfig.Anthropic))
{
    // Use Ollama client with Anthropic endpoint
    var ollamaClient = new OllamaChatClient("https://api.anthropic.com", modelConfig.Name);
    builder.Services
        .AddSingleton(ollamaClient)
        .AddChatClient(services => services.GetRequiredService<OllamaChatClient>())
        .UseLogging()
        .UseFunctionInvocation();
}
else if (!string.IsNullOrEmpty(modelConfig.Mistral))
{
    // Use Ollama client with Mistral endpoint
    var ollamaClient = new OllamaChatClient("https://api.mistral.ai", modelConfig.Name);
    builder.Services
        .AddSingleton(ollamaClient)
        .AddChatClient(services => services.GetRequiredService<OllamaChatClient>())
        .UseLogging()
        .UseFunctionInvocation();
}
else
{
    // No API keys found, exit with error
    Console.ForegroundColor = ConsoleColor.Red;
    Console.Error.WriteLine("Error: No API keys found. Please provide at least one API key in appsettings.json or environment variables.");
    Console.ResetColor();
    Environment.Exit(1);
}

var app = builder.Build();

var manager = app.Services.GetRequiredService<InterfaceManager>();

await manager.DisplayWelcome();
await manager.RunMainLoop();
