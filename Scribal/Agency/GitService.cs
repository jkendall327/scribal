using System.Diagnostics.CodeAnalysis;
using System.IO.Abstractions;
using LibGit2Sharp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Scribal.Config;

namespace Scribal.Agency;

public interface IGitServiceFactory
{
    bool TryOpenRepository([NotNullWhen(true)] out IGitService? service);
    bool TryOpenRepository(string repoPath, [NotNullWhen(true)] out IGitService? service);
    bool TryCreateAndOpenRepository(string repoPath, [NotNullWhen(true)] out IGitService? service);
    void DeleteRepository(string repoPath);
}

public class GitServiceFactory(
    TimeProvider time,
    IFileSystem fileSystem,
    IOptions<AppConfig> config,
    ILoggerFactory factory) : IGitServiceFactory
{
    public bool TryOpenRepository([NotNullWhen(true)] out IGitService? service)
    {
        var cwd = fileSystem.Directory.GetCurrentDirectory();
        return TryOpenRepository(cwd, out service);
    }
    
    public bool TryOpenRepository(string repoPath, [NotNullWhen(true)] out IGitService? service)
    {
        service = null;

        try
        {
            if (Repository.IsValid(repoPath))
            {
                service = BuildAssumingValid(repoPath);

                return true;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    public bool TryCreateAndOpenRepository(string repoPath, [NotNullWhen(true)] out IGitService? service)
    {
        Repository.Init(repoPath);

        return TryOpenRepository(repoPath, out service);
    }

    private GitService BuildAssumingValid(string repoPath)
    {
        var repo = new Repository(repoPath);

        var logger = factory.CreateLogger<GitService>();

        return new(repo, time, fileSystem, config, logger);
    }

    public void DeleteRepository(string repoPath)
    {
    }
}

public interface IGitService
{
    Task<string> GetCurrentBranch();
    Task<bool> CreateCommitAsync(string filepath, string message, CancellationToken ct = default);
    Task<bool> CreateCommitAsync(List<string> files, string message, CancellationToken ct = default);
    Task<bool> CreateCommitAllAsync(string message, CancellationToken ct = default);
    Task CreateGitIgnore(string gitignore);
}

public sealed class GitService(
    IRepository repo,
    TimeProvider time,
    IFileSystem fileSystem,
    IOptions<AppConfig> config,
    ILogger<GitService> logger) : IGitService, IDisposable
{
    private readonly string _name = repo.Config.GetValueOrDefault<string>("user.name");
    private readonly string _email = repo.Config.GetValueOrDefault<string>("user.email");

    public Task<string> GetCurrentBranch()
    {
        var name = repo.Head.FriendlyName;

        return Task.FromResult(name);
    }

    public async Task<bool> CreateCommitAsync(string filepath, string message, CancellationToken ct = default)
    {
        return await CreateCommitAsync([filepath], message, ct);
    }

    public Task<bool> CreateCommitAsync(List<string> files, string message, CancellationToken ct = default)
    {
        if (config.Value.DryRun)
        {
            logger.LogInformation("Dry run: Skipping commit");

            return Task.FromResult(true);
        }

        ct.ThrowIfCancellationRequested();

        try
        {
            foreach (var file in files)
            {
                Commands.Stage(repo, file);
                logger.LogInformation("Staged changes for {Filepath}", file);
            }

            var sig = new Signature($"{_name} (scribal)", _email, time.GetLocalNow());

            repo.Commit(message, sig, sig);

            logger.LogInformation("Committed changes with message: {Message}", message);

            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create commit");

            return Task.FromResult(false);
        }
    }

    public Task<bool> CreateCommitAllAsync(string message, CancellationToken ct = default)
    {
        if (config.Value.DryRun)
        {
            logger.LogInformation("Dry run: Skipping commit all with message: {Message}", message);

            return Task.FromResult(true);
        }

        ct.ThrowIfCancellationRequested();

        try
        {
            Commands.Stage(repo, "*"); // Stage all changes
            var sig = new Signature($"{_name} (scribal)", _email, time.GetLocalNow());
            repo.Commit(message, sig, sig);
            logger.LogInformation("Committed all staged changes with message: {Message}", message);

            return Task.FromResult(true);
        }
        catch (EmptyCommitException)
        {
            logger.LogInformation("No changes staged to commit for message: {Message}", message);

            return Task.FromResult(true); // Not an error, but nothing was committed.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create commit all with message: {Message}", message);

            return Task.FromResult(false);
        }
    }

    public async Task CreateGitIgnore(string gitignore)
    {
        var folder = repo.Info.Path;

        var root = fileSystem.DirectoryInfo.New(folder).Parent;

        if (root is null)
        {
            throw new InvalidOperationException("Couldn't find parent directory to the .git folder.");
        }

        var path = fileSystem.Path.Combine(root.FullName, ".gitignore");

        if (!config.Value.DryRun)
        {
            await fileSystem.File.WriteAllTextAsync(path, gitignore);
        }
    }

    public void Dispose()
    {
        repo.Dispose();
    }
}