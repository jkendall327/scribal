using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.IO.Abstractions;
using Microsoft.Extensions.Logging;
using Scribal.Cli.Interface;
using Scribal.Workspace;
using Spectre.Console;

namespace Scribal.Cli.Features;

public class ChapterManagerService(
    IFileSystem fileSystem,
    WorkspaceManager workspaceManager,
    IUserInteraction userInteraction,
    ILogger<ChapterManagerService> logger,
    IChapterDeletionService chapterDeletionService,
    ChapterDrafterService chapterDrafterService,
    NewChapterCreator newChapterCreator)
{
    public async Task ManageChaptersAsync(InvocationContext context)
    {
        logger.LogInformation("Starting chapter management");
        var token = context.GetCancellationToken();

        if (!workspaceManager.InWorkspace)
        {
            var foundWorkspace = WorkspaceManager.TryFindWorkspaceFolder(fileSystem);

            if (foundWorkspace == null)
            {
                logger.LogWarning("Not in a Scribal workspace and no workspace found nearby");

                AnsiConsole.MarkupLine(
                    "[red]You are not currently in a Scribal workspace. Use '/init' to create one.[/]");

                return;
            }

            logger.LogInformation("Workspace found at {WorkspacePath}, attempting to load", foundWorkspace);

            // If a workspace is found but not loaded, LoadWorkspaceStateAsync will handle it.
        }

        var state = await workspaceManager.LoadWorkspaceStateAsync(cancellationToken: token);

        if (state == null)
        {
            logger.LogError("Could not load workspace state initially");
            AnsiConsole.MarkupLine("[red]Could not load workspace state. Exiting chapter management.[/]");

            return;
        }

        if (!state.Chapters.Any())
        {
            logger.LogInformation("No chapters found in the workspace upon initial load");

            AnsiConsole.MarkupLine(
                "[yellow]No chapters found in the workspace. Generate an outline first using '/outline'.[/]");

            return;
        }

        logger.LogInformation("Initially loaded {ChapterCount} chapters", state.Chapters.Count);

        while (!token.IsCancellationRequested)
        {
            // Reload state at the beginning of each loop iteration to reflect changes (e.g., deletion)
            state = await workspaceManager.LoadWorkspaceStateAsync(cancellationToken: token);

            if (state == null)
            {
                logger.LogError("Could not reload workspace state in loop");
                AnsiConsole.MarkupLine("[red]Error reloading workspace state. Exiting chapter management.[/]");

                break;
            }

            if (!state.Chapters.Any())
            {
                logger.LogInformation("No chapters remain in the workspace after potential deletion/update");

                AnsiConsole.MarkupLine(
                    "[yellow]No chapters remain or an error occurred. Returning to the previous menu.[/]");

                break;
            }

            logger.LogDebug("Refreshed chapter list, {ChapterCount} chapters found", state.Chapters.Count);
            AnsiConsole.WriteLine();
            var chapterChoices = state.Chapters.OrderBy(c => c.Number).Select(FormatChapterDisplayString).ToList();

            var commandChoices = new List<string>
            {
                "+ Create New Chapter"
            };

            commandChoices.AddRange(chapterChoices);
            commandChoices.Add("Back");

            var selectionPrompt = new SelectionPrompt<string>()
                                  .Title("Select a chapter to manage, create a new one, or [blue]Back[/] to return:")
                                  .PageSize(10)
                                  .AddChoices(commandChoices);

            var choice = AnsiConsole.Prompt(selectionPrompt);

            if (choice == "Back" || token.IsCancellationRequested)
            {
                break;
            }

            if (choice == "+ Create New Chapter")
            {
                await newChapterCreator.CreateNewChapterAsync(token);

                continue;
            }

            var selectedChapterState = state.Chapters.FirstOrDefault(c => FormatChapterDisplayString(c) == choice);

            if (selectedChapterState == null)
            {
                logger.LogWarning("Invalid chapter selection choice: {Choice}", choice);

                continue;
            }

            logger.LogInformation("Selected chapter {ChapterNumber}: {ChapterTitle}",
                selectedChapterState.Number,
                selectedChapterState.Title);

            await ChapterSubMenuAsync(selectedChapterState, token);
        }

        logger.LogInformation("Exiting chapter management");
    }

    private Parser BuildChapterSubMenuParser(ChapterState chapter, CancellationTokenSource linkedCts)
    {
        var dummyCmd = new Command("/dummy", "A dummy action for the chapter.");

        dummyCmd.SetHandler(async () =>
        {
            AnsiConsole.MarkupLine(
                $"[grey]Executed dummy action for chapter {chapter.Number}: {Markup.Escape(chapter.Title)}.[/]");

            try
            {
                await Task.Delay(100, linkedCts.Token);
            }
            catch (OperationCanceledException)
            {
                AnsiConsole.MarkupLine("[yellow]Dummy action cancelled.[/]");
            }
        });

        var backCmd = new Command("/back", "Return to chapter selection.");

        backCmd.SetHandler(() =>
        {
            AnsiConsole.MarkupLine("[blue]Returning to chapter selection...[/]");
            linkedCts.Cancel();
        });

        var deleteCmd = new Command("/delete", "Delete this chapter.");
        deleteCmd.SetHandler(async () => { await DeleteChapterAsync(chapter, linkedCts); });

        var draftCmd = new Command("/draft", "Draft this chapter using AI.");

        draftCmd.SetHandler(async () =>
        {
            // We want the draft operation to be cancellable by the parent token (e.g. Ctrl+C)
            // but not necessarily by the subMenuCts which is for /back.
            // However, if /back is invoked during drafting, it might be good to cancel.
            // For now, pass the linkedCts.Token. If drafting becomes long and needs independent cancellation,
            // we might need a different token strategy.
            await chapterDrafterService.DraftChapterAsync(chapter, linkedCts.Token);

            // If drafting completes successfully, we might want to stay in the sub-menu.
            // If it's cancelled by /back from within the refinement loop of drafting, linkedCts will be cancelled.
        });

        var chapterRootCommand =
            new RootCommand($"Actions for Chapter {chapter.Number}: {Markup.Escape(chapter.Title)}")
            {
                dummyCmd,
                draftCmd, // Added draft command
                deleteCmd,
                backCmd
            };

        return new CommandLineBuilder(chapterRootCommand).UseDefaults().UseHelp("/help").Build();
    }

    private async Task ChapterSubMenuAsync(ChapterState selectedChapter, CancellationToken parentToken)
    {
        logger.LogInformation("Entering sub-menu for chapter {ChapterNumber}: {ChapterTitle}",
            selectedChapter.Number,
            selectedChapter.Title);

        using var chapterSubMenuCts = CancellationTokenSource.CreateLinkedTokenSource(parentToken);
        var chapterSubMenuParser = BuildChapterSubMenuParser(selectedChapter, chapterSubMenuCts);

        while (!chapterSubMenuCts.IsCancellationRequested && !parentToken.IsCancellationRequested)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"Managing Chapter: {FormatChapterDisplayString(selectedChapter)}");
            if (!string.IsNullOrWhiteSpace(selectedChapter.Summary))
            {
                AnsiConsole.MarkupLine($"Summary: [grey]{Markup.Escape(selectedChapter.Summary)}[/]");
            }

            AnsiConsole.MarkupLine(
                "Enter a command for this chapter ([blue]/help[/] for options, [blue]/back[/] to return to chapter list):");

            AnsiConsole.Markup($"Chapter {selectedChapter.Number} > ");

            var input = ReadLine.Read();

            if (parentToken.IsCancellationRequested)
            {
                AnsiConsole.MarkupLine("[yellow]Exiting chapter menu due to external cancellation.[/]");

                break;
            }

            if (string.IsNullOrWhiteSpace(input))
            {
                continue;
            }

            try
            {
                logger.LogDebug("Invoking chapter submenu command: {Input}", input);
                await chapterSubMenuParser.InvokeAsync(input);
            }
            catch (OperationCanceledException) when (chapterSubMenuCts.IsCancellationRequested)
            {
                logger.LogInformation("Chapter action cancelled or /back invoked for chapter {ChapterNumber}",
                    selectedChapter.Number);

                AnsiConsole.MarkupLine("[yellow](Chapter action cancelled or /back invoked)[/]");
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Error processing chapter command {CommandInput} for chapter {ChapterNumber}",
                    input,
                    selectedChapter.Number);

                AnsiConsole.MarkupLine($"[red]Error processing chapter command: {ex.Message}[/]");
            }
        }

        logger.LogInformation("Exiting sub-menu for chapter {ChapterNumber}: {ChapterTitle}",
            selectedChapter.Number,
            selectedChapter.Title);
    }

    private async Task DeleteChapterAsync(ChapterState chapterToDelete, CancellationTokenSource subMenuCts)
    {
        logger.LogInformation("Attempting to delete chapter {ChapterNumber}: {ChapterTitle}",
            chapterToDelete.Number,
            chapterToDelete.Title);

        var confirmPrompt =
            $"Are you sure you want to delete Chapter {chapterToDelete.Number}: '{Markup.Escape(chapterToDelete.Title)}'? This action cannot be undone.";

        if (!await userInteraction.ConfirmAsync(confirmPrompt))
        {
            logger.LogInformation("User cancelled deletion of chapter {ChapterNumber}", chapterToDelete.Number);
            AnsiConsole.MarkupLine("[yellow]Chapter deletion cancelled.[/]");

            return;
        }

        logger.LogInformation("User confirmed deletion of chapter {ChapterNumber}", chapterToDelete.Number);

        var deletionResult = await chapterDeletionService.DeleteChapterAsync(chapterToDelete, subMenuCts.Token);

        foreach (var error in deletionResult.Errors)
        {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(error)}[/]");
        }

        foreach (var warning in deletionResult.Warnings)
        {
            AnsiConsole.MarkupLine($"[yellow]{Markup.Escape(warning)}[/]");
        }

        foreach (var action in deletionResult.ActionsTaken)
        {
            AnsiConsole.MarkupLine($"[green]{Markup.Escape(action)}[/]");
        }

        if (deletionResult.Success)
        {
            logger.LogInformation("Successfully deleted chapter {ChapterNumber}: {ChapterTitle}",
                chapterToDelete.Number,
                chapterToDelete.Title);

            AnsiConsole.MarkupLine(
                $"[bold green]{Markup.Escape(deletionResult.OverallMessage ?? "Chapter deleted successfully.")}[/]");

            await subMenuCts.CancelAsync(); // Exit the current chapter's sub-menu
        }
        else
        {
            logger.LogError(deletionResult.Exception,
                "Failed to delete chapter {ChapterNumber}: {ChapterTitle}. Reason: {DeletionMessage}",
                chapterToDelete.Number,
                chapterToDelete.Title,
                deletionResult.OverallMessage);

            AnsiConsole.MarkupLine(
                $"[bold red]{Markup.Escape(deletionResult.OverallMessage ?? "Chapter deletion failed.")}[/]");

            if (deletionResult.Exception != null && deletionResult.Exception is not OperationCanceledException)
            {
                ExceptionDisplay.DisplayException(deletionResult.Exception);
            }
        }
    }

    private string FormatChapterDisplayString(ChapterState chapter)
    {
        var stateColor = chapter.State switch
        {
            ChapterStateType.Unstarted => "red",
            ChapterStateType.Draft => "yellow",
            ChapterStateType.Done => "green",
            var _ => "grey"
        };

        return $"{chapter.Number}. {Markup.Escape(chapter.Title)} ([{stateColor}]{chapter.State}[/])";
    }
}
