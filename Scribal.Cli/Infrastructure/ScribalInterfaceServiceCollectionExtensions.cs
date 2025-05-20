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
        services.AddSingleton<StickyTreeSelector>(); // Added StickyTreeSelector
        // services.AddSingleton(AnsiConsole.Console); // Keep for SpectreUserInteraction if it needs direct IAnsiConsole
        // services.AddSingleton<ConsoleChatRenderer>(); // Keep for SpectreUserInteraction if it needs it
        
        // Ensure IUserInteraction is the primary interface for console interactions.
        // SpectreUserInteraction itself will take IAnsiConsole and ConsoleChatRenderer if it needs to delegate.
        // If a service was missed in a previous refactoring step and still directly depends on IAnsiConsole 
        // or ConsoleChatRenderer (and isn't SpectreUserInteraction), that's an issue.
        // However, the existing registrations of IAnsiConsole and ConsoleChatRenderer are fine
        // as long as SpectreUserInteraction is the one consuming them.
    }
}