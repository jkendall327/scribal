using System.IO.Abstractions;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Scribal.Agency;

namespace Scribal.Workspace;

public record WorkspaceCreation(bool Created, bool GitRepoInitialised);

public class WorkspaceManager(
    IFileSystem fileSystem, 
    IGitService git, 
    IUserInteraction interaction, 
    IOptions<AppConfig> options,
    ILogger<WorkspaceManager> logger)
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
            logger.LogDebug("Workspace already initialized");
            return new(false, false);
        }

        var dry = options.Value.DryRun;
        
        var cwd = fileSystem.Directory.GetCurrentDirectory();
        logger.LogInformation("Initializing workspace in {CurrentDirectory}", cwd);

        var dir = fileSystem.Path.Join(cwd, WorkspaceDirectoryName);

        var workspace = fileSystem.DirectoryInfo.New(dir);

        if (!dry)
        {
            logger.LogDebug("Creating workspace directory at {WorkspaceDirectory}", dir);
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
            logger.LogDebug("Writing workspace config to {ConfigPath}", configPath);
            await fileSystem.File.WriteAllTextAsync(configPath, json);
            
            logger.LogDebug("Writing workspace state to {StatePath}", statePath);
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
            logger.LogDebug("Git repository already enabled");
            return false;
        }

        var ok = await interaction.ConfirmAsync("Would you like to create a Git repo?");

        if (!ok)
        {
            logger.LogInformation("User declined Git repository creation");
            return false;
        }

        if (!dry)
        {
            logger.LogInformation("Creating Git repository in {Directory}", cwd);
            git.CreateRepository(cwd);
        }

        // Might want to add more stuff to this later.
        var gitignore = ConfigFileName;

        logger.LogDebug("Creating .gitignore file with {Content}", gitignore);
        await git.CreateGitIgnore(gitignore);
        
        logger.LogInformation("Git repository successfully initialized");
        return true;
    }

    public static string? TryFindWorkspaceFolder(IFileSystem fileSystem, ILogger? logger = null)
    {
        var dir = fileSystem.Directory.GetCurrentDirectory();
        logger?.LogDebug("Searching for workspace starting from {CurrentDirectory}", dir);

        while (dir is not null)
        {
            var path = fileSystem.Path.Combine(dir, WorkspaceDirectoryName);

            if (fileSystem.Directory.Exists(path))
            {
                logger?.LogDebug("Found workspace folder at {WorkspacePath}", path);
                return path;
            }

            dir = fileSystem.Directory.GetParent(dir)?.FullName;
        }

        logger?.LogDebug("No workspace folder found");
        return null;
    }

    public static string? TryFindWorkspaceConfig(IFileSystem fileSystem, ILogger? logger = null)
    {
        var dir = TryFindWorkspaceFolder(fileSystem, logger);

        if (dir is null)
        {
            logger?.LogDebug("No workspace folder found, cannot locate config");
            return null;
        }

        var path = fileSystem.Path.Combine(dir, ConfigFileName);
        logger?.LogDebug("Found workspace config at {ConfigPath}", path);

        return path;
    }
}
