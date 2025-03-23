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

public class GitService : IGitService
{
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
        throw new NotImplementedException();
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
}