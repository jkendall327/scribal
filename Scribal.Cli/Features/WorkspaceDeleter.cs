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
    IUserInteraction interaction,
    IAnsiConsole console,
    IGitServiceFactory gitFactory,
    ILogger<WorkspaceDeleter> logger)
{
    public async Task DeleteWorkspaceCommandAsync(InvocationContext context)
    {
        var workspacePath = workspaceManager.CurrentWorkspacePath;

        if (string.IsNullOrWhiteSpace(workspacePath) || !fileSystem.Directory.Exists(workspacePath))
        {
            console.MarkupLine("[yellow]No .scribal workspace found to delete in the current project structure.[/]");

            return;
        }

        console.MarkupLine($"[yellow]Workspace found at: {Markup.Escape(workspacePath)}[/]");

        var ok = await interaction.ConfirmAsync(
            $"[bold red]Are you sure you want to delete the .scribal workspace at '{Markup.Escape(workspacePath)}'? This action cannot be undone.[/]");

        if (!ok)
        {
            console.MarkupLine("[yellow].scribal workspace deletion cancelled by user.[/]");

            return;
        }

        try
        {
            await workspaceManager.DeleteWorkspaceAsync();

            console.MarkupLine(
                $"[green].scribal workspace at '{Markup.Escape(workspacePath)}' deleted successfully.[/]");

            logger.LogInformation(".scribal workspace at {WorkspacePath} deleted successfully", workspacePath);

            if (gitFactory.TryOpenRepository(out var git))
            {
                var cancellationToken = context.GetCancellationToken();
                var commitMessage = "Deleted .scribal workspace";
                logger.LogInformation("Attempting to commit deletion of workspace: {WorkspacePath}", workspacePath);

                var commitSuccess = await git.CreateCommitAsync(workspacePath, commitMessage, cancellationToken);

                if (commitSuccess)
                {
                    console.MarkupLine(
                        $"[green]Committed workspace deletion to git: {Markup.Escape(commitMessage)}[/]");

                    logger.LogInformation("Successfully committed deletion of workspace {WorkspacePath}",
                        workspacePath);
                }
                else
                {
                    console.MarkupLine(
                        $"[red]Failed to commit workspace deletion for {Markup.Escape(workspacePath)} to git.[/]");

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
                    console.MarkupLine($"[yellow]A .git folder was found at: {Markup.Escape(gitFolderPath)}[/]");

                    var deleteGit = await interaction.ConfirmAsync(
                        $"[bold red]Do you also want to delete the .git folder at '{Markup.Escape(gitFolderPath)}'? This will remove all version history for the project and cannot be undone.[/]");

                    if (!deleteGit)
                    {
                        console.MarkupLine("[yellow].git folder deletion skipped by user.[/]");

                        return;
                    }

                    try
                    {
                        gitFactory.DeleteRepository(gitFolderPath);

                        console.MarkupLine(
                            $"[green].git folder at '{Markup.Escape(gitFolderPath)}' deleted successfully.[/]");
                    }
                    catch (Exception ex)
                    {
                        console.MarkupLine($"[red]Failed to delete .git folder: {Markup.Escape(ex.Message)}[/]");

                        ExceptionDisplay.DisplayException(ex, console);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            console.MarkupLine($"[red]Failed to delete .scribal workspace: {Markup.Escape(ex.Message)}[/]");
            ExceptionDisplay.DisplayException(ex, console);
        }
    }
}