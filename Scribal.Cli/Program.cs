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
builder.Services.AddScribal(new FileSystem(), TimeProvider.System);

// UI services
builder.Services.AddSingleton<CancellationService>();
builder.Services.AddSingleton<CommandService>();
builder.Services.AddSingleton<InterfaceManager>();

var app = builder.Build();

var git = app.Services.GetRequiredService<IGitService>();
var filesystem = app.Services.GetRequiredService<IFileSystem>();

git.Initialise(filesystem.Directory.GetCurrentDirectory());

var cancellation = app.Services.GetRequiredService<CancellationService>();
cancellation.Initialise();

var manager = app.Services.GetRequiredService<InterfaceManager>();

await manager.DisplayWelcome();
await manager.RunMainLoop();