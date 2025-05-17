using Microsoft.Extensions.DependencyInjection;
using Scribal.Cli.Features; // AI: Ensures ChapterSplitterService from this namespace is found
using Scribal.Cli.Interface;
using Scribal.Workspace; // AI: Still needed for WorkspaceManager and other services
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
        services.AddSingleton<ChapterManagerService>();
        services.AddSingleton<ChapterDrafterService>();
        services.AddSingleton<NewChapterCreator>();
        services.AddSingleton<IChapterDeletionService, ChapterDeletionService>();
        services.AddSingleton<IChapterSplitterService, ChapterSplitterService>(); // AI: Added ChapterSplitterService registration
        services.AddSingleton<IChapterMergerService, ChapterMergerService>(); // AI: Added ChapterMergerService registration
        services.AddSingleton<WorkspaceDeleter>();
        services.AddSingleton<ExportService>();
        services.AddSingleton(AnsiConsole.Console);
        services.AddSingleton<ConsoleChatRenderer>();
    }
}
