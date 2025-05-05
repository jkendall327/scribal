using System.Diagnostics.CodeAnalysis;
using LibGit2Sharp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Scribal.Agency;

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

public sealed class GitService(TimeProvider time, IConfiguration configuration, ILogger<GitService> logger) : IGitService, IDisposable
{
    private Repository? _repo;
    private string? _name;
    private string? _email;
    
    public bool Enabled => _repo is not null;
    
    public void Initialise(string path)
    {
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
        EnsureValidRepository();
        
        return Task.FromResult(_repo.Head.Tip.MessageShort);
    }

    public Task<bool> StageChange()
    {
        throw new NotImplementedException();
    }

    public Task<bool> CreateCommit(string filepath, string message)
    {
        var dry = configuration.GetValue<bool>("DryRun");

        if (dry)
        {
            return Task.FromResult(true);
        }
        
        EnsureValidRepository();
        
        Commands.Stage(_repo, filepath);

        var sig = new Signature($"{_name} (scribal)", _email, time.GetLocalNow());

        _repo.Commit(message, sig, sig);
        
        return Task.FromResult(true);
    }

    public void Dispose()
    {
        _repo?.Dispose();
    }
}