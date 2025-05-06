using System.IO.Abstractions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Embeddings;
using Scribal;
using Scribal.Agency;
using Scribal.Cli;
using Scribal.Context;

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

var modelState = new ModelState
{
    ModelServiceId = "gemini",
    EmbeddingModelServiceId = "openai",
    WeakModelServiceId = "openai"
};

builder.Services.AddSingleton(modelState);

var modelConfiguration = new ModelConfiguration(builder.Configuration);

builder.Services.AddScribalAi(builder.Configuration, modelConfiguration);
builder.Services.AddScribal(builder.Configuration, new FileSystem(), TimeProvider.System);
builder.Services.AddScribalInterface();

var app = builder.Build();

await App.RunScribal(app);

return;

void IncorporateConfigFromScribalWorkspace(HostApplicationBuilder host)
{
    var current = Directory.GetCurrentDirectory();
    
    var workspace = Path.Join(current, ".scribal", "scribal.config");
    
    host.Configuration.AddJsonFile(workspace, optional: true, reloadOnChange: true);
}