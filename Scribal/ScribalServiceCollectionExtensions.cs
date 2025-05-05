using System.IO.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Scribal.Agency;
using Scribal.AI;
using Scribal.Context;
using DiffEditor = Scribal.Agency.DiffEditor;

namespace Scribal;

public static class ScribalServiceCollectionExtensions
{
    public static IServiceCollection AddScribal(this IServiceCollection services, IFileSystem fileSystem, TimeProvider time)
    {
        // Infrastructure
        services.AddSingleton(fileSystem);
        services.AddSingleton(time);

        // Tools
        services.AddSingleton<FileReader>();
        services.AddSingleton<DiffEditor>();

        // LLM interfacing
        services.AddSingleton<IChatSessionStore, InMemoryChatSessionStore>();
        services.AddSingleton<IAiChatService, AiChatService>();

        // Context gathering
        services.AddSingleton<RepoMapStore>();
        services.AddSingleton<PromptRenderer>();
        services.AddSingleton<PromptBuilder>();
        services.AddSingleton<IDocumentScanService, DocumentScanService>();

        // Other
        services.AddSingleton<CommitGenerator>();
        services.AddSingleton<IGitService, GitService>();

        return services;
    }
}