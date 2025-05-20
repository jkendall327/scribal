using Microsoft.Extensions.Logging;
using Scribal.Workspace;
using Spectre.Console;

namespace Scribal.Cli.Features;

public class ChapterSplitterService : IChapterSplitterService
{
    private readonly WorkspaceManager _workspaceManager;
    private readonly IUserInteraction _userInteraction;
    private readonly ILogger<ChapterSplitterService> _logger;

    public ChapterSplitterService(WorkspaceManager workspaceManager,
        IUserInteraction userInteraction,
        ILogger<ChapterSplitterService> logger)
    {
        _workspaceManager = workspaceManager;
        _userInteraction = userInteraction;
        _logger = logger;
    }

    public async Task<bool> SplitChapterAsync(ChapterState sourceChapter, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Initiating split for Chapter {ChapterNumber}: {ChapterTitle}",
            sourceChapter.Number,
            sourceChapter.Title);

        await _userInteraction.NotifyAsync($"Splitting Chapter {sourceChapter.Number}: {Markup.Escape(sourceChapter.Title)}");

        var ordinalPrompt = $"Enter the ordinal position for the new chapter part (e.g., if splitting chapter {sourceChapter.Number}, and new part is immediately after, enter {sourceChapter.Number + 1}):";
        // IUserInteraction.AskAsync<T> does not support complex validation like Spectre's TextPrompt.
        // We'll get the value and validate manually.
        var newChapterOrdinalString = await _userInteraction.AskAsync<string>(ordinalPrompt, cancellationToken: cancellationToken);

        if (cancellationToken.IsCancellationRequested) return false;

        if (!int.TryParse(newChapterOrdinalString, out var newChapterOrdinal) || newChapterOrdinal <= 0)
        {
            await _userInteraction.NotifyAsync("Ordinal must be a positive number. Aborting split.", new(MessageType.Error));
            _logger.LogWarning("Invalid ordinal input: {OrdinalInput}", newChapterOrdinalString);
            return false;
        }
        
        var newChapterTitle = (await _userInteraction
                              .AskAsync<string>(
                                  $"Enter the title for the new chapter part (at position {newChapterOrdinal}):", cancellationToken: cancellationToken))
                              .Trim();

        if (string.IsNullOrWhiteSpace(newChapterTitle))
        {
            await _userInteraction.NotifyAsync("New chapter title cannot be empty. Aborting split.", new(MessageType.Error));
            _logger.LogWarning("New chapter title was empty, split aborted by user input");

            return false;
        }

        if (cancellationToken.IsCancellationRequested) return false;

        var newChapterSummary = (await _userInteraction
                                .AskAsync<string>(
                                    $"Enter a brief summary for the new chapter part '{Markup.Escape(newChapterTitle)}':", cancellationToken: cancellationToken))
                                .Trim();

        if (cancellationToken.IsCancellationRequested) return false;

        await _userInteraction.NotifyAsync(
            $"Provide an updated summary for the original chapter (Chapter {sourceChapter.Number}: {Markup.Escape(sourceChapter.Title)}).");

        var updatedSourceChapterSummary =
            (await _userInteraction.AskAsync<string>($"Updated summary for Chapter {sourceChapter.Number}:", cancellationToken: cancellationToken)).Trim();

        if (cancellationToken.IsCancellationRequested) return false;

        var confirmPrompt =
            $"Confirm split: Original Chapter {sourceChapter.Number} ('{Markup.Escape(sourceChapter.Title)}') will be updated. " +
            $"New Chapter {newChapterOrdinal} ('{Markup.Escape(newChapterTitle)}') will be created. Proceed?";

        var confirmed = await _userInteraction.ConfirmAsync(confirmPrompt, cancellationToken);

        if (!confirmed || cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("User cancelled chapter split operation for chapter {SourceChapterNumber}",
                sourceChapter.Number);

            await _userInteraction.NotifyAsync("Chapter split cancelled.", new(MessageType.Warning));

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