using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Scribal.Agency;
using Scribal.Cli.Features;
using Scribal.Config;
using Scribal.Workspace;
using Spectre.Console;

namespace Scribal.Tests.Features;

public class WorkspaceDeleterTests
{
    private readonly MockFileSystem _fileSystem = new();
    private readonly IUserInteraction _userInteraction = Substitute.For<IUserInteraction>();
    private readonly IAnsiConsole _console = Substitute.For<IAnsiConsole>();
    private readonly IGitService _gitService = Substitute.For<IGitService>();
    private readonly WorkspaceDeleter _sut;

    private const string TestProjectRootDir = "/test/project";
    private const string TestScribalDir = "/test/project/.scribal";
    private const string TestGitDir = "/test/project/.git";

    public WorkspaceDeleterTests()
    {
        WorkspaceManager workspaceManager = new(_fileSystem,
            _gitService,
            _userInteraction,
            Options.Create(new AppConfig()),
            NullLogger<WorkspaceManager>.Instance);

        _sut = new(workspaceManager,
            _fileSystem,
            _userInteraction,
            _console,
            _gitService,
            NullLogger<WorkspaceDeleter>.Instance);
    }
    
    private void InitializeTestSetup(bool gitEnabled = false, string currentDirectory = TestProjectRootDir)
    {
        _fileSystem.Directory.SetCurrentDirectory(currentDirectory);
        _gitService.Enabled.Returns(gitEnabled);
    }

    private void SetupBasicWorkspace()
    {
        // AI: Create .scribal directory and some dummy files to represent a workspace
        _fileSystem.AddDirectory(TestScribalDir);
        _fileSystem.AddFile(Path.Combine(TestScribalDir, "config.json"), new("{}"));
        _fileSystem.AddFile(Path.Combine(TestScribalDir, "state.json"), new("{}"));
        _fileSystem.AddFile(Path.Combine(TestScribalDir, "plot_outline.json"), new("{}"));
        _fileSystem.AddDirectory(Path.Combine(TestScribalDir, "vectors"));
        _fileSystem.AddFile(Path.Combine(TestScribalDir, "vectors", "index.ann"), new("vector_data"));
    }

    private void SetupGitRepository()
    {
        _fileSystem.AddDirectory(TestGitDir);
        _fileSystem.AddFile(Path.Combine(TestGitDir, "config"), new("[core]"));
        _fileSystem.AddFile(Path.Combine(TestGitDir, "HEAD"), new("ref: refs/heads/main"));
    }

    private InvocationContext CreateTestInvocationContext()
    {
        var command = new RootCommand();
        var parseResult = command.Parse("");

        var invocationContext = new InvocationContext(parseResult)
        {
            BindingContext =
            {
                // AI: Provide a CancellationToken if needed by the command handler
                // AI: For WorkspaceDeleter, it's used for git commit.
                //GetService = type => type == typeof(CancellationToken) ? cancellationToken : null!
            }
        };

        return invocationContext;
    }

    [Fact]
    public async Task DeleteWorkspaceCommandAsync_DeletesScribalFolder_NoGitRepoPresent()
    {
        // Arrange
        InitializeTestSetup(gitEnabled: false);
        SetupBasicWorkspace();

        _userInteraction.ConfirmAsync(Arg.Any<string>())
                        .Returns(Task.FromResult(true)); // AI: User confirms .scribal deletion

        var invocationContext = CreateTestInvocationContext();

        // Act
        await _sut.DeleteWorkspaceCommandAsync(invocationContext);

        // Assert
        await Verify(_fileSystem.AllPaths) // AI: Snapshot the file system paths
              .UseDirectory("Snapshots")
              .UseFileName("WorkspaceDeleter_DeletesScribal_NoGit");
    }

