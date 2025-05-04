using System.IO.Abstractions;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Scribal;
using Scribal.AI;
using Scribal.Cli;

// .NET looks for appsettings.json in the content root path,
// which Host.CreateApplicationBuilder sets as the current working directory.
// But our current working directory will almost always be somewhere different.
var contentRoot = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = contentRoot,
    EnvironmentName = "Development"
});

builder.Logging.ClearProviders();

builder.Services.AddScribalAi(builder.Configuration);

// Infrastructure
var filesystem = new FileSystem();
builder.Services.AddSingleton<IFileSystem>(filesystem);
builder.Services.AddSingleton(TimeProvider.System);

// Tools
builder.Services.AddSingleton<FileReader>();
builder.Services.AddSingleton<DiffEditor>();

// LLM interfacing
builder.Services.AddSingleton<IChatSessionStore, InMemoryChatSessionStore>();
builder.Services.AddSingleton<CancellationService>();
builder.Services.AddSingleton<IAiChatService, AiChatService>();

// UI
builder.Services.AddSingleton<CommandService>();
builder.Services.AddSingleton<InterfaceManager>();

// Context gathering
builder.Services.AddSingleton<RepoMapStore>();
builder.Services.AddSingleton<PromptRenderer>();
builder.Services.AddSingleton<PromptBuilder>();
builder.Services.AddSingleton<IDocumentScanService, DocumentScanService>();

// Other
builder.Services.AddSingleton<CommitGenerator>();
builder.Services.AddSingleton<IGitService, GitService>();

var app = builder.Build();

var git = app.Services.GetRequiredService<IGitService>();
git.Initialise(filesystem.Directory.GetCurrentDirectory());

var cancellation = app.Services.GetRequiredService<CancellationService>();
cancellation.Initialise();

var manager = app.Services.GetRequiredService<InterfaceManager>();

await manager.DisplayWelcome();
await manager.RunMainLoop();