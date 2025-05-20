using System.CommandLine.Invocation;
using System.IO.Abstractions;
using Microsoft.Extensions.Logging;
using Scribal.Agency;
using Scribal.Cli.Interface;
using Scribal.Workspace;
using Spectre.Console;

namespace Scribal.Cli.Features;

public class WorkspaceDeleter(
    WorkspaceManager workspaceManager,
    IFileSystem fileSystem,
    IUserInteraction userInteraction, // Renamed from interaction for consistency
    IGitServiceFactory gitFactory,
    ILogger<WorkspaceDeleter> logger)
{
    private readonly IUserInteraction _userInteraction = userInteraction;

    public async Task DeleteWorkspaceCommandAsync(InvocationContext context)
    {
        var workspacePath = workspaceManager.CurrentWorkspacePath;
        var cancellationToken = context.GetCancellationToken();

        if (string.IsNullOrWhiteSpace(workspacePath) || !fileSystem.Directory.Exists(workspacePath))
        {
            await _userInteraction.NotifyAsync("No .scribal workspace found to delete in the current project structure.", new(MessageType.Warning), cancellationToken);
            return;
        }

        await _userInteraction.NotifyAsync($"Workspace found at: {Markup.Escape(workspacePath)}", new(MessageType.Warning), cancellationToken);

        var ok = await _userInteraction.ConfirmAsync(
            $"[bold red]Are you sure you want to delete the .scribal workspace at '{Markup.Escape(workspacePath)}'? This action cannot be undone.[/]", cancellationToken);

        if (!ok)
        {
            await _userInteraction.NotifyAsync(".scribal workspace deletion cancelled by user.", new(MessageType.Warning), cancellationToken);
            return;
        }

        try
        {
            await workspaceManager.DeleteWorkspaceAsync();

            await _userInteraction.NotifyAsync(
                $".scribal workspace at '{Markup.Escape(workspacePath)}' deleted successfully.", new(MessageType.Informational), cancellationToken);

            logger.LogInformation(".scribal workspace at {WorkspacePath} deleted successfully", workspacePath);

            if (gitFactory.TryOpenRepository(out var git))
            {
                var commitMessage = "Deleted .scribal workspace";
                logger.LogInformation("Attempting to commit deletion of workspace: {WorkspacePath}", workspacePath);

                var commitSuccess = await git.CreateCommitAsync(workspacePath, commitMessage, cancellationToken);

                if (commitSuccess)
                {
                    await _userInteraction.NotifyAsync(
                        $"Committed workspace deletion to git: {Markup.Escape(commitMessage)}", new(MessageType.Informational), cancellationToken);

                    logger.LogInformation("Successfully committed deletion of workspace {WorkspacePath}",
                        workspacePath);
                }
                else
                {
                    await _userInteraction.NotifyAsync(
                        $"Failed to commit workspace deletion for {Markup.Escape(workspacePath)} to git.", new(MessageType.Error), cancellationToken);

                    logger.LogWarning("Failed to commit deletion of workspace {WorkspacePath}", workspacePath);
                }
            }
            else
            {
                logger.LogInformation(
                    "Git service not enabled. Skipping commit for workspace deletion at {WorkspacePath}",
                    workspacePath);
            }

            var projectRootPath = fileSystem.DirectoryInfo.New(workspacePath).Parent?.FullName;

            if (!string.IsNullOrWhiteSpace(projectRootPath))
            {
                var gitFolderPath = fileSystem.Path.Combine(projectRootPath, ".git");

                if (fileSystem.Directory.Exists(gitFolderPath))
                {
                    await _userInteraction.NotifyAsync($"A .git folder was found at: {Markup.Escape(gitFolderPath)}", new(MessageType.Warning), cancellationToken);

                    var deleteGit = await _userInteraction.ConfirmAsync(
                        $"[bold red]Do you also want to delete the .git folder at '{Markup.Escape(gitFolderPath)}'? This will remove all version history for the project and cannot be undone.[/]", cancellationToken);

                    if (!deleteGit)
                    {
                        await _userInteraction.NotifyAsync(".git folder deletion skipped by user.", new(MessageType.Warning), cancellationToken);
                        return;
                    }

                    try
                    {
                        gitFactory.DeleteRepository(gitFolderPath);

                        await _userInteraction.NotifyAsync(
                            $".git folder at '{Markup.Escape(gitFolderPath)}' deleted successfully.", new(MessageType.Informational), cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        await _userInteraction.NotifyError($"Failed to delete .git folder: {Markup.Escape(ex.Message)}", ex, cancellationToken);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            await _userInteraction.NotifyError($"Failed to delete .scribal workspace: {Markup.Escape(ex.Message)}", ex, cancellationToken);
        }
    }
}