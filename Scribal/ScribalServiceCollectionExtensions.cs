using System.IO.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Scribal.Agency;
using Scribal.AI;
using Scribal.Context;
using Scribal.Workspace;
using DiffEditor = Scribal.Agency.DiffEditor;

namespace Scribal;

public static class ScribalServiceCollectionExtensions
{
    public static IServiceCollection AddScribal(this IServiceCollection services,
        IConfiguration config,
        IFileSystem fileSystem,
        TimeProvider time)
    {
        services.Configure<AppConfig>(config.GetSection("AppConfig"));
        
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
        services.AddSingleton<MarkdownIngestor>();
        
        // Workspace
        services.AddSingleton<WorkspaceManager>();

        // Other
        services.AddSingleton<CommitGenerator>();
        services.AddSingleton<IGitService, GitService>();

        return services;
    }
}