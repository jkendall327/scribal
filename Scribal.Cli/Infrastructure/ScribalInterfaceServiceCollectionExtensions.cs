using Microsoft.Extensions.DependencyInjection;
using Scribal.Cli.Features; // Added for ChapterManagerService

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
        services.AddSingleton<ChapterManagerService>(); // Added ChapterManagerService

        return services;
    }
}
