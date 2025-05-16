namespace Scribal.Workspace;

public class WorkspaceState
{
    public string? Premise { get; set; }
    public string? PlotOutlineFile { get; set; } // Added to store the path/filename of the plot outline
    public List<ChapterState> Chapters { get; set; } = [];
    public string PipelineStage { get; set; }
}

public class ChapterState
{
    public int Number { get; set; }
    public required string Title { get; set; }
    public string? Summary { get; set; } // Added Summary
    public ChapterStateType State { get; set; }
}

public enum ChapterStateType
{
    Unstarted,
    Draft,
    Done
}