using LibGit2Sharp;

namespace Scribal.Cli;

public interface IGitService
{
    Task<string> GetRepoName();
    Task<List<string>> GetBranches();
    Task<string> GetCurrentBranch();
    Task<string> GetCurrentCommit();
    Task<bool> StageChange();
    Task<bool> CreateCommit();
}

public sealed class GitService(string path) : IGitService, IDisposable
{
    private readonly Repository _repo = new(path);

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

    public Task<bool> CreateCommit()
    {
        throw new NotImplementedException();
    }

    public void Dispose()
    {
        _repo.Dispose();
    }
}