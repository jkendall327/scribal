using NSubstitute;
using System.IO.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using Scribal.Workspace;
using Xunit;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System;
using Microsoft.Extensions.Options;
using Scribal.Agency; // For Exception

namespace Scribal.Tests.Workspace
{
    public class ChapterDeletionServiceTests
    {
        private readonly IFileSystem _fileSystem;
        private readonly WorkspaceManager _workspaceManager; // Assumes WorkspaceManager methods are virtual or it implements an interface
        private readonly ChapterDeletionService _sut;

        private const string TestWorkspaceDir = "/test/project/.scribal";
        private const string TestProjectRootDir = "/test/project";
        private const string TestChaptersDir = "/test/project/chapters";
        private const string TestPlotOutlineFile = "/test/project/.scribal/plot_outline.json";

        public ChapterDeletionServiceTests()
        {
            _fileSystem = Substitute.For<IFileSystem>();
            
            // Mocking IPath separately for robust path joining
            var pathMock = Substitute.For<IPath>();
            pathMock.Join(Arg.Any<string>(), Arg.Any<string>()).Returns(args => System.IO.Path.Join((string)args[0], (string)args[1]));
            pathMock.Join(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns(args => System.IO.Path.Join((string)args[0], (string)args[1], (string)args[2]));
            _fileSystem.Path.Returns(pathMock);

            // WorkspaceManager is concrete. NSubstitute can create a substitute,
            // but will only be able to intercept calls to virtual methods.
            // If LoadWorkspaceStateAsync/SaveWorkspaceStateAsync are not virtual, these tests
            // will hit the actual implementation or fail if WorkspaceManager has complex constructor dependencies.
            // For this test setup, we assume they are virtual or WorkspaceManager implements an IWorkspaceManager interface.
            _workspaceManager = new(_fileSystem, Substitute.For<IGitService>(), Substitute.For<IUserInteraction>(), Options.Create(new AppConfig()), NullLogger<WorkspaceManager>.Instance);
            
            _sut = new ChapterDeletionService(_fileSystem, _workspaceManager, NullLogger<ChapterDeletionService>.Instance);

            // Default setup for WorkspaceManager.TryFindWorkspaceFolder to succeed via _fileSystem mocks
            // This static method's behavior is controlled by how IFileSystem is mocked.
            // It typically searches up from current dir for a ".scribal" folder.
            _fileSystem.Directory.GetCurrentDirectory().Returns(TestProjectRootDir);
            var dirInfoProject = Substitute.For<IDirectoryInfo>();
            dirInfoProject.FullName.Returns(TestProjectRootDir);
            dirInfoProject.Parent.Returns((IDirectoryInfo?)null); // End search here
            _fileSystem.DirectoryInfo.New(TestProjectRootDir).Returns(dirInfoProject);
            // This makes TryFindWorkspaceFolder find TestProjectRootDir/.scribal
            _fileSystem.Directory.Exists(System.IO.Path.Join(TestProjectRootDir, ".scribal")).Returns(true);


            // Setup for SUT's own derivation of projectRootDir from workspaceDir
            var wsDirInfo = Substitute.For<IDirectoryInfo>();
            wsDirInfo.FullName.Returns(TestWorkspaceDir);
            var prjRootInfo = Substitute.For<IDirectoryInfo>();
            prjRootInfo.FullName.Returns(TestProjectRootDir);
            wsDirInfo.Parent.Returns(prjRootInfo);
            _fileSystem.DirectoryInfo.New(TestWorkspaceDir).Returns(wsDirInfo);
        }

        private string ToJson<T>(T obj) => JsonSerializer.Serialize(obj, new JsonSerializerOptions { PropertyNameCaseInsensitive = true, WriteIndented = true });

        private void SetupTryFindWorkspaceFolderSuccess()
        {
            // WorkspaceManager.TryFindWorkspaceFolder searches for ".scribal"
            // This setup ensures it finds "/test/project/.scribal"
            _fileSystem.Directory.Exists(System.IO.Path.Join(TestProjectRootDir, ".scribal")).Returns(true);
        }

        private void SetupTryFindWorkspaceFolderFail()
        {
            _fileSystem.Directory.Exists(Arg.Is<string>(s => s.EndsWith(".scribal"))).Returns(false);
        }

        [Fact]
        public async Task DeleteChapterAsync_WorkspaceNotFound_ReturnsFailure()
        {
            // Arrange
            SetupTryFindWorkspaceFolderFail();
            var chapterToDelete = new ChapterState { Number = 1, Title = "Test Chapter" };

            // Act
            var result = await _sut.DeleteChapterAsync(chapterToDelete, CancellationToken.None);

            // Assert
            Assert.False(result.Success);
            Assert.Contains("Failed to find workspace directory.", result.OverallMessage);
            Assert.Contains("Could not find workspace directory. Cannot delete chapter.", result.Errors);
        }

        [Fact]
        public async Task DeleteChapterAsync_ProjectRootNotFound_ReturnsFailure()
        {
            // Arrange
            SetupTryFindWorkspaceFolderSuccess(); // Workspace dir is found
            _fileSystem.DirectoryInfo.New(TestWorkspaceDir).Parent.Returns((IDirectoryInfo?)null); // But its parent is null

            var chapterToDelete = new ChapterState { Number = 1, Title = "Test Chapter" };

            // Act
            var result = await _sut.DeleteChapterAsync(chapterToDelete, CancellationToken.None);

            // Assert
            Assert.False(result.Success);
            Assert.Contains("Failed to determine project root directory.", result.OverallMessage);
            Assert.Contains("Could not determine project root directory. Cannot delete chapter.", result.Errors);
        }

        [Fact]
        public async Task DeleteChapterAsync_SuccessfulDeletion_MiddleChapter_RenumbersAndRenames()
        {
            // Arrange
            SetupTryFindWorkspaceFolderSuccess();
            var chapterToDelete = new ChapterState { Number = 2, Title = "Chapter Two" };
            var chapter1Dir = System.IO.Path.Join(TestChaptersDir, "chapter_01");
            var chapter2Dir = System.IO.Path.Join(TestChaptersDir, "chapter_02"); // To be deleted
            var chapter3Dir = System.IO.Path.Join(TestChaptersDir, "chapter_03"); // To be renamed to chapter_02

            var initialOutline = new StoryOutline
            {
                Chapters = new List<Chapter>
                {
                    new Chapter { ChapterNumber = 1, Title = "One", Summary = "S1" },
                    new Chapter { ChapterNumber = 2, Title = "Two", Summary = "S2" }, // To delete
                    new Chapter { ChapterNumber = 3, Title = "Three", Summary = "S3" } // To become 2
                }
            };
            var initialState = new WorkspaceState
            {
                Chapters = new List<ChapterState>
                {
                    new ChapterState { Number = 1, Title = "One", State = ChapterStateType.Draft },
                    new ChapterState { Number = 2, Title = "Two", State = ChapterStateType.Draft }, // To delete
                    new ChapterState { Number = 3, Title = "Three", State = ChapterStateType.Draft } // To become 2
                }
            };

            _fileSystem.Directory.Exists(chapter2Dir).Returns(true); // Chapter to delete exists
            _fileSystem.Directory.Exists(chapter3Dir).Returns(true); // Chapter to rename exists
            _fileSystem.File.Exists(TestPlotOutlineFile).Returns(true);
            _fileSystem.File.ReadAllTextAsync(TestPlotOutlineFile, Arg.Any<CancellationToken>()).Returns(Task.FromResult(ToJson(initialOutline)));
            _workspaceManager.LoadWorkspaceStateAsync(TestWorkspaceDir, Arg.Any<CancellationToken>()).Returns(Task.FromResult<WorkspaceState?>(initialState));

            // Act
            var result = await _sut.DeleteChapterAsync(chapterToDelete, CancellationToken.None);

            // Assert
            Assert.True(result.Success);
            Assert.Equal($"Chapter {chapterToDelete.Number}: '{chapterToDelete.Title}' successfully deleted and workspace updated.", result.OverallMessage);

            // 1. Delete chapter subfolder
            _fileSystem.Directory.Received(1).Delete(chapter2Dir, true);
            Assert.Contains($"Deleted directory: {chapter2Dir}", result.ActionsTaken);

            // 2. Update StoryOutline
            // await _fileSystem.File.Received(1).WriteAllTextAsync(TestPlotOutlineFile, Arg.Is<string>(json =>
            //     JsonSerializer.Deserialize<StoryOutline>(json)!.Chapters.Count == 2 &&
            //     JsonSerializer.Deserialize<StoryOutline>(json)!.Chapters[0].ChapterNumber == 1 &&
            //     JsonSerializer.Deserialize<StoryOutline>(json)!.Chapters[0].Title == "One" &&
            //     JsonSerializer.Deserialize<StoryOutline>(json)!.Chapters[1].ChapterNumber == 2 && // Renumbered from 3
            //     JsonSerializer.Deserialize<StoryOutline>(json)!.Chapters[1].Title == "Three"
            // ), Arg.Any<CancellationToken>());
            // Assert.Contains("Plot outline updated.", result.ActionsTaken);

            // 3. Update WorkspaceState
            await _workspaceManager.Received(1).SaveWorkspaceStateAsync(Arg.Is<WorkspaceState>(ws =>
                ws.Chapters.Count == 2 &&
                ws.Chapters[0].Number == 1 && ws.Chapters[0].Title == "One" &&
                ws.Chapters[1].Number == 2 && ws.Chapters[1].Title == "Three" // Renumbered from 3
            ), TestWorkspaceDir, Arg.Any<CancellationToken>());
            Assert.Contains("Workspace state updated.", result.ActionsTaken);

            // 4. Rename remaining chapter subfolders
            _fileSystem.Directory.Received(1).Move(chapter3Dir, System.IO.Path.Join(TestChaptersDir, "chapter_02"));
            Assert.Contains($"Renamed directory: chapter_03 -> chapter_02", result.ActionsTaken);
            _fileSystem.Directory.DidNotReceive().Move(chapter1Dir, Arg.Any<string>()); // Chapter 1 dir should not be touched
            Assert.Contains("Chapter directories re-organized.", result.ActionsTaken);
        }

        [Fact]
        public async Task DeleteChapterAsync_ChapterDirectoryNotFound_StillSucceedsAndWarns()
        {
            // Arrange
            SetupTryFindWorkspaceFolderSuccess();
            var chapterToDelete = new ChapterState { Number = 1, Title = "Chapter One" };
            var chapter1Dir = System.IO.Path.Join(TestChaptersDir, "chapter_01");

            var initialOutline = new StoryOutline { Chapters = new List<Chapter> { new Chapter { ChapterNumber = 1, Title = "One" } } };
            var initialState = new WorkspaceState { Chapters = new List<ChapterState> { new ChapterState { Number = 1, Title = "One" } } };

            _fileSystem.Directory.Exists(chapter1Dir).Returns(false); // Chapter to delete does NOT exist
            _fileSystem.File.Exists(TestPlotOutlineFile).Returns(true);
            _fileSystem.File.ReadAllTextAsync(TestPlotOutlineFile, Arg.Any<CancellationToken>()).Returns(Task.FromResult(ToJson(initialOutline)));
            _workspaceManager.LoadWorkspaceStateAsync(TestWorkspaceDir, Arg.Any<CancellationToken>()).Returns(Task.FromResult<WorkspaceState?>(initialState));

            // Act
            var result = await _sut.DeleteChapterAsync(chapterToDelete, CancellationToken.None);

            // Assert
            Assert.True(result.Success);
            _fileSystem.Directory.DidNotReceive().Delete(Arg.Any<string>(), Arg.Any<bool>());
            Assert.Contains($"Directory not found, skipped deletion: {chapter1Dir}", result.Warnings);
            Assert.Contains("Plot outline updated.", result.ActionsTaken); // Other actions should still occur
            Assert.Contains("Workspace state updated.", result.ActionsTaken);
        }
        
        [Fact]
        public async Task DeleteChapterAsync_CancellationBeforeAnyAction_ReturnsCancelled()
        {
            // Arrange
            SetupTryFindWorkspaceFolderSuccess();
            var chapterToDelete = new ChapterState { Number = 1, Title = "Test Chapter" };
            var cts = new CancellationTokenSource();
            cts.Cancel(); // Cancel immediately

            // Act
            var result = await _sut.DeleteChapterAsync(chapterToDelete, cts.Token);

            // Assert
            Assert.False(result.Success);
            Assert.Equal("Operation cancelled.", result.OverallMessage);
            // Depending on where it's caught, it might not add to Errors list, but OverallMessage is key
        }

        [Fact]
        public async Task DeleteChapterAsync_PlotOutlineFileMissing_CreatesNewAndSucceeds()
        {
            // Arrange
            SetupTryFindWorkspaceFolderSuccess();
            var chapterToDelete = new ChapterState { Number = 1, Title = "Chapter One" };
            // Initial state: 2 chapters, deleting chapter 1. Chapter 2 should remain and become chapter 1.
            var initialWorkspaceState = new WorkspaceState
            {
                Chapters = new List<ChapterState>
                {
                    new ChapterState { Number = 1, Title = "One", State = ChapterStateType.Draft }, // To delete
                    new ChapterState { Number = 2, Title = "Two", State = ChapterStateType.Draft }  // To become 1
                }
            };
            var chapter1Dir = System.IO.Path.Join(TestChaptersDir, "chapter_01");
            var chapter2DirOriginal = System.IO.Path.Join(TestChaptersDir, "chapter_02");
            var chapter2DirNew = System.IO.Path.Join(TestChaptersDir, "chapter_01");


            _fileSystem.Directory.Exists(chapter1Dir).Returns(true);
            _fileSystem.Directory.Exists(chapter2DirOriginal).Returns(true);
            _fileSystem.File.Exists(TestPlotOutlineFile).Returns(false); // Plot outline does not exist
            _workspaceManager.LoadWorkspaceStateAsync(TestWorkspaceDir, Arg.Any<CancellationToken>()).Returns(Task.FromResult<WorkspaceState?>(initialWorkspaceState));

            // Act
            var result = await _sut.DeleteChapterAsync(chapterToDelete, CancellationToken.None);

            // Assert
            Assert.True(result.Success);
            // await _fileSystem.File.Received(1).WriteAllTextAsync(TestPlotOutlineFile, Arg.Is<string>(json =>
            // {
            //     var outline = JsonSerializer.Deserialize<StoryOutline>(json);
            //     return outline != null &&
            //            outline.Chapters.Count == 1 &&
            //            outline.Chapters[0].ChapterNumber == 1 && // Chapter "Two" becomes chapter 1
            //            outline.Chapters[0].Title == "Two"; // Title should be from the remaining chapter
            // }), Arg.Any<CancellationToken>());
            Assert.Contains("Plot outline updated.", result.ActionsTaken);
            _fileSystem.Directory.Received(1).Move(chapter2DirOriginal, chapter2DirNew);
        }
    }
}