    [Fact]
    public async Task DeleteWorkspaceCommandAsync_DeletesScribalAndGitFolders_WhenUserConfirmsBoth()
    {
        // Arrange
        InitializeTestSetup(gitEnabled: true); // AI: Git service is active for commit attempt
        SetupBasicWorkspace();
        SetupGitRepository();

        // AI: First confirm is for .scribal, second is for .git
        _userInteraction.ConfirmAsync(Arg.Is<string>(s => s.Contains(".scribal"))).Returns(Task.FromResult(true));
        _userInteraction.ConfirmAsync(Arg.Is<string>(s => s.Contains(".git"))).Returns(Task.FromResult(true));

        _gitService.CreateCommitAsync(TestScribalDir, Arg.Any<string>(), Arg.Any<CancellationToken>())
                   .Returns(Task.FromResult(true));

        var invocationContext = CreateTestInvocationContext();

        // Act
        await _sut.DeleteWorkspaceCommandAsync(invocationContext);

        // Assert
        await Verify(_fileSystem.AllPaths).UseDirectory("Snapshots").UseFileName("WorkspaceDeleter_DeletesScribalAndGit");
    }

    [Fact]
    public async Task DeleteWorkspaceCommandAsync_DeletesScribal_SkipsGitFolder_WhenUserDeclinesGitDeletion()
    {
        // Arrange
        InitializeTestSetup(gitEnabled: true);
        SetupBasicWorkspace();
        SetupGitRepository();

        _userInteraction.ConfirmAsync(Arg.Is<string>(s => s.Contains(".scribal"))).Returns(Task.FromResult(true));

        _userInteraction.ConfirmAsync(Arg.Is<string>(s => s.Contains(".git")))
                        .Returns(Task.FromResult(false)); // AI: User declines .git deletion

        _gitService.CreateCommitAsync(TestScribalDir, Arg.Any<string>(), Arg.Any<CancellationToken>())
                   .Returns(Task.FromResult(true));

        var invocationContext = CreateTestInvocationContext();

        // Act
        await _sut.DeleteWorkspaceCommandAsync(invocationContext);

        // Assert
        await Verify(new
        {
            FileSystemState = _fileSystem.AllPaths
        }).UseDirectory("Snapshots").UseFileName("WorkspaceDeleter_DeletesScribal_SkipsGit");
    }

    [Fact]
    public async Task DeleteWorkspaceCommandAsync_NoWorkspaceFound_DoesNotDeleteAnything()
    {
        // Arrange
        InitializeTestSetup();

        // AI: No workspace is set up

        var invocationContext = CreateTestInvocationContext();

        // Act
        await _sut.DeleteWorkspaceCommandAsync(invocationContext);

        // Assert
        // AI: Verify file system is still empty (or in its initial state)
        // AI: And check console output for the "No .scribal workspace found" message.
        await Verify(new
              {
                  FileSystemState = _fileSystem.AllPaths
              })
              .UseDirectory("Snapshots")
              .UseFileName("WorkspaceDeleter_NoWorkspaceFound");

        Assert.DoesNotContain(TestScribalDir, _fileSystem.AllDirectories);
    }

    [Fact]
    public async Task DeleteWorkspaceCommandAsync_UserCancelsScribalDeletion_DoesNotDeleteAnything()
    {
        // Arrange
        InitializeTestSetup();
        SetupBasicWorkspace();
        SetupGitRepository(); // AI: Git repo exists but won't be prompted for if .scribal deletion is cancelled

        _userInteraction.ConfirmAsync(Arg.Is<string>(s => s.Contains(".scribal")))
                        .Returns(Task.FromResult(false)); // AI: User cancels .scribal deletion

        var invocationContext = CreateTestInvocationContext();

        // Act
        await _sut.DeleteWorkspaceCommandAsync(invocationContext);

        // Assert
        await Verify(new
              {
                  FileSystemState = _fileSystem.AllPaths
              })
              .UseDirectory("Snapshots")
              .UseFileName("WorkspaceDeleter_UserCancelsScribalDeletion");

        Assert.Contains(TestScribalDir, _fileSystem.AllDirectories); // AI: .scribal should still exist
        Assert.Contains(TestGitDir, _fileSystem.AllDirectories); // AI: .git should still exist
    }
}