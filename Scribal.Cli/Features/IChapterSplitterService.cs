// AI: New file for Chapter Splitter Service Interface (moved from Scribal.Workspace)

using Scribal.Workspace;

// AI: Still need this for ChapterState

namespace Scribal.Cli.Features;

public interface IChapterSplitterService
{
    Task<bool> SplitChapterAsync(ChapterState sourceChapter, CancellationToken cancellationToken);
}