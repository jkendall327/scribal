using System.IO.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Scribal;
using Scribal.Cli;
using Scribal.Cli.Infrastructure;
using Scribal.Workspace;
using Serilog;

// .NET looks for appsettings.json in the content root path,
// which Host.CreateApplicationBuilder sets as the current working directory.
// But our current working directory will almost always be somewhere different.
var contentRoot = Path.GetDirectoryName(AppContext.BaseDirectory);

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = contentRoot
});

IncorporateConfigFromScribalWorkspace(builder);

SetupLogging(builder);

builder.Services.AddScribalAi(builder.Configuration);
builder.Services.AddScribal(builder.Configuration, new FileSystem(), TimeProvider.System);
builder.Services.AddScribalInterface();

var app = builder.Build();

await App.RunScribal(app);

return;

void IncorporateConfigFromScribalWorkspace(HostApplicationBuilder host)
{
    var config = WorkspaceManager.TryFindWorkspaceConfig(new FileSystem());

    if (config == null)
    {
        return;
    }

    host.Configuration.AddJsonFile(config, true, true);
}

void SetupLogging(HostApplicationBuilder host)
{
    var path = Path.Combine(host.Environment.ContentRootPath, "logs");

    Log.Logger = new LoggerConfiguration().MinimumLevel.Debug()
                                          .WriteTo.File($"{path}/log-.txt",
                                              rollingInterval: RollingInterval.Day,
                                              retainedFileCountLimit: 7,
                                              outputTemplate:
                                              "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                                          .Enrich.FromLogContext()
                                          .CreateLogger();

    host.Logging.ClearProviders();
    host.Logging.AddSerilog();
}