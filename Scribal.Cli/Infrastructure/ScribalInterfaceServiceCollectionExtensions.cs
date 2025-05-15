using Microsoft.Extensions.DependencyInjection;
using Scribal.Cli.Features;
using Scribal.Workspace;
using Spectre.Console; // Added for ChapterManagerService

namespace Scribal.Cli;

public static class ScribalInterfaceServiceCollectionExtensions
{
    public static IServiceCollection AddScribalInterface(this IServiceCollection services)
    {
        services.AddSingleton<CancellationService>();
        services.AddSingleton<IUserInteraction, SpectreUserInteraction>();
        services.AddSingleton<CommandService>();
        services.AddSingleton<InterfaceManager>();
        services.AddSingleton<PitchService>();
        services.AddSingleton<OutlineService>(); // Added OutlineService
        services.AddSingleton<ChapterManagerService>();
        services.AddSingleton<ChapterDrafterService>(); // Added
        services.AddSingleton<IChapterDeletionService, ChapterDeletionService>(); // Added
        services.AddSingleton(AnsiConsole.Console);
        
        return services;
    }
}
