// AI: New file for Chapter Merger Service Interface

using Scribal.Workspace;

namespace Scribal.Cli.Features;

public interface IChapterMergerService
{
    Task<bool> MergeChapterAsync(ChapterState sourceChapter, CancellationToken cancellationToken);
}