using Microsoft.Extensions.DependencyInjection;
using Scribal.Cli.Features;
using Scribal.Cli.Interface;
using Scribal.Workspace;
using Spectre.Console;

namespace Scribal.Cli.Infrastructure;

public static class ScribalInterfaceServiceCollectionExtensions
{
    public static void AddScribalInterface(this IServiceCollection services)
    {
        services.AddSingleton<IUserInteraction, SpectreUserInteraction>();
        services.AddSingleton<CommandService>();
        services.AddSingleton<InterfaceManager>();
        services.AddSingleton<PitchService>();
        services.AddSingleton<OutlineService>();
        services.AddSingleton<IRefinementService, RefinementService>();
        services.AddSingleton<ChapterManagerService>();
        services.AddSingleton<ChapterDrafterService>();
        services.AddSingleton<NewChapterCreator>();
        services.AddSingleton<IChapterDeletionService, ChapterDeletionService>();

        services.AddSingleton<IChapterSplitterService, ChapterSplitterService>();
        services.AddSingleton<IChapterMergerService, ChapterMergerService>();
        services.AddSingleton<WorkspaceDeleter>();
        services.AddSingleton<ExportService>();
        services.AddSingleton(AnsiConsole.Console);
        services.AddSingleton<ConsoleChatRenderer>();
    }
}