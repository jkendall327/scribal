using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.IO.Abstractions;
using Scribal.Agency;
using Scribal.AI;
using Scribal.Cli.Features;
using Scribal.Context;
using Scribal.Workspace;
using Spectre.Console;

namespace Scribal.Cli;

public class CommandService(
    IFileSystem fileSystem,
    RepoMapStore repoStore,
    IChatSessionStore conversationStore,
    PitchService pitchService,
    OutlineService outlineService,
    WorkspaceDeleter workspaceDeleter,
    WorkspaceManager workspaceManager,
    ChapterManagerService chapterManagerService,
    IGitService gitService)
{
    private readonly Argument<string> _ideaArgument = new()
    {
        Name = "Idea",
        Arity = ArgumentArity.ExactlyOne,
        Description = "Your basic idea for the story"
    };

    private readonly Argument<string> _premiseArgument = new()
    {
        Name = "Premise",
        Arity = ArgumentArity.ExactlyOne,
        Description = "The story premise to be turned into an outline"
    };

    private readonly Argument<string> _commitMessageArgument = new()
    {
        Name = "Message",
        Arity = ArgumentArity.ExactlyOne,
        Description = "The commit message for all staged files"
    };

    public Parser Build()
    {
        Command Create(string name, string description, Func<InvocationContext, Task> action, string[]? aliases = null)
        {
            var cmd = new Command(name, description);

            foreach (var alias in aliases ?? [])
            {
                cmd.AddAlias(alias);
            }

            cmd.SetHandler(action);

            return cmd;
        }

        var quit = Create("/quit", "Exit Scribal", QuitCommand, ["/exit"]);
        var clear = Create("/clear", "Clear conversation history", ClearCommand);
        var tree = Create("/tree", "Set files to be included in context", TreeCommand);
        var init = Create("/init", "Creates a new Scribal workspace in the current folder", InitCommand);

        var pitch = Create("/pitch", "Turns an initial story idea into a fleshed-out premise", PitchCommand);
        pitch.AddArgument(_ideaArgument);

        var outline = Create("/outline", "Generates a plot outline from a premise", OutlineCommand);
        outline.AddArgument(_premiseArgument);

        var chaptersCmd = Create("/chapters",
            "Manage chapters in the workspace",
            chapterManagerService.ManageChaptersAsync);

        var deleteWorkspaceCmd = Create("/delete",
            "Deletes the .scribal workspace and optionally the .git folder",
            workspaceDeleter.DeleteWorkspaceCommandAsync);

        var commitCmd = Create("/commit",
            "Stages all changes and creates a commit with the given message",
            CommitAllCommandAsync);

        commitCmd.AddArgument(_commitMessageArgument);

        var statusCmd = Create("/status", "Displays the current project status", StatusCommand);

        var root = new RootCommand("Scribal interactive shell")
        {
            init,
            statusCmd,
            clear,
            pitch,
            outline,
            chaptersCmd,
            deleteWorkspaceCmd,
            commitCmd,
            tree,
            quit
        };

        return new CommandLineBuilder(root).UseDefaults().UseHelp("/help").Build();
    }

    private async Task OutlineCommand(InvocationContext arg)
    {
        var premise = arg.ParseResult.GetValueForArgument(_premiseArgument);
        var token = arg.GetCancellationToken();

        if (string.IsNullOrWhiteSpace(premise))
        {
            AnsiConsole.MarkupLine("[red]Premise cannot be empty.[/]");

            return;
        }

        try
        {
            await outlineService.CreateOutlineFromPremise(premise, token);
        }
        catch (Exception e)
        {
            AnsiConsole.WriteException(e);
        }
    }

    private async Task CommitAllCommandAsync(InvocationContext context)
    {
        var message = context.ParseResult.GetValueForArgument(_commitMessageArgument);
        var token = context.GetCancellationToken();

        if (string.IsNullOrWhiteSpace(message))
        {
            AnsiConsole.MarkupLine("[red]Commit message cannot be empty.[/]");

            return;
        }

        if (!gitService.Enabled)
        {
            AnsiConsole.MarkupLine("[yellow]Git is not initialized for this workspace. Cannot commit.[/]");

            return;
        }

        try
        {
            AnsiConsole.MarkupLine($"Attempting to commit all changes with message: \"{message}\"...");
            var success = await gitService.CreateCommitAllAsync(message, token);

            if (success)
            {
                AnsiConsole.MarkupLine("[green]Commit operation completed. Check logs for details.[/]");
            }
            else
            {
                AnsiConsole.MarkupLine("[red]Failed to create commit. Check logs for details.[/]");
            }
        }
        catch (InvalidOperationException ex)
        {
            AnsiConsole.MarkupLine($"[red]Git operation failed: {ex.Message}[/]");
        }
        catch (Exception e)
        {
            AnsiConsole.MarkupLine("[red]An unexpected error occurred while committing.[/]");
            AnsiConsole.WriteException(e);
        }
    }

    private async Task PitchCommand(InvocationContext arg)
    {
        var idea = arg.ParseResult.GetValueForArgument(_ideaArgument);
        var token = arg.GetCancellationToken();

        if (string.IsNullOrWhiteSpace(idea))
        {
            AnsiConsole.MarkupLine("[red]Idea cannot be empty.[/]");

            return;
        }

        try
        {
            await pitchService.CreatePremiseFromPitch(idea, token);
        }
        catch (Exception e)
        {
            AnsiConsole.WriteException(e);
        }
    }

    private async Task InitCommand(InvocationContext arg)
    {
        try
        {
            (var created, var initialised) = await workspaceManager.InitialiseWorkspace();

            var message = created ? "Workspace initialised." : "You are already in a Scribal workspace.";

            AnsiConsole.WriteLine(message);

            if (initialised)
            {
                AnsiConsole.WriteLine("Git repo initialised.");
            }
        }
        catch (Exception e)
        {
            AnsiConsole.WriteException(e);
        }
    }

    private Task ClearCommand(InvocationContext ctx)
    {
        _ = conversationStore.TryClearConversation(string.Empty);
        AnsiConsole.MarkupLine("[yellow]Conversation history cleared.[/]");

        return Task.FromResult(true);
    }

    private Task TreeCommand(InvocationContext ctx)
    {
        var cwd = fileSystem.Directory.GetCurrentDirectory();
        
        var files = StickyTreeSelector.Scan(cwd);
        
        // Add files, don't replace on multiple /tree invocations.
        foreach (var file in files)
        {
            // Don't care if duplicates failed to be added.
            _ = repoStore.Paths.Add(file);
        }
        
        return Task.FromResult(true);
    }

    private async Task StatusCommand(InvocationContext context)
    {
        // AI: Check if currently in a Scribal workspace
        if (!workspaceManager.InWorkspace)
        {
            AnsiConsole.MarkupLine("[yellow]Not currently in a Scribal workspace. Use '/init' to create one.[/]");
            return;
        }

        AnsiConsole.MarkupLine($"[green]Current Workspace Path:[/] {workspaceManager.CurrentWorkspacePath ?? "N/A"}");

        // AI: Load the workspace state
        var state = await workspaceManager.LoadWorkspaceStateAsync(cancellationToken: context.GetCancellationToken());

        if (state is null)
        {
            AnsiConsole.MarkupLine("[red]Could not load workspace state. The state file might be corrupted or missing.[/]");
            return;
        }

        // AI: Display the current pipeline stage
        AnsiConsole.MarkupLine($"[green]Current Pipeline Stage:[/] {state.PipelineStage.ToString()}");

        // AI: Display the premise if available
        if (!string.IsNullOrWhiteSpace(state.Premise))
        {
            AnsiConsole.MarkupLine($"[green]Premise:[/] {state.Premise}");
        }
        else
        {
            AnsiConsole.MarkupLine("[green]Premise:[/] Not set");
        }
        
        // AI: Display plot outline file if available
        if (!string.IsNullOrWhiteSpace(state.PlotOutlineFile))
        {
            AnsiConsole.MarkupLine($"[green]Plot Outline File:[/] {state.PlotOutlineFile}");
        }
        else
        {
            AnsiConsole.MarkupLine("[green]Plot Outline File:[/] Not set");
        }


        // AI: Display chapter statuses
        if (state.Chapters is not null && state.Chapters.Any())
        {
            AnsiConsole.MarkupLine("[green]Chapters:[/]");
            var table = new Table().Expand();
            table.AddColumn("Number");
            table.AddColumn("Title");
            table.AddColumn("State");
            table.AddColumn("Summary");

            foreach (var chapter in state.Chapters.OrderBy(c => c.Number))
            {
                table.AddRow(
                    chapter.Number.ToString(),
                    chapter.Title ?? "N/A",
                    chapter.State.ToString(),
                    chapter.Summary ?? "N/A"
                );
            }
            AnsiConsole.Write(table);
        }
        else
        {
            AnsiConsole.MarkupLine("[yellow]No chapters found in the current workspace state.[/]");
        }
    }

    private static Task QuitCommand(InvocationContext ctx)
    {
        if (AnsiConsole.Confirm("Are you sure you want to quit?"))
        {
            AnsiConsole.MarkupLine("[yellow]Thank you for using Scribal. Goodbye![/]");
            Environment.Exit(0);
        }

        return Task.CompletedTask;
    }
}
