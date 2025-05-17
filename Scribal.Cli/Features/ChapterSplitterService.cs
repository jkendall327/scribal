// AI: New file for Chapter Splitter Service Implementation (moved from Scribal.Workspace)

using Microsoft.Extensions.Logging;
using Scribal.Workspace;
using Spectre.Console;

// AI: For IUserInteraction
// AI: For IAnsiConsole and prompts

namespace Scribal.Cli.Features;

public class ChapterSplitterService : IChapterSplitterService
{
    private readonly WorkspaceManager _workspaceManager;
    private readonly IAnsiConsole _console;
    private readonly IUserInteraction _userInteraction;
    private readonly ILogger<ChapterSplitterService> _logger;

    public ChapterSplitterService(WorkspaceManager workspaceManager,
        IAnsiConsole console,
        IUserInteraction userInteraction,
        ILogger<ChapterSplitterService> logger)
    {
        _workspaceManager = workspaceManager;
        _console = console;
        _userInteraction = userInteraction;
        _logger = logger;
    }

    public async Task<bool> SplitChapterAsync(ChapterState sourceChapter, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Initiating split for Chapter {ChapterNumber}: {ChapterTitle}",
            sourceChapter.Number,
            sourceChapter.Title);

        _console.MarkupLine($"Splitting Chapter {sourceChapter.Number}: {Markup.Escape(sourceChapter.Title)}");

        var prompt = new TextPrompt<int>(
                         $"Enter the ordinal position for the new chapter part (e.g., if splitting chapter {sourceChapter.Number}, and new part is immediately after, enter {sourceChapter.Number + 1}):")
                     .PromptStyle("green")
                     .ValidationErrorMessage("[red]That's not a valid number[/]")
                     .Validate(ordinal =>
                     {
                         if (ordinal <= 0)
                         {
                             return ValidationResult.Error("[red]Ordinal must be a positive number.[/]");
                         }

                         // AI: Potentially add validation against existing chapter numbers if needed, though WorkspaceManager handles shifts.
                         return ValidationResult.Success();
                     });

        var newChapterOrdinal = _console.Prompt(prompt);

        if (cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        var newChapterTitle = _console
                              .Ask<string>(
                                  $"Enter the title for the new chapter part (at position {newChapterOrdinal}):")
                              .Trim();

        if (string.IsNullOrWhiteSpace(newChapterTitle))
        {
            _console.MarkupLine("[red]New chapter title cannot be empty. Aborting split.[/]");
            _logger.LogWarning("New chapter title was empty, split aborted by user input");

            return false;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        var newChapterSummary = _console
                                .Ask<string>(
                                    $"Enter a brief summary for the new chapter part '{Markup.Escape(newChapterTitle)}':")
                                .Trim();

        if (cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        _console.MarkupLine(
            $"Provide an updated summary for the original chapter (Chapter {sourceChapter.Number}: {Markup.Escape(sourceChapter.Title)}).");

        var updatedSourceChapterSummary =
            _console.Ask<string>($"Updated summary for Chapter {sourceChapter.Number}:").Trim();

        if (cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        var confirmPrompt =
            $"Confirm split: Original Chapter {sourceChapter.Number} ('{Markup.Escape(sourceChapter.Title)}') will be updated. " +
            $"New Chapter {newChapterOrdinal} ('{Markup.Escape(newChapterTitle)}') will be created. Proceed?";

        var confirmed = await _userInteraction.ConfirmAsync(confirmPrompt);

        if (!confirmed || cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("User cancelled chapter split operation for chapter {SourceChapterNumber}",
                sourceChapter.Number);

            _console.MarkupLine("[yellow]Chapter split cancelled.[/]");

            return false;
        }

        _logger.LogInformation(
            "User confirmed split. Source: C{SourceChapterNumber}, New Ordinal: {NewChapterOrdinal}, New Title: {NewChapterTitle}",
            sourceChapter.Number,
            newChapterOrdinal,
            newChapterTitle);

        var success = await _workspaceManager.InsertSplitChapterAsync(sourceChapter.Number,
            updatedSourceChapterSummary,
            newChapterOrdinal,
            newChapterTitle,
            newChapterSummary,
            cancellationToken);

        if (success)
        {
            _logger.LogInformation(
                "Successfully split chapter {SourceChapterNumber} and created new chapter {NewChapterTitle} via WorkspaceManager",
                sourceChapter.Number,
                newChapterTitle);
        }
        else
        {
            _logger.LogError("WorkspaceManager failed to split chapter {SourceChapterNumber}", sourceChapter.Number);
        }

        return success;
    }
}