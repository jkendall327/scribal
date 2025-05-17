namespace Scribal.Workspace;

public enum PipelineStageType
{
    AwaitingPremise,
    AwaitingOutline,
    DraftingChapters
}

public class WorkspaceState
{
    public string? Premise { get; set; }
    public string? PlotOutlineFile { get; set; } // Added to store the path/filename of the plot outline
    public List<ChapterState> Chapters { get; set; } = [];
    public PipelineStageType PipelineStage { get; set; }
}

public class ChapterState
{
    public int Number { get; set; }
    public required string Title { get; init; }
    public string? Summary { get; init; }
    public ChapterStateType State { get; set; }
    public string? DraftFilePath { get; set; }
}

public enum ChapterStateType
{
    Unstarted,
    Draft,
    Done
}