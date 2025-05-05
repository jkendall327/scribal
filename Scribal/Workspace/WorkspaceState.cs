namespace Scribal.Workspace;

public class WorkspaceState
{
    public string? Premise { get; set; }
    public List<ChapterState> Chapters { get; set; } = [];
}

public class ChapterState
{
    public int Number { get; set; }
    public required string Title { get; set; }
    public ChapterStateType State { get; set; }
}

public enum ChapterStateType
{
    Unstarted,
    Draft,
    Done
}