using System.IO.Abstractions;
using System.Text.Json;

namespace Scribal.Workspace;

public class WorkspaceManager(IFileSystem fileSystem)
{
    public bool InWorkspace => _workspace is not null;
    public bool Headless => !InWorkspace;

    private IDirectoryInfo? _workspace;
    
    public async Task CheckForWorkspace()
    {
        var workspace = TryFindWorkspaceFolder();

        if (workspace is null)
        {
            return;
        }
        
        _workspace = fileSystem.DirectoryInfo.New(workspace);
    }

    public async Task InitialiseWorkspace()
    {
        if (_workspace is not null)
        {
            return;
        }
        
        var cwd = fileSystem.Directory.GetCurrentDirectory();
        
        var dir = fileSystem.Path.Join(cwd, ".scribal");
        
        var workspace = fileSystem.DirectoryInfo.New(dir);
        
        workspace.Create();

        var config = new WorkspaceConfig();
        var json = JsonSerializer.Serialize(config);
        var configPath = fileSystem.Path.Join(cwd, ".scribal.config.json");
        await fileSystem.File.WriteAllTextAsync(configPath, json);
        
        var state = new WorkspaceState();
        var statePath = fileSystem.Path.Join(cwd, ".scribal.state.json");
        var stateJson = JsonSerializer.Serialize(state);
        await fileSystem.File.WriteAllTextAsync(statePath, stateJson);
        
        // TODO: init a git repo if user consents. create a gitignore for them?
        
        _workspace = workspace;
    }

    public string? TryFindWorkspaceFolder()
    {
        var dir = fileSystem.Directory.GetCurrentDirectory();

        while (dir is not null)
        {
            var path = fileSystem.Path.Combine(dir, ".scribal");
        
            if (fileSystem.Directory.Exists(path))
            {
                return path;
            }

            dir = fileSystem.Directory.GetParent(dir)?.FullName;
        }

        return null;
    }
    
    public string? TryFindWorkspaceConfig()
    {
        var dir = TryFindWorkspaceFolder();

        if (dir is null)
        {
            return null;
        }
        
        var path = fileSystem.Path.Combine(dir, "scribal.config");

        return path;
    }
}