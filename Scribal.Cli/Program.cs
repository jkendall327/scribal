using System.IO.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Scribal;
using Scribal.Cli;
using Scribal.Workspace;

// .NET looks for appsettings.json in the content root path,
// which Host.CreateApplicationBuilder sets as the current working directory.
// But our current working directory will almost always be somewhere different.
var contentRoot = Path.GetDirectoryName(AppContext.BaseDirectory);

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = contentRoot,
});

IncorporateConfigFromScribalWorkspace(builder);

builder.Logging.ClearProviders();

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

    host.Configuration.AddJsonFile(config, optional: true, reloadOnChange: true);
}