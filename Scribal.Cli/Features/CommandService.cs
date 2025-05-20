using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.IO.Abstractions;
using Scribal.Agency;
using Scribal.AI;
using Scribal.Cli.Interface;
using Scribal.Context;
using Scribal.Workspace;
using Spectre.Console;

namespace Scribal.Cli.Features;

public class CommandService(
    IFileSystem fileSystem,
    RepoMapStore repoStore,
    IChatSessionStore conversationStore,
    PitchService pitchService,
    OutlineService outlineService,
    WorkspaceDeleter workspaceDeleter,
    WorkspaceManager workspaceManager,
    ChapterManagerService chapterManagerService,
    IGitServiceFactory gitFactory,
    ExportService exportService,
    IUserInteraction userInteraction,
    StickyTreeSelector stickyTreeSelector) // Added StickyTreeSelector
{
    private readonly IUserInteraction _userInteraction = userInteraction;
    private readonly StickyTreeSelector _stickyTreeSelector = stickyTreeSelector; // Added StickyTreeSelector field
    private readonly Argument<string> _ideaArgument = new()
    {
        Name = "Idea",
        Arity = ArgumentArity.ExactlyOne,
        Description = "Your basic idea for the story"
    };

    private readonly Argument<string> _premiseArgument = new()
    {
        Name = "Premise",
        Arity = ArgumentArity.ZeroOrOne,
        Description = "The story premise to be turned into an outline"
    };

    private readonly Argument<string> _commitMessageArgument = new()
    {
        Name = "Message",
        Arity = ArgumentArity.ExactlyOne,
        Description = "The commit message for all staged files"
    };

    private readonly Option<string> _exportFileNameOption = new(
        ["--output", "-o"],
        "The name of the output Markdown file for export. Defaults to 'exported_story.md'.")
    {
        Arity = ArgumentArity.ZeroOrOne
    };

    public Parser Build()
    {
        var quit = Create("/quit", "Exit Scribal", QuitCommandAsync, ["/exit"]);
        var clear = Create("/clear", "Clear conversation history", ClearCommandAsync);
        var tree = Create("/add", "Set files to be included in context", TreeCommand); // Stays Task, no await needed
        var init = Create("/init", "Creates a new Scribal workspace in the current folder", InitCommandAsync);

        var pitch = Create("/pitch", "Turns an initial story idea into a fleshed-out premise", PitchCommand); // Already async
        pitch.AddArgument(_ideaArgument);

        var outline = Create("/outline", "Generates a plot outline from a premise", OutlineCommand);
        outline.AddArgument(_premiseArgument);

        var drop = Create("/drop",
            "Drop all selected files from context",
            DropCommand);
        
        var chaptersCmd = Create("/chapters",
            "Manage chapters in the workspace",
            chapterManagerService.ManageChaptersAsync);

        var deleteWorkspaceCmd = Create("/delete",
            "Deletes the .scribal workspace and optionally the .git folder",
            workspaceDeleter.DeleteWorkspaceCommandAsync);

        var commitCmd = Create("/commit",
            "Stages all changes and creates a commit with the given message",
            CommitAllCommandAsync); // Already async

        commitCmd.AddArgument(_commitMessageArgument);

        var statusCmd = Create("/status", "Displays the current project status", StatusCommandAsync);

        var exportCmd = Create("/export", "Exports all chapters into a single Markdown file", ExportCommandAsync); // Already async
        exportCmd.AddOption(_exportFileNameOption);

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
            exportCmd,
            tree,
            drop,
            quit
        };

        return new CommandLineBuilder(root).UseDefaults().UseHelp("/help").Build();

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
    }

    private async Task DropCommand(InvocationContext arg)
    {
        repoStore.Paths.Clear();
        await _userInteraction.NotifyAsync("All files cleared from context.", new(MessageType.Warning));
    }

    private async Task ExportCommandAsync(InvocationContext context)
    {
        var outputFileName = context.ParseResult.GetValueForOption(_exportFileNameOption);
        var token = context.GetCancellationToken();

        if (!workspaceManager.InWorkspace)
        {
            await _userInteraction.NotifyAsync(
                "Cannot export: Not currently in a Scribal workspace. Use '/init' to create one.",
                new(MessageType.Error));

            return;
        }

        try
        {
            await _userInteraction.NotifyAsync("Starting export...");
            await exportService.ExportStoryAsync(outputFileName, token);

            // TODO: Success/failure messages are handled within ExportService
        }
        catch (OperationCanceledException)
        {
            await _userInteraction.NotifyAsync("Export operation cancelled.", new(MessageType.Warning));
        }
        catch (Exception e)
        {
            await _userInteraction.NotifyError("An unexpected error occurred during export.", e);
        }
    }

    private async Task OutlineCommand(InvocationContext arg)
    {
        var premise = arg.ParseResult.GetValueForArgument(_premiseArgument);
        
        var token = arg.GetCancellationToken();

        try
        {
            await outlineService.CreateOutlineFromPremise(premise, token);
        }
        catch (Exception e)
        {
            // Assuming OutlineService handles its own specific user notifications for known errors.
            // This NotifyError is for unexpected exceptions during the call.
            await _userInteraction.NotifyError("An unexpected error occurred during outline generation.", e);
        }
    }

    private async Task CommitAllCommandAsync(InvocationContext context)
    {
        var message = context.ParseResult.GetValueForArgument(_commitMessageArgument);
        var token = context.GetCancellationToken();

        if (string.IsNullOrWhiteSpace(message))
        {
            await _userInteraction.NotifyAsync("Commit message cannot be empty.", new(MessageType.Error));

            return;
        }

        if (!gitFactory.TryOpenRepository(out var git))
        {
            await _userInteraction.NotifyAsync("Git is not initialized for this workspace. Cannot commit.",
                new(MessageType.Warning));

            return;
        }

        try
        {
            await _userInteraction.NotifyAsync($"Attempting to commit all changes with message: \"{message}\"...");
            
            var success = await git.CreateCommitAllAsync(message, token);

            await _userInteraction.NotifyAsync(success
                ? "Commit operation completed. Check logs for details."
                : "Failed to create commit. Check logs for details.",
                success ? new(MessageType.Informational) : new(MessageType.Error));
        }
        catch (InvalidOperationException ex)
        {
            await _userInteraction.NotifyAsync($"Git operation failed: {ex.Message}", new(MessageType.Error));
        }
        catch (Exception e)
        {
            await _userInteraction.NotifyError("An unexpected error occurred while committing.", e);
        }
    }

    private async Task PitchCommand(InvocationContext arg)
    {
        var idea = arg.ParseResult.GetValueForArgument(_ideaArgument);
        var token = arg.GetCancellationToken();

        if (string.IsNullOrWhiteSpace(idea))
        {
            await _userInteraction.NotifyAsync("Idea cannot be empty.", new(MessageType.Error));

            return;
        }

        try
        {
            await pitchService.CreatePremiseFromPitch(idea, token);
        }
        catch (Exception e)
        {
            // Assuming PitchService handles its own specific user notifications.
            await _userInteraction.NotifyError("An unexpected error occurred during pitch generation.", e);
        }
    }

    private async Task InitCommandAsync(InvocationContext arg)
    {
        try
        {
            (var created, var initialised) = await workspaceManager.InitialiseWorkspace();

            var message = created ? "Workspace initialised." : "You are already in a Scribal workspace.";

            await _userInteraction.NotifyAsync(message);

            if (initialised)
            {
                await _userInteraction.NotifyAsync("Git repo initialised.");
            }
        }
        catch (Exception e)
        {
            await _userInteraction.NotifyError("An error occurred during workspace initialisation.", e);
        }
    }

    private async Task ClearCommandAsync(InvocationContext ctx)
    {
        _ = conversationStore.TryClearConversation(string.Empty);
        
        await _userInteraction.ClearAsync();
        
        await _userInteraction.NotifyAsync("Conversation history cleared.", new(MessageType.Warning), ctx.GetCancellationToken());
    }

    private async Task TreeCommand(InvocationContext ctx) // Changed to async Task
    {
        var cwd = fileSystem.Directory.GetCurrentDirectory();
        var token = ctx.GetCancellationToken();

        // Call the instance method ScanAsync from the injected StickyTreeSelector
        var files = await _stickyTreeSelector.ScanAsync(cwd, token);

        // Add files, don't replace on multiple /add invocations.
        foreach (var file in files)
        {
            // Don't care if duplicates failed to be added.
            _ = repoStore.Paths.Add(file);
        }
        
        // Optional: Notify user that files have been added to context
        if (files.Any())
        {
            await _userInteraction.NotifyAsync("Selected files have been added to the context for AI interaction.", new(MessageType.Informational), token);
        }
        // If ScanAsync already provides user feedback about selection, this NotifyAsync might be redundant or supplementary.
    }

    private async Task StatusCommandAsync(InvocationContext context)
    {
        if (!workspaceManager.InWorkspace)
        {
            await _userInteraction.NotifyAsync("Not currently in a Scribal workspace. Use '/init' to create one.",
                new(MessageType.Warning));

            return;
        }

        await _userInteraction.NotifyAsync(
            $"[green]Current Workspace Path:[/] {workspaceManager.CurrentWorkspacePath ?? "N/A"}");
        var state = await workspaceManager.LoadWorkspaceStateAsync(cancellationToken: context.GetCancellationToken());

        if (state is null)
        {
            await _userInteraction.NotifyAsync(
                "Could not load workspace state. The state file might be corrupted or missing.",
                new(MessageType.Error));

            return;
        }

        await _userInteraction.NotifyAsync($"[green]Current Pipeline Stage:[/] {state.PipelineStage.ToString()}");

        if (string.IsNullOrWhiteSpace(state.Premise))
        {
            await _userInteraction.NotifyAsync("[green]Premise:[/] Not set");
        }
        else
        {
            await _userInteraction.DisplayProsePassageAsync(state.Premise, "Premise");
        }

        await _userInteraction.NotifyAsync(!string.IsNullOrWhiteSpace(state.PlotOutlineFile)
            ? $"[green]Plot Outline File:[/] {state.PlotOutlineFile}"
            : "[green]Plot Outline File:[/] Not set");

        if (state.Chapters.Any())
        {
            await _userInteraction.NotifyAsync("[green]Chapters:[/]");
            var table = new Table().Expand();
            table.AddColumn("Number");
            table.AddColumn("Title");
            table.AddColumn("State");
            table.AddColumn("Summary");

            foreach (var chapter in state.Chapters.OrderBy(c => c.Number))
            {
                table.AddRow(chapter.Number.ToString(),
                    chapter.Title,
                    chapter.State.ToString(),
                    chapter.Summary ?? "N/A");
            }

            await _userInteraction.WriteTableAsync(table);
        }
        else
        {
            await _userInteraction.NotifyAsync("No chapters found in the current workspace state.",
                new(MessageType.Warning));
        }
    }

    private async Task QuitCommandAsync(InvocationContext ctx)
    {
        await _userInteraction.NotifyAsync("Thank you for using Scribal. Goodbye!", new(MessageType.Warning), ctx.GetCancellationToken());
        Environment.Exit(0);
    }
}