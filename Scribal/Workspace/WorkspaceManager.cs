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
    private const string StateFileName = "state.json"; // As per plans.md, this could be project_state.json, sticking to current code
    private const string PlotOutlineFileName = "plot_outline.json";

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
        // Config file path should be inside the workspace directory
        var configPath = fileSystem.Path.Join(workspace.FullName, ConfigFileName);

        var state = new WorkspaceState();
        // State file path should be inside the workspace directory
        // var statePath = fileSystem.Path.Join(workspace.FullName, StateFileName); // Not directly used, SaveWorkspaceStateAsync handles path
        
        if (!dry)
        {
            logger.LogDebug("Writing workspace config to {ConfigPath}", configPath);
            var jsonConfig = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            await fileSystem.File.WriteAllTextAsync(configPath, jsonConfig);
            
            // Use SaveWorkspaceStateAsync to ensure consistency and save to the correct location
            await SaveWorkspaceStateAsync(state, workspace.FullName);
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

        // .gitignore should ignore the config and state files within .scribal directory
        var gitignoreBuilder = new StringBuilder();
        // Paths in .gitignore are relative to the .gitignore file itself (which is in cwd)
        gitignoreBuilder.AppendLine($"{WorkspaceDirectoryName}/{ConfigFileName}");
        gitignoreBuilder.AppendLine($"{WorkspaceDirectoryName}/{StateFileName}");
        gitignoreBuilder.AppendLine($"{WorkspaceDirectoryName}/{PlotOutlineFileName}");
        // Potentially add other patterns to .gitignore later e.g. .scribal/vectors/

        var gitignoreContent = gitignoreBuilder.ToString();
        logger.LogDebug("Creating .gitignore file in {Directory} with content:\n{Content}", cwd, gitignoreContent);
        // CreateGitIgnore expects the content of the .gitignore file.
        // It will create/update .gitignore in the root of the repository (cwd).
        await git.CreateGitIgnore(gitignoreContent);
        
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
        
        if (fileSystem.File.Exists(path))
        {
            logger?.LogDebug("Found workspace config at {ConfigPath}", path);
            return path;
        }
        
        logger?.LogDebug("Workspace config not found at {ConfigPath}", path);
        return null;
    }

    public async Task<WorkspaceState?> LoadWorkspaceStateAsync(string? workspacePath = null,
        CancellationToken cancellationToken = default)
    {
        workspacePath ??= _workspace?.FullName ?? TryFindWorkspaceFolder(fileSystem, logger);

        if (string.IsNullOrEmpty(workspacePath))
        {
            logger.LogWarning("Cannot load workspace state, workspace directory not found.");
            return null;
        }

        var stateFilePath = fileSystem.Path.Join(workspacePath, StateFileName);

        if (!fileSystem.File.Exists(stateFilePath))
        {
            logger.LogDebug("Workspace state file not found at {StateFilePath}. Returning new state.", stateFilePath);
            return new WorkspaceState(); // Return a new empty state if file doesn't exist
        }

        try
        {
            var json = await fileSystem.File.ReadAllTextAsync(stateFilePath, cancellationToken);
            var state = JsonSerializer.Deserialize<WorkspaceState>(json);
            logger.LogInformation("Workspace state loaded from {StateFilePath}", stateFilePath);
            return state;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load or deserialize workspace state from {StateFilePath}", stateFilePath);
            return null; 
        }
    }

    public async Task SaveWorkspaceStateAsync(WorkspaceState state,
        string? workspacePath = null,
        CancellationToken cancellationToken = default)
    {
        workspacePath ??= _workspace?.FullName ?? TryFindWorkspaceFolder(fileSystem, logger);

        if (string.IsNullOrEmpty(workspacePath))
        {
            logger.LogError("Cannot save workspace state, workspace directory not found or not initialized.");
            return;
        }
        
        if (!fileSystem.Directory.Exists(workspacePath))
        {
            logger.LogDebug("Workspace directory {WorkspacePath} does not exist. Creating it.", workspacePath);
            fileSystem.Directory.CreateDirectory(workspacePath);
        }

        var stateFilePath = fileSystem.Path.Join(workspacePath, StateFileName);
        try
        {
            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
            await fileSystem.File.WriteAllTextAsync(stateFilePath, json, cancellationToken);
            logger.LogInformation("Workspace state saved to {StateFilePath}", stateFilePath);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save workspace state to {StateFilePath}", stateFilePath);
        }
    }

    public async Task SavePlotOutlineAsync(StoryOutline outline, string premise)
    {
        var workspacePath = _workspace?.FullName ?? TryFindWorkspaceFolder(fileSystem, logger);
        if (string.IsNullOrEmpty(workspacePath))
        {
            logger.LogError("Cannot save plot outline, workspace directory not found or not initialized.");
            return;
        }

        var plotOutlineFilePath = fileSystem.Path.Join(workspacePath, PlotOutlineFileName);
        try
        {
            var outlineJson = JsonSerializer.Serialize(outline, new JsonSerializerOptions { WriteIndented = true });
            await fileSystem.File.WriteAllTextAsync(plotOutlineFilePath, outlineJson);
            logger.LogInformation("Plot outline saved to {PlotOutlineFilePath}", plotOutlineFilePath);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save plot outline to {PlotOutlineFilePath}", plotOutlineFilePath);
            return; 
        }

        var state = await LoadWorkspaceStateAsync(workspacePath) ?? new WorkspaceState();

        state.Premise = premise;
        state.PlotOutlineFile = PlotOutlineFileName; 
        state.Chapters = outline.Chapters.Select(c => new ChapterState
        {
            Number = c.ChapterNumber,
            Title = c.Title,
            State = ChapterStateType.Unstarted 
        }).ToList();

        await SaveWorkspaceStateAsync(state, workspacePath);

        // Create chapter directories
        var projectRootPath = fileSystem.DirectoryInfo.New(workspacePath).Parent?.FullName;
        if (string.IsNullOrEmpty(projectRootPath))
        {
            logger.LogError("Could not determine project root path to create chapter directories.");
            return;
        }

        var mainChaptersDirectoryPath = fileSystem.Path.Join(projectRootPath, "chapters");
        if (!fileSystem.Directory.Exists(mainChaptersDirectoryPath))
        {
            logger.LogInformation("Creating main chapters directory at {MainChaptersDirectoryPath}", mainChaptersDirectoryPath);
            fileSystem.Directory.CreateDirectory(mainChaptersDirectoryPath);
        }

        foreach (var chapter in outline.Chapters.OrderBy(c => c.ChapterNumber))
        {
            // Format chapter directory name, e.g., "chapter_01", "chapter_02"
            var chapterDirectoryName = $"chapter_{chapter.ChapterNumber:D2}";
            var chapterSpecificDirectoryPath = fileSystem.Path.Join(mainChaptersDirectoryPath, chapterDirectoryName);

            if (!fileSystem.Directory.Exists(chapterSpecificDirectoryPath))
            {
                logger.LogInformation("Creating chapter directory at {ChapterSpecificDirectoryPath}", chapterSpecificDirectoryPath);
                fileSystem.Directory.CreateDirectory(chapterSpecificDirectoryPath);
            }
        }
    }
}
