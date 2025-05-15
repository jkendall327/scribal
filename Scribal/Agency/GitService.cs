using System.Diagnostics.CodeAnalysis;
using System.IO.Abstractions;
using LibGit2Sharp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Scribal.Agency;

public interface IGitService
{
    public bool Enabled { get; }
    void Initialise(string path);
    void CreateRepository(string path);
    Task<string> GetCurrentBranch();
    Task<bool> CreateCommit(string filepath, string message);
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

    public Task<string> GetCurrentCommit()
    {
        EnsureValidRepository();

        return Task.FromResult(_repo.Head.Tip.MessageShort);
    }

    public Task<bool> StageChange()
    {
        throw new NotImplementedException();
    }

    public Task<bool> CreateCommit(string filepath, string message)
    {
        if (config.Value.DryRun)
        {
            return Task.FromResult(true);
        }

        EnsureValidRepository();

        Commands.Stage(_repo, filepath);

        var sig = new Signature($"{_name} (scribal)", _email, time.GetLocalNow());

        _repo.Commit(message, sig, sig);

        return Task.FromResult(true);
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

    public void Dispose() => _repo?.Dispose();
}