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
    NewChapterCreator newChapterCreator,
    IChapterSplitterService chapterSplitterService,
    IChapterMergerService chapterMergerService)
{
    private readonly IUserInteraction _userInteraction = userInteraction;

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

                await _userInteraction.NotifyAsync(
                    "You are not currently in a Scribal workspace. Use '/init' to create one.",
                    new(MessageType.Error));

                return;
            }

            logger.LogInformation("Workspace found at {WorkspacePath}, attempting to load", foundWorkspace);

            // If a workspace is found but not loaded, LoadWorkspaceStateAsync will handle it.
        }

        var state = await workspaceManager.LoadWorkspaceStateAsync(cancellationToken: token);

        if (state == null)
        {
            logger.LogError("Could not load workspace state initially");
            await _userInteraction.NotifyAsync("Could not load workspace state. Exiting chapter management.",
                new(MessageType.Error));

            return;
        }

        if (!state.Chapters.Any())
        {
            logger.LogInformation("No chapters found in the workspace upon initial load");

            await _userInteraction.NotifyAsync(
                "No chapters found in the workspace. Generate an outline first using '/outline'.",
                new(MessageType.Warning));

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
                await _userInteraction.NotifyAsync("Error reloading workspace state. Exiting chapter management.",
                    new(MessageType.Error));

                break;
            }

            if (!state.Chapters.Any())
            {
                logger.LogInformation("No chapters remain in the workspace after potential deletion/update");

                await _userInteraction.NotifyAsync(
                    "No chapters remain or an error occurred. Returning to the previous menu.",
                    new(MessageType.Warning));

                break;
            }

            logger.LogDebug("Refreshed chapter list, {ChapterCount} chapters found", state.Chapters.Count);
            await _userInteraction.NotifyAsync(""); // Replicates AnsiConsole.WriteLine() for spacing
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

            var choice = await _userInteraction.PromptAsync(selectionPrompt);

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
        dummyCmd.SetHandler(async () => await HandleDummyCommandAsync(chapter, linkedCts.Token));

        var backCmd = new Command("/back", "Return to chapter selection.");
        backCmd.SetHandler(async () => await HandleBackCommandAsync(linkedCts));

        var deleteCmd = new Command("/delete", "Delete this chapter.");
        deleteCmd.SetHandler(async () => await DeleteChapterAsync(chapter, linkedCts));

        var draftCmd = new Command("/draft", "Draft this chapter using AI.");
        draftCmd.SetHandler(async () => await chapterDrafterService.DraftChapterAsync(chapter, linkedCts.Token));

        var splitCmd = new Command("/split", "Split this chapter into two.");
        splitCmd.SetHandler(async () => await SplitChapterAsync(chapter, linkedCts));

        var mergeCmd = new Command("/merge", "Merge this chapter into another.");
        mergeCmd.SetHandler(async () => await MergeChapterAsync(chapter, linkedCts));

        var chapterRootCommand =
            new RootCommand($"Actions for Chapter {chapter.Number}: {Markup.Escape(chapter.Title)}")
            {
                dummyCmd,
                draftCmd,
                deleteCmd,
                splitCmd,
                mergeCmd,
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
            await _userInteraction.NotifyAsync(""); // Replicates AnsiConsole.WriteLine()
            await _userInteraction.NotifyAsync($"Managing Chapter: {FormatChapterDisplayString(selectedChapter)}");

            if (!string.IsNullOrWhiteSpace(selectedChapter.Summary))
            {
                await _userInteraction.DisplayProsePassageAsync(selectedChapter.Summary, "Summary");
            }

            await _userInteraction.NotifyAsync(
                "Enter a command for this chapter ([blue]/help[/] for options, [blue]/back[/] to return to chapter list):");

            // For the prompt like "Chapter X > ", IUserInteraction typically doesn't support partial line prompts.
            // GetUserInputAsync is expected to handle the full interaction.
            // We can prepend the prompt to the user's mental model or adjust GetUserInputAsync if it had prompt capabilities.
            // For now, we'll rely on the previous "Enter a command" message.
            // If a visual prompt prefix is strictly needed, IUserInteraction would need a method for it,
            // or we make GetUserInputAsync take a prompt string.
            // Consider current GetUserInputAsync as a simple line reader.
            // await _userInteraction.NotifyAsync($"Chapter {selectedChapter.Number} > ", new MessageOptions { NoNewLine = true }); // Assuming NotifyAsync can do this
            
            var input = await _userInteraction.GetUserInputAsync(parentToken);

            if (parentToken.IsCancellationRequested)
            {
                await _userInteraction.NotifyAsync("Exiting chapter menu due to external cancellation.",
                    new(MessageType.Warning));

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

                await _userInteraction.NotifyAsync("(Chapter action cancelled or /back invoked)",
                    new(MessageType.Warning));
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Error processing chapter command {CommandInput} for chapter {ChapterNumber}",
                    input,
                    selectedChapter.Number);

                await _userInteraction.NotifyError($"Error processing chapter command: {ex.Message}", ex);
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

        if (!await _userInteraction.ConfirmAsync(confirmPrompt, subMenuCts.Token))
        {
            logger.LogInformation("User cancelled deletion of chapter {ChapterNumber}", chapterToDelete.Number);
            await _userInteraction.NotifyAsync("Chapter deletion cancelled.", new(MessageType.Warning));

            return;
        }

        logger.LogInformation("User confirmed deletion of chapter {ChapterNumber}", chapterToDelete.Number);

        var deletionResult = await chapterDeletionService.DeleteChapterAsync(chapterToDelete, subMenuCts.Token);

        foreach (var error in deletionResult.Errors)
        {
            await _userInteraction.NotifyAsync(Markup.Escape(error), new(MessageType.Error));
        }

        foreach (var warning in deletionResult.Warnings)
        {
            await _userInteraction.NotifyAsync(Markup.Escape(warning), new(MessageType.Warning));
        }

        foreach (var action in deletionResult.ActionsTaken)
        {
            await _userInteraction.NotifyAsync(Markup.Escape(action), new(MessageType.Informational, MessageStyle.Bold)); // Assuming green means informational/success
        }

        if (deletionResult.Success)
        {
            logger.LogInformation("Successfully deleted chapter {ChapterNumber}: {ChapterTitle}",
                chapterToDelete.Number,
                chapterToDelete.Title);

            await _userInteraction.NotifyAsync(
                Markup.Escape(deletionResult.OverallMessage ?? "Chapter deleted successfully."),
                new(MessageType.Informational, MessageStyle.Bold)); // Assuming green means informational/success

            await subMenuCts.CancelAsync(); // Exit the current chapter's sub-menu
        }
        else
        {
            logger.LogError(deletionResult.Exception,
                "Failed to delete chapter {ChapterNumber}: {ChapterTitle}. Reason: {DeletionMessage}",
                chapterToDelete.Number,
                chapterToDelete.Title,
                deletionResult.OverallMessage);

            await _userInteraction.NotifyError(
                Markup.Escape(deletionResult.OverallMessage ?? "Chapter deletion failed."),
                deletionResult.Exception); // NotifyError will handle displaying the exception
        }
    }

    private async Task SplitChapterAsync(ChapterState sourceChapter, CancellationTokenSource subMenuCts)
    {
        logger.LogInformation("Handing off to ChapterSplitterService for chapter {ChapterNumber}: {ChapterTitle}",
            sourceChapter.Number,
            sourceChapter.Title);

        var success = await chapterSplitterService.SplitChapterAsync(sourceChapter, subMenuCts.Token);

        if (success)
        {
            await _userInteraction.NotifyAsync(
                $"Chapter split operation completed successfully for {Markup.Escape(sourceChapter.Title)}. Returning to chapter list.",
                new(MessageType.Informational, MessageStyle.Bold));

            logger.LogInformation(
                "Successfully completed split operation for chapter {SourceChapterNumber} via service",
                sourceChapter.Number);

            await subMenuCts.CancelAsync();
        }
        else
        {
            await _userInteraction.NotifyAsync(
                "Chapter split operation failed or was cancelled. Check logs for details.",
                new(MessageType.Error, MessageStyle.Bold));

            logger.LogWarning(
                "Chapter split operation failed or was cancelled for chapter {SourceChapterNumber} via service",
                sourceChapter.Number);
        }
    }

    private async Task MergeChapterAsync(ChapterState sourceChapter, CancellationTokenSource subMenuCts)
    {
        logger.LogInformation("Handing off to ChapterMergerService for chapter {ChapterNumber}: {ChapterTitle}",
            sourceChapter.Number,
            sourceChapter.Title);

        var success = await chapterMergerService.MergeChapterAsync(sourceChapter, subMenuCts.Token);

        if (success)
        {
            await _userInteraction.NotifyAsync(
                $"Chapter merge operation completed successfully for {Markup.Escape(sourceChapter.Title)}. Returning to chapter list.",
                new(MessageType.Informational, MessageStyle.Bold));

            logger.LogInformation(
                "Successfully completed merge operation for chapter {SourceChapterNumber} via service",
                sourceChapter.Number);

            await subMenuCts.CancelAsync();
        }
        else
        {
            await _userInteraction.NotifyAsync(
                "Chapter merge operation failed or was cancelled. Check logs for details.",
                new(MessageType.Error, MessageStyle.Bold));

            logger.LogWarning(
                "Chapter merge operation failed or was cancelled for chapter {SourceChapterNumber} via service",
                sourceChapter.Number);
        }
    }
    
    // Helper methods for command handlers in BuildChapterSubMenuParser
    private async Task HandleDummyCommandAsync(ChapterState chapter, CancellationToken token)
    {
        await _userInteraction.NotifyAsync(
            $"Executed dummy action for chapter {chapter.Number}: {Markup.Escape(chapter.Title)}.", new(MessageType.Hint));
        try
        {
            await Task.Delay(100, token);
        }
        catch (OperationCanceledException)
        {
            await _userInteraction.NotifyAsync("Dummy action cancelled.", new(MessageType.Warning));
        }
    }

    private async Task HandleBackCommandAsync(CancellationTokenSource linkedCts)
    {
        await _userInteraction.NotifyAsync("Returning to chapter selection...", new(MessageType.Informational));
        await linkedCts.CancelAsync();
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