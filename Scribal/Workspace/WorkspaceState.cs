namespace Scribal.Workspace;

// AI: Defines the stages of the writing pipeline
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
    public PipelineStageType PipelineStage { get; set; } // AI: Changed from string to enum
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
