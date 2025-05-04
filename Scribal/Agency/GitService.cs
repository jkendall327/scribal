using System.Diagnostics.CodeAnalysis;
using LibGit2Sharp;
using Microsoft.Extensions.Logging;

namespace Scribal.Cli;

public interface IGitService
{
    public bool Enabled { get; }
    void Initialise(string path);
    Task<string> GetRepoName();
    Task<List<string>> GetBranches();
    Task<string> GetCurrentBranch();
    Task<string> GetCurrentCommit();
    Task<bool> StageChange();
    Task<bool> CreateCommit(string filepath, string message);
}

public sealed class GitService(ILogger<GitService> logger) : IGitService, IDisposable
{
    private Repository? _repo;

    public bool Enabled => _repo is not null;
    
    public void Initialise(string path)
    {
        try
        {
            _repo = new(path);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to initialise git repository");
        }
    }
    
    [MemberNotNull(nameof(_repo))]
    private void EnsureValidRepository()
    {
        if (_repo is null)
        {
            throw new InvalidOperationException("Not in a valid Git repository.");
        }
    }
    
    public Task<string> GetRepoName()
    {
        throw new NotImplementedException();
    }

    public Task<List<string>> GetBranches()
    {
        throw new NotImplementedException();
    }

    public Task<string> GetCurrentBranch()
    {
        EnsureValidRepository();

        var name = _repo.Head.FriendlyName;
        return Task.FromResult(name);
    }

    public Task<string> GetCurrentCommit()
    {
        throw new NotImplementedException();
    }

    public Task<bool> StageChange()
    {
        throw new NotImplementedException();
    }

    public Task<bool> CreateCommit(string filepath, string message)
    {
        Commands.Stage(_repo, filepath);
        var sig = new Signature("AI-assistant", "ai@example.com", DateTimeOffset.Now);
        _repo.Commit($"AI edited {filepath}", sig, sig);
        
        return Task.FromResult(true);
    }

    public void Dispose()
    {
        _repo?.Dispose();
    }
}