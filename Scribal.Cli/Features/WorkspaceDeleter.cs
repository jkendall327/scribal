using System.CommandLine.Invocation;
using System.IO.Abstractions;
using Scribal.Workspace;
using Spectre.Console;

namespace Scribal.Cli;

public class WorkspaceDeleter(
    WorkspaceManager workspaceManager,
    IFileSystem fileSystem,
    IUserInteraction interaction,
    IAnsiConsole console)
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
            fileSystem.Directory.Delete(workspacePath, true);

            console.MarkupLine(
                $"[green].scribal workspace at '{Markup.Escape(workspacePath)}' deleted successfully.[/]");

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
                        fileSystem.Directory.Delete(gitFolderPath, true);

                        console.MarkupLine(
                            $"[green].git folder at '{Markup.Escape(gitFolderPath)}' deleted successfully.[/]");
                    }
                    catch (Exception ex)
                    {
                        console.MarkupLine($"[red]Failed to delete .git folder: {Markup.Escape(ex.Message)}[/]");

                        console.WriteException(ex);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            console.MarkupLine($"[red]Failed to delete .scribal workspace: {Markup.Escape(ex.Message)}[/]");
            console.WriteException(ex);
        }
    }
}