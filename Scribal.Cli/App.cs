using System.IO.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Scribal.Agency;
using Scribal.Context;

namespace Scribal.Cli;

public static class App
{
    public static async Task RunScribal(IHost app)
    {
        var filesystem = app.Services.GetRequiredService<IFileSystem>();
        var cwd = filesystem.DirectoryInfo.New(filesystem.Directory.GetCurrentDirectory());

        var ingestor = app.Services.GetRequiredService<MarkdownIngestor>();

        var config = app.Services.GetRequiredService<IOptions<AppConfig>>();
        var state = app.Services.GetRequiredService<IOptions<AiSettings>>();

        if (config.Value.IngestContent && state.Value.Embeddings is null)
        {
            await ingestor.IngestAllMarkdown(cwd, SearchOption.AllDirectories);
        }

        var git = app.Services.GetRequiredService<IGitService>();
        git.Initialise(filesystem.Directory.GetCurrentDirectory());

        var cancellation = app.Services.GetRequiredService<CancellationService>();
        cancellation.Initialise();

        var manager = app.Services.GetRequiredService<InterfaceManager>();

        await manager.DisplayWelcome();
        await manager.RunMainLoop();
    }
}