using System.IO.Abstractions;
using System.Reflection;
using Microsoft.Extensions.Configuration;
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
    ContentRootPath = contentRoot
});

builder.Logging.ClearProviders();

// Without the appsettings.json for priority reasons...
var modelConfiguration = new ConfigurationBuilder()
    .AddEnvironmentVariables()
    .AddUserSecrets<Program>()
    .Build();

builder.Services.AddScribalAi(modelConfiguration);

var filesystem = new FileSystem();

builder.Services.AddSingleton<IFileSystem>(filesystem);
builder.Services.AddSingleton<RepoMapStore>();
builder.Services.AddSingleton<CommitGenerator>();
builder.Services.AddSingleton<CancellationService>();
builder.Services.AddSingleton<CommandService>();
builder.Services.AddSingleton<PromptRenderer>();
builder.Services.AddSingleton<FileReader>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<PromptBuilder>();
builder.Services.AddSingleton<IDocumentScanService, DocumentScanService>();
builder.Services.AddSingleton<InterfaceManager>();
builder.Services.AddSingleton<DiffEditor>();
builder.Services.AddSingleton<IAiChatService, AiChatService>();
builder.Services.AddSingleton<IGitService, GitService>();
builder.Services.AddSingleton<IChatSessionStore, InMemoryChatSessionStore>();

var app = builder.Build();

var git = app.Services.GetRequiredService<IGitService>();
git.Initialise(filesystem.Directory.GetCurrentDirectory());

var cancellation = app.Services.GetRequiredService<CancellationService>();
cancellation.Initialise();

var manager = app.Services.GetRequiredService<InterfaceManager>();

await manager.DisplayWelcome();
await manager.RunMainLoop();