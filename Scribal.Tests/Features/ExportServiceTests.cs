using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Scribal.Agency;
using Scribal.Cli.Features;
using Scribal.Config;
using Scribal.Workspace;
using Spectre.Console;

namespace Scribal.Tests.Features;

public class ExportServiceTests
{
    private readonly MockFileSystem _fileSystem = new();
    private readonly IAnsiConsole _console = Substitute.For<IAnsiConsole>();
    private readonly ExportService _sut;
    private readonly IGitService _gitService = Substitute.For<IGitService>();
    private readonly IUserInteraction _userInteraction = Substitute.For<IUserInteraction>();

    private const string TestProjectRootDir = "/test/project";
    private const string TestScribalDir = "/test/project/.scribal";
    private const string TestChaptersDir = "/test/project/chapters";
    private const string DefaultExportFileName = "exported_story.md";

    public ExportServiceTests()
    {
        var workspaceManager = new WorkspaceManager(_fileSystem,
            _gitService,
            _userInteraction,
            Options.Create(new AppConfig()),
            NullLogger<WorkspaceManager>.Instance);

        _sut = new(_fileSystem, workspaceManager, _console, NullLogger<ExportService>.Instance);

        // Set current directory and ensure .scribal dir for most tests
        // Specific tests (like NotInWorkspace) might override this.
        _fileSystem.Directory.SetCurrentDirectory(TestProjectRootDir);
        _fileSystem.AddDirectory(TestScribalDir);
    }

    private void SetupWorkspaceState(WorkspaceState state)
    {
        var stateFilePath = _fileSystem.Path.Join(TestScribalDir, "state.json");
        var stateJson = JsonSerializer.Serialize(state, JsonDefaults.Default);
        _fileSystem.AddFile(stateFilePath, new(stateJson));
    }

    private void AddChapterFile(int chapterNumber, string fileName, string content, DateTime? lastWriteTime = null)
    {
        var chapterDirName = $"chapter_{chapterNumber:D2}";
        var chapterDirPath = _fileSystem.Path.Join(TestChaptersDir, chapterDirName);
        _fileSystem.AddDirectory(chapterDirPath);
        var filePath = _fileSystem.Path.Join(chapterDirPath, fileName);
        var fileData = new MockFileData(content);

        if (lastWriteTime.HasValue)
        {
            fileData.LastWriteTime = lastWriteTime.Value;
        }

        _fileSystem.AddFile(filePath, fileData);
    }

    [Fact]
    public async Task ExportStoryAsync_WithFinalFiles_ExportsCorrectly()
    {
        // Arrange
        var state = new WorkspaceState
        {
            Chapters =
            [
                new ChapterState
                {
                    Number = 1,
                    Title = "Chapter One",
                    State = ChapterStateType.Done,
                    Summary = "Summary 1"
                },
                new ChapterState
                {
                    Number = 2,
                    Title = "Chapter Two",
                    State = ChapterStateType.Done,
                    Summary = "Summary 2"
                }
            ],
            PipelineStage = PipelineStageType.DraftingChapters
        };

        SetupWorkspaceState(state);

        _fileSystem.AddDirectory(TestChaptersDir);
        AddChapterFile(1, "chapter_01_final.md", "Content for chapter 1.");
        AddChapterFile(2, "chapter_02_final.md", "Content for chapter 2.");

        // Act
        await _sut.ExportStoryAsync(null, CancellationToken.None);

        // Assert
        var exportedFilePath = _fileSystem.Path.Join(TestChaptersDir, DefaultExportFileName);
        Assert.True(_fileSystem.FileExists(exportedFilePath), $"Exported file not found at {exportedFilePath}");
        var exportedContent = await _fileSystem.File.ReadAllTextAsync(exportedFilePath);

        await Verify(exportedContent).UseDirectory("Snapshots").UseFileName("ExportService_WithFinalFiles");
    }

    [Fact]
    public async Task ExportStoryAsync_PrefersFinalOverDraft_AndLatestDraftIfNoFinal()
    {
        // Arrange
        var state = new WorkspaceState
        {
            Chapters =
            [
                new ChapterState
                {
                    Number = 1,
                    Title = "Chapter One",
                    State = ChapterStateType.Draft,
                    Summary = "Summary 1"
                },
                new ChapterState
                {
                    Number = 2,
                    Title = "Chapter Two",
                    State = ChapterStateType.Draft,
                    Summary = "Summary 2"
                },
                new ChapterState
                {
                    Number = 3,
                    Title = "Chapter Three",
                    State = ChapterStateType.Draft,
                    Summary = "Summary 3"
                }
            ],
            PipelineStage = PipelineStageType.DraftingChapters
        };

        SetupWorkspaceState(state);
        _fileSystem.AddDirectory(TestChaptersDir);

        var time1 = new DateTime(2023, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var time2 = new DateTime(2023, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        AddChapterFile(1, "chapter_01_draft1.md", "Content for chapter 1 draft 1.");
        AddChapterFile(1, "chapter_01_final.md", "Final content for chapter 1.");
        AddChapterFile(2, "chapter_02_draft1.md", "Content for chapter 2 draft 1.", time2);
        AddChapterFile(2, "chapter_02_draft2.md", "Content for chapter 2 draft 2.", time1);
        AddChapterFile(3, "chapter_03_draft3.md", "Content for chapter 3 draft 3.", time1);
        AddChapterFile(3, "chapter_03_draft2.md", "Content for chapter 3 draft 2.", time2);

        // Act
        await _sut.ExportStoryAsync(null, CancellationToken.None);

        // Assert
        var exportedFilePath = _fileSystem.Path.Join(TestChaptersDir, DefaultExportFileName);
        Assert.True(_fileSystem.FileExists(exportedFilePath), $"Exported file not found at {exportedFilePath}");
        var exportedContent = await _fileSystem.File.ReadAllTextAsync(exportedFilePath);

        await Verify(exportedContent).UseDirectory("Snapshots").UseFileName("ExportService_DraftPreference");
    }

    [Fact]
    public async Task ExportStoryAsync_MultipleFinalFiles_PicksLatestByTime()
    {
        // Arrange
        var state = new WorkspaceState
        {
            Chapters =
            [
                new ChapterState
                {
                    Number = 1,
                    Title = "Chapter One",
                    State = ChapterStateType.Done,
                    Summary = "Summary 1"
                }
            ],
            PipelineStage = PipelineStageType.DraftingChapters
        };

        SetupWorkspaceState(state);
        _fileSystem.AddDirectory(TestChaptersDir);

        var time1 = new DateTime(2023, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var time2 = new DateTime(2023, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        AddChapterFile(1, "some_old_final.md", "This is the older final content.", time1);
        AddChapterFile(1, "this_is_the_actual_final.md", "This is the newer final content.", time2);
        AddChapterFile(1, "not_really_a_final_file.md", "This is a draft that should be ignored.", time1);

        // Act
        await _sut.ExportStoryAsync(null, CancellationToken.None);

        // Assert
        var exportedFilePath = _fileSystem.Path.Join(TestChaptersDir, DefaultExportFileName);
        Assert.True(_fileSystem.FileExists(exportedFilePath));
        var exportedContent = await _fileSystem.File.ReadAllTextAsync(exportedFilePath);

        await Verify(exportedContent)
              .UseDirectory("Snapshots")
              .UseFileName("ExportService_MultipleFinalFilesLatestTime");
    }

    [Fact]
    public async Task ExportStoryAsync_WithMissingChapterContent_IncludesPlaceholders()
    {
        // Arrange
        var state = new WorkspaceState
        {
            Chapters =
            [
                new ChapterState
                {
                    Number = 1,
                    Title = "Chapter One",
                    State = ChapterStateType.Done,
                    Summary = "Summary 1"
                },
                new ChapterState
                {
                    Number = 2,
                    Title = "Chapter Two",
                    State = ChapterStateType.Unstarted,
                    Summary = "Summary 2"
                },
                new ChapterState
                {
                    Number = 3,
                    Title = "Chapter Three",
                    State = ChapterStateType.Unstarted,
                    Summary = "Summary 3"
                }
            ],
            PipelineStage = PipelineStageType.DraftingChapters
        };

        SetupWorkspaceState(state);
        _fileSystem.AddDirectory(TestChaptersDir);

        AddChapterFile(1, "chapter_01_final.md", "Content for chapter 1.");

        var chapter2DirPath = _fileSystem.Path.Join(TestChaptersDir, "chapter_02");
        _fileSystem.AddDirectory(chapter2DirPath);

        // Act
        await _sut.ExportStoryAsync(null, CancellationToken.None);

        // Assert
        var exportedFilePath = _fileSystem.Path.Join(TestChaptersDir, DefaultExportFileName);
        Assert.True(_fileSystem.FileExists(exportedFilePath), $"Exported file not found at {exportedFilePath}");
        var exportedContent = await _fileSystem.File.ReadAllTextAsync(exportedFilePath);

        await Verify(exportedContent).UseDirectory("Snapshots").UseFileName("ExportService_MissingContent");
    }

    [Fact]
    public async Task ExportStoryAsync_NoChaptersInState_LogsAndReturnsWithoutExporting()
    {
        // Arrange
        var state = new WorkspaceState
        {
            Chapters = [],
            PipelineStage = PipelineStageType.DraftingChapters
        };

        SetupWorkspaceState(state);
        _fileSystem.AddDirectory(TestChaptersDir);

        // Act
        await _sut.ExportStoryAsync(null, CancellationToken.None);

        // Assert
        var exportedFilePath = _fileSystem.Path.Join(TestChaptersDir, DefaultExportFileName);

        Assert.False(_fileSystem.FileExists(exportedFilePath),
            "Exported file should not be created if no chapters in state.");
    }

    [Fact]
    public async Task ExportStoryAsync_NullWorkspaceState_LogsAndReturnsWithoutExporting()
    {
        // Arrange
        // Simulate LoadWorkspaceStateAsync returning null by providing a corrupt state file.
        var stateFilePath = _fileSystem.Path.Join(TestScribalDir, "state.json");
        _fileSystem.AddFile(stateFilePath, new("invalid json"));
        _fileSystem.AddDirectory(TestChaptersDir);

        // Act
        await _sut.ExportStoryAsync(null, CancellationToken.None);

        // Assert
        var exportedFilePath = _fileSystem.Path.Join(TestChaptersDir, DefaultExportFileName);

        Assert.False(_fileSystem.FileExists(exportedFilePath),
            "Exported file should not be created if workspace state is null.");
    }

    [Fact]
    public async Task ExportStoryAsync_WithCustomOutputFileName_UsesCorrectName()
    {
        // Arrange
        var state = new WorkspaceState
        {
            Chapters =
            [
                new ChapterState
                {
                    Number = 1,
                    Title = "Chapter One",
                    State = ChapterStateType.Done,
                    Summary = "Summary 1"
                }
            ],
            PipelineStage = PipelineStageType.DraftingChapters
        };

        SetupWorkspaceState(state);
        _fileSystem.AddDirectory(TestChaptersDir);
        AddChapterFile(1, "chapter_01_final.md", "Content for chapter 1.");

        var customOutputFileName = "MyStory_Exported.md";

        // Act
        await _sut.ExportStoryAsync(customOutputFileName, CancellationToken.None);

        // Assert
        var exportedFilePath = _fileSystem.Path.Join(TestChaptersDir, customOutputFileName);
        Assert.True(_fileSystem.FileExists(exportedFilePath), $"Exported file not found at {exportedFilePath}");
        var exportedContent = await _fileSystem.File.ReadAllTextAsync(exportedFilePath);

        await Verify(exportedContent).UseDirectory("Snapshots").UseFileName("ExportService_CustomOutputName");
    }

    [Fact]
    public async Task ExportStoryAsync_WithCustomOutputFileNameWithoutExtension_AddsMdExtension()
    {
        // Arrange
        var state = new WorkspaceState
        {
            Chapters =
            [
                new ChapterState
                {
                    Number = 1,
                    Title = "Chapter One",
                    State = ChapterStateType.Done,
                    Summary = "Summary 1"
                }
            ],
            PipelineStage = PipelineStageType.DraftingChapters
        };

        SetupWorkspaceState(state);
        _fileSystem.AddDirectory(TestChaptersDir);
        AddChapterFile(1, "chapter_01_final.md", "Content for chapter 1.");

        var customOutputFileNameWithoutExt = "MyStory_Exported_NoExt";
        var expectedOutputFileNameWithExt = "MyStory_Exported_NoExt.md";

        // Act
        await _sut.ExportStoryAsync(customOutputFileNameWithoutExt, CancellationToken.None);

        // Assert
        var exportedFilePath = _fileSystem.Path.Join(TestChaptersDir, expectedOutputFileNameWithExt);
        Assert.True(_fileSystem.FileExists(exportedFilePath), $"Exported file not found at {exportedFilePath}");
    }

    [Fact]
    public async Task ExportStoryAsync_NotInWorkspace_ThrowsInvalidOperationException()
    {
        // Arrange
        // Ensure a clean file system state for this test to correctly simulate "not in workspace"
        var localFileSystem = new MockFileSystem();
        localFileSystem.Directory.SetCurrentDirectory(TestProjectRootDir);

        var localWorkspaceManager = new WorkspaceManager(localFileSystem,
            Substitute.For<IGitService>(),
            Substitute.For<IUserInteraction>(),
            Options.Create(new AppConfig()),
            NullLogger<WorkspaceManager>.Instance);

        var sutNotInWorkspace = new ExportService(localFileSystem,
            localWorkspaceManager,
            Substitute.For<IAnsiConsole>(),
            NullLogger<ExportService>.Instance);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sutNotInWorkspace.ExportStoryAsync(null, CancellationToken.None));

        Assert.Equal("Attempted to export story when not in a workspace.", exception.Message);
    }
}