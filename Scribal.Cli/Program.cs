using System.IO.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Scribal;
using Scribal.Cli;

var builder = Host.CreateApplicationBuilder(args);

var modelConfiguration = new ConfigurationBuilder()
    .AddEnvironmentVariables()
    .AddUserSecrets<Program>()
    .Build();

builder.Services.AddScribalAi(modelConfiguration);

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
builder.Services.AddSingleton<IAiChatService, AiChatService>();
builder.Services.AddSingleton<IGitService, GitService>(s => new(Directory.GetCurrentDirectory()));
builder.Services.AddSingleton<IChatSessionStore, InMemoryChatSessionStore>();

var app = builder.Build();

var manager = app.Services.GetRequiredService<InterfaceManager>();

await manager.DisplayWelcome();
await manager.RunMainLoop();