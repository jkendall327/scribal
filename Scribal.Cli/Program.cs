using System.IO.Abstractions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.SemanticKernel;
using OpenAI;
using Scribal.Cli;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddUserSecrets<Program>();

builder.Services.AddSingleton<Kernel>(sp =>
{
    var kb = Kernel.CreateBuilder();

    var cfg = builder.Configuration;

    kb.AddOpenAIChatCompletion(modelId: cfg["OpenAI:Model"] ?? "gpt-4o-mini",
        apiKey: cfg["OpenAI:ApiKey"],
        serviceId: "openai");

#pragma warning disable SKEXP0070 // experimental attribute until GA
    kb.AddGoogleAIGeminiChatCompletion(modelId: cfg["Gemini:Model"] ?? "gemini-1.5-pro",
        apiKey: cfg["Gemini:ApiKey"],
        serviceId: "gemini");
#pragma warning restore SKEXP0070

#pragma warning disable SKEXP0010 // “other OpenAI-style” endpoint
    kb.AddOpenAIChatCompletion(modelId: cfg["DeepSeek:Model"] ?? "deepseek-chat",
        apiKey: cfg["DeepSeek:ApiKey"],
        endpoint: new Uri("https://api.deepseek.com"),
        serviceId: "deepseek");
#pragma warning restore SKEXP0010

    kb.Plugins.AddFromType<FileReader>("FileReader");
    kb.Plugins.AddFromType<DiffService>("DiffEditor");

    return kb.Build();
});

var modelConfig = new ModelConfiguration();
builder.Configuration.GetSection(ModelConfiguration.SectionName).Bind(modelConfig);

var filesystem = new FileSystem();

builder.Services.AddSingleton<IFileSystem>(filesystem);
builder.Services.AddSingleton<RepoMapStore>();
builder.Services.AddSingleton<CommandService>();
builder.Services.AddSingleton<FileReader>();
builder.Services.AddSingleton<PromptBuilder>();
builder.Services.AddSingleton<IDocumentScanService, DocumentScanService>();
builder.Services.AddSingleton<InterfaceManager>();
builder.Services.AddSingleton<DiffService>();
builder.Services.AddSingleton<IConversationStore, ConversationStore>();
builder.Services.AddSingleton<IModelClient, ModelClient>();
builder.Services.AddSingleton<IGitService, GitService>(s => new(Directory.GetCurrentDirectory()));
builder.Services.AddSingleton(modelConfig);

// builder.Services.AddSingleton(new OpenAIClient(Environment.GetEnvironmentVariable("OPENAI_API_KEY")));
// builder.Services.AddChatClient(services => services.GetRequiredService<OpenAIClient>().AsChatClient("gpt-4o-mini"))
//     .UseLogging()
//     .UseFunctionInvocation();

var app = builder.Build();

var manager = app.Services.GetRequiredService<InterfaceManager>();

await manager.DisplayWelcome();
await manager.RunMainLoop();