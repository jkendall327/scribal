// AI: New file for Chapter Splitter Service Implementation
using Microsoft.Extensions.Logging;
using System.Threading;
using System.Threading.Tasks;

namespace Scribal.Workspace;

public class ChapterSplitterService : IChapterSplitterService
{
    private readonly WorkspaceManager _workspaceManager;
    private readonly ILogger<ChapterSplitterService> _logger;

    public ChapterSplitterService(
        WorkspaceManager workspaceManager,
        ILogger<ChapterSplitterService> logger)
    {
        _workspaceManager = workspaceManager;
        _logger = logger;
    }

    public async Task<bool> SplitChapterAsync(
        ChapterState sourceChapter,
        int newChapterOrdinal,
        string newChapterTitle,
        string newChapterSummary,
        string updatedSourceChapterSummary,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Attempting to split Chapter {SourceChapterNumber}: {SourceChapterTitle} into a new chapter at ordinal {NewChapterOrdinal} with title {NewChapterTitle}",
            sourceChapter.Number, sourceChapter.Title, newChapterOrdinal, newChapterTitle);

        var success = await _workspaceManager.InsertSplitChapterAsync(
            sourceChapter.Number,
            updatedSourceChapterSummary,
            newChapterOrdinal,
            newChapterTitle,
            newChapterSummary,
            cancellationToken);

        if (success)
        {
            _logger.LogInformation("Successfully split chapter {SourceChapterNumber} and created new chapter {NewChapterTitle}",
                sourceChapter.Number, newChapterTitle);
        }
        else
        {
            _logger.LogError("Failed to split chapter {SourceChapterNumber}", sourceChapter.Number);
        }
        return success;
    }
}
