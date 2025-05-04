using System.IO.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Scribal;
using Scribal.AI;
using Scribal.Cli;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();

var modelConfiguration = new ConfigurationBuilder()
    .AddEnvironmentVariables()
    .AddUserSecrets<Program>()
    .Build();

builder.Services.AddScribalAi(modelConfiguration);

var filesystem = new FileSystem();
var cwd = filesystem.Directory.GetCurrentDirectory();

builder.Services.AddSingleton<IFileSystem>(filesystem);
builder.Services.AddSingleton<RepoMapStore>();
builder.Services.AddSingleton<IFunctionInvocationFilter, GitCommitFilter>();
builder.Services.AddSingleton<CancellationService>();
builder.Services.AddSingleton<CommandService>();
builder.Services.AddSingleton<FileReader>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<PromptBuilder>();
builder.Services.AddSingleton<IDocumentScanService, DocumentScanService>();
builder.Services.AddSingleton<InterfaceManager>();
builder.Services.AddSingleton<DiffEditor>();
builder.Services.AddSingleton<IAiChatService, AiChatService>();
builder.Services.AddSingleton<IGitService, GitService>(s => new(cwd));
builder.Services.AddSingleton<IChatSessionStore, InMemoryChatSessionStore>();

var app = builder.Build();

var cancellation = app.Services.GetRequiredService<CancellationService>();
cancellation.Initialise();

var manager = app.Services.GetRequiredService<InterfaceManager>();

await manager.DisplayWelcome();
await manager.RunMainLoop();