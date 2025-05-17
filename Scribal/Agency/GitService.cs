using System.Diagnostics.CodeAnalysis;
using System.IO.Abstractions;
using LibGit2Sharp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Scribal.Config;

namespace Scribal.Agency;

public interface IGitService
{
    public bool Enabled { get; }
    void Initialise(string path);
    void CreateRepository(string path);
    Task<string> GetCurrentBranch();
    Task<bool> CreateCommitAsync(string filepath, string message, CancellationToken ct = default);
    Task<bool> CreateCommitAsync(List<string> files, string message, CancellationToken ct = default);
    Task<bool> CreateCommitAllAsync(string message, CancellationToken ct = default);
    Task CreateGitIgnore(string gitignore);
}

public sealed class GitService(
    TimeProvider time,
    IFileSystem fileSystem,
    IOptions<AppConfig> config,
    ILogger<GitService> logger) : IGitService, IDisposable
{
    private Repository? _repo;
    private string? _name;
    private string? _email;

    public bool Enabled => _repo is not null;

    public void Initialise(string path)
    {
        // TODO: use Repository.IsValid instead of throwing?

        try
        {
            _repo = new(path);

            _name = _repo.Config.GetValueOrDefault<string>("user.name");
            _email = _repo.Config.GetValueOrDefault<string>("user.email");

            if (string.IsNullOrEmpty(_name) || string.IsNullOrEmpty(_email))
            {
                // TODO surface this to the user or make it optional.
                throw new InvalidOperationException("No username or email set in git config.");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to initialise git repository");
        }
    }

    public void CreateRepository(string path)
    {
        Repository.Init(path);
        Initialise(path);
    }

    [MemberNotNull(nameof(_repo))]
    [MemberNotNull(nameof(_name))]
    [MemberNotNull(nameof(_email))]
    private void EnsureValidRepository()
    {
        if (_repo is null)
        {
            throw new InvalidOperationException("Not in a valid Git repository.");
        }

        if (_name is null || _email is null)
        {
            throw new InvalidOperationException("No username or email set in git config.");
        }
    }

    public Task<string> GetCurrentBranch()
    {
        EnsureValidRepository();

        var name = _repo.Head.FriendlyName;

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
        EnsureValidRepository();

        try
        {
            foreach (var file in files)
            {
                Commands.Stage(_repo, file);
                logger.LogInformation("Staged changes for {Filepath}", file);
            }
            
            var sig = new Signature($"{_name} (scribal)", _email, time.GetLocalNow());
            
            _repo.Commit(message, sig, sig);
            
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
        EnsureValidRepository();

        try
        {
            Commands.Stage(_repo, "*"); // Stage all changes
            var sig = new Signature($"{_name} (scribal)", _email, time.GetLocalNow());
            _repo.Commit(message, sig, sig);
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
        EnsureValidRepository();

        var folder = _repo.Info.Path;

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
        _repo?.Dispose();
    }
}