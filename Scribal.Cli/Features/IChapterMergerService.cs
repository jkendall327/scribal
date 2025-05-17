// AI: New file for Chapter Merger Service Interface
using Scribal.Workspace;
using System.Threading;
using System.Threading.Tasks;

namespace Scribal.Cli.Features;

public interface IChapterMergerService
{
    Task<bool> MergeChapterAsync(
        ChapterState sourceChapter,
        CancellationToken cancellationToken);
}
