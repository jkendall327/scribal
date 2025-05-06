using Microsoft.Extensions.DependencyInjection;

namespace Scribal.Cli;

public static class ScribalInterfaceServiceCollectionExtensions
{
    public static IServiceCollection AddScribalInterface(this IServiceCollection services)
    {
        services.AddSingleton<CancellationService>();
        services.AddSingleton<CommandService>();
        services.AddSingleton<InterfaceManager>();

        return services;
    }
}