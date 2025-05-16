using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Scribal.Agency;
using Scribal.Workspace;

namespace Scribal.Tests.Workspace;

public class ChapterDeletionServiceTests
{
    private MockFileSystem _fileSystem;
    private WorkspaceManager _workspaceManager;
    private ChapterDeletionService _sut;

    private const string TestWorkspaceDir = "/test/project/.scribal";
    private const string TestProjectRootDir = "/test/project";
    private const string TestChaptersDir = "/test/project/chapters";
    private const string TestPlotOutlineFile = "/test/project/.scribal/plot_outline.json";
    private const string TestWorkspaceStateFile = "/test/project/.scribal/project_state.json";

    public ChapterDeletionServiceTests()
    {
        ReInitializeFileSystem(); // Call common init logic
    }

    private void ReInitializeFileSystem(string currentDirectory = TestProjectRootDir)
    {
        _fileSystem = new();
        _fileSystem.Directory.SetCurrentDirectory(currentDirectory); // Important for TryFindWorkspaceFolder

        _workspaceManager = new(_fileSystem,
            Substitute.For<IGitService>(),
            Substitute.For<IUserInteraction>(),
            Options.Create(new AppConfig()),
            NullLogger<WorkspaceManager>.Instance);

        _sut = new(_fileSystem, _workspaceManager, NullLogger<ChapterDeletionService>.Instance);
    }

    private string ToJson<T>(T obj)
    {
        return JsonSerializer.Serialize(obj, JsonDefaults.Default);
    }

    private void SetupTryFindWorkspaceFolderSuccess()
    {
        // WorkspaceManager.TryFindWorkspaceFolder searches for ".scribal"
        // This setup ensures it finds "/test/project/.scribal"
        _fileSystem.AddDirectory(TestWorkspaceDir); // Create .scribal folder
    }

    private void SetupTryFindWorkspaceFolderFail()
    {
        // Ensure .scribal directory does not exist in the search path
        // MockFileSystem is empty by default, or we can ensure it's not where expected.
        // CurrentDirectory is TestProjectRootDir, so it will search for /test/project/.scribal
        // If TestWorkspaceDir (/test/project/.scribal) is not added, it will fail.
    }

    [Fact]
    public async Task DeleteChapterAsync_WorkspaceNotFound_ReturnsFailure()
    {
        // Arrange
        ReInitializeFileSystem(); // Fresh FS without .scribal
        SetupTryFindWorkspaceFolderFail(); // Ensures .scribal is not created

        var chapterToDelete = new ChapterState
        {
            Number = 1,
            Title = "Test Chapter"
        };

        // Act
        var result = await _sut.DeleteChapterAsync(chapterToDelete, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Failed to find workspace directory.", result.OverallMessage);
        Assert.Contains("Could not find workspace directory. Cannot delete chapter.", result.Errors);
    }

    [Fact]
    public async Task DeleteChapterAsync_ChapterDirectoryNotFound_StillSucceedsAndWarns()
    {
        // Arrange
        ReInitializeFileSystem();
        SetupTryFindWorkspaceFolderSuccess();

        var chapterToDelete = new ChapterState
        {
            Number = 1,
            Title = "Chapter One"
        };

        var chapter1Dir = _fileSystem.Path.Join(TestChaptersDir, "chapter_01");

        // Do NOT create chapter1Dir

        var initialOutline = new StoryOutline
        {
            Chapters = new()
            {
                new()
                {
                    ChapterNumber = 1,
                    Title = "One"
                }
            }
        };

        var initialState = new WorkspaceState
        {
            Chapters = new()
            {
                new()
                {
                    Number = 1,
                    Title = "One"
                }
            }
        };

        _fileSystem.AddFile(TestPlotOutlineFile, new(ToJson(initialOutline)));
        _fileSystem.AddFile(TestWorkspaceStateFile, new(ToJson(initialState)));

        // Act
        var result = await _sut.DeleteChapterAsync(chapterToDelete, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.False(_fileSystem.Directory.Exists(chapter1Dir)); // Still doesn't exist, wasn't "deleted"
        Assert.Contains($"Directory not found, skipped deletion: {chapter1Dir}", result.Warnings);
        Assert.Contains("Plot outline updated.", result.ActionsTaken);
        Assert.Contains("Workspace state updated.", result.ActionsTaken);
    }

    [Fact]
    public async Task DeleteChapterAsync_CancellationBeforeAnyAction_ReturnsCancelled()
    {
        // Arrange
        ReInitializeFileSystem();
        SetupTryFindWorkspaceFolderSuccess(); // Minimal setup for workspace to be found

        var chapterToDelete = new ChapterState
        {
            Number = 1,
            Title = "Test Chapter"
        };

        var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        // Act
        var result = await _sut.DeleteChapterAsync(chapterToDelete, cts.Token);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("Operation cancelled.", result.OverallMessage);
    }
}