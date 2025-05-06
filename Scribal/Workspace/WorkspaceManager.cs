using System.IO.Abstractions;
using System.Text.Json;
using Scribal.Agency;

namespace Scribal.Workspace;

public class WorkspaceManager(IFileSystem fileSystem, GitService git, IUserInteraction interaction)
{
    private const string WorkspaceDirectoryName = ".scribal";
    private const string ConfigFileName = "config.json";
    private const string StateFileName = "state.json";

    public bool InWorkspace => _workspace is not null;
    public bool Headless => !InWorkspace;

    private IDirectoryInfo? _workspace;

    public async Task CheckForWorkspace()
    {
        var workspace = TryFindWorkspaceFolder(fileSystem);

        if (workspace is null)
        {
            return;
        }

        _workspace = fileSystem.DirectoryInfo.New(workspace);
    }

    public async Task<bool> InitialiseWorkspace()
    {
        if (_workspace is not null)
        {
            return false;
        }

        var cwd = fileSystem.Directory.GetCurrentDirectory();

        var dir = fileSystem.Path.Join(cwd, WorkspaceDirectoryName);

        var workspace = fileSystem.DirectoryInfo.New(dir);

        workspace.Create();

        var config = new WorkspaceConfig();
        var json = JsonSerializer.Serialize(config);
        var configPath = fileSystem.Path.Join(cwd, ConfigFileName);
        await fileSystem.File.WriteAllTextAsync(configPath, json);

        var state = new WorkspaceState();
        var statePath = fileSystem.Path.Join(cwd, StateFileName);
        var stateJson = JsonSerializer.Serialize(state);
        await fileSystem.File.WriteAllTextAsync(statePath, stateJson);

        // TODO: init a git repo if user consents. create a gitignore for them?

        var ok = await interaction.ConfirmAsync("Would you like to create a Git repo?");

        if (ok)
        {
        }

        _workspace = workspace;

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