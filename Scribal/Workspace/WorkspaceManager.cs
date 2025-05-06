using System.IO.Abstractions;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Scribal.Agency;

namespace Scribal.Workspace;

public record WorkspaceCreation(bool Created, bool GitRepoInitialised);

public class WorkspaceManager(IFileSystem fileSystem, IGitService git, IUserInteraction interaction, IOptions<AppConfig> options)
{
    private const string WorkspaceDirectoryName = ".scribal";
    private const string ConfigFileName = "config.json";
    private const string StateFileName = "state.json";

    public bool InWorkspace => _workspace is not null;
    
    private IDirectoryInfo? _workspace;

    public async Task<WorkspaceCreation> InitialiseWorkspace()
    {
        if (_workspace is not null)
        {
            return new(false, false);
        }

        var dry = options.Value.DryRun;
        
        var cwd = fileSystem.Directory.GetCurrentDirectory();

        var dir = fileSystem.Path.Join(cwd, WorkspaceDirectoryName);

        var workspace = fileSystem.DirectoryInfo.New(dir);

        if (!dry)
        {
            workspace.Create();
        }

        var config = new WorkspaceConfig();
        var json = JsonSerializer.Serialize(config);
        var configPath = fileSystem.Path.Join(cwd, ConfigFileName);

        var state = new WorkspaceState();
        var statePath = fileSystem.Path.Join(cwd, StateFileName);
        var stateJson = JsonSerializer.Serialize(state);
        
        if (!dry)
        {
            await fileSystem.File.WriteAllTextAsync(configPath, json);
            await fileSystem.File.WriteAllTextAsync(statePath, stateJson);
        }

        var repoInitialised = await TryInitialiseGitRepo(dry, cwd);

        _workspace = workspace;

        return new(true, repoInitialised);
    }

    private async Task<bool> TryInitialiseGitRepo(bool dry, string cwd)
    {
        if (git.Enabled)
        {
            return false;
        }

        var ok = await interaction.ConfirmAsync("Would you like to create a Git repo?");

        if (!ok)
        {
            return false;
        }

        if (!dry)
        {
            git.CreateRepository(cwd);
        }

        // Might want to add more stuff to this later.
        var gitignore = ConfigFileName;

        await git.CreateGitIgnore(gitignore);
        
        return true;
    }

    public static string? TryFindWorkspaceFolder(IFileSystem fileSystem)
    {
        var dir = fileSystem.Directory.GetCurrentDirectory();

        while (dir is not null)
        {
            var path = fileSystem.Path.Combine(dir, WorkspaceDirectoryName);

            if (fileSystem.Directory.Exists(path))
            {
                return path;
            }

            dir = fileSystem.Directory.GetParent(dir)?.FullName;
        }

        return null;
    }

    public static string? TryFindWorkspaceConfig(IFileSystem fileSystem)
    {
        var dir = TryFindWorkspaceFolder(fileSystem);

        if (dir is null)
        {
            return null;
        }

        var path = fileSystem.Path.Combine(dir, ConfigFileName);

        return path;
    }
}