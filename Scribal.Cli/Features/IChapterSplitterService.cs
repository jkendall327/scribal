using Scribal.Workspace;

namespace Scribal.Cli.Features;

public interface IChapterSplitterService
{
    Task<bool> SplitChapterAsync(ChapterState sourceChapter, CancellationToken cancellationToken);
}