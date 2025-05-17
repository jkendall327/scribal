// AI: New file for Chapter Splitter Service Interface
using System.Threading;
using System.Threading.Tasks;

namespace Scribal.Workspace;

public interface IChapterSplitterService
{
    Task<bool> SplitChapterAsync(
        ChapterState sourceChapter,
        int newChapterOrdinal,
        string newChapterTitle,
        string newChapterSummary,
        string updatedSourceChapterSummary,
        CancellationToken cancellationToken);
}
