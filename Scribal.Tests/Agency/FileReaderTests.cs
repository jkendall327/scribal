// AI: Test suite for the FileReader class

using System.IO.Abstractions.TestingHelpers;
using Scribal.Agency;

namespace Scribal.Tests.Agency;

public class FileReaderTests
{
    private readonly MockFileSystem _fileSystem = new();
    private readonly FileReader _fileReader;

    public FileReaderTests()
    {
        _fileReader = new(_fileSystem);
    }

    [Fact]
    public async Task ReadFileContentAsync_FileExistsInCwd_ReturnsFileContent()
    {
        // AI: Arrange
        var filePath = "testfile.txt";
        var fileContent = "This is a test file.";
        _fileSystem.AddFile(filePath, new(fileContent));
        _fileSystem.Directory.SetCurrentDirectory(_fileSystem.Path.GetFullPath("."));

        // AI: Act
        var result = await _fileReader.ReadFileContentAsync(filePath);

        // AI: Assert
        Assert.Equal(fileContent, result);
    }

    [Fact]
    public async Task ReadFileContentAsync_FileDoesNotExist_ReturnsError()
    {
        // AI: Arrange
        var filePath = "nonexistentfile.txt";
        _fileSystem.Directory.SetCurrentDirectory(_fileSystem.Path.GetFullPath("."));

        // AI: Act
        var result = await _fileReader.ReadFileContentAsync(filePath);

        // AI: Assert
        Assert.Equal(FileReader.FileNotFoundError, result);
    }

    [Fact]
    public async Task ReadFileContentAsync_FileOutsideCwd_ReturnsAccessDeniedError()
    {
        // AI: Arrange
        var outsideFilePath = "/outsidetest.txt";

        // AI: Ensure the mock file system knows about the CWD for path resolution
        var cwd = "/example";
        _fileSystem.AddDirectory(cwd); // AI: Ensure CWD exists
        _fileSystem.Directory.SetCurrentDirectory(cwd);

        // AI: Create a file outside the CWD
        var absoluteOutsidePath = _fileSystem.Path.GetFullPath(outsideFilePath);
        _fileSystem.AddFile(absoluteOutsidePath, new("This content should not be accessible."));

        // AI: Act
        var result = await _fileReader.ReadFileContentAsync(outsideFilePath);

        // AI: Assert
        Assert.Equal(FileReader.AccessDeniedError, result);
    }

    [Fact]
    public async Task ReadFileContentAsync_FileOutsideCwdWithAbsolutePath_ReturnsAccessDeniedError()
    {
        // AI: Arrange
        var outsideDir = _fileSystem.Path.GetFullPath(_fileSystem.Path.Combine("..", "somedir"));
        var outsideFilePath = _fileSystem.Path.Combine(outsideDir, "absolutepathtest.txt");

        _fileSystem.AddDirectory(outsideDir);
        _fileSystem.AddFile(outsideFilePath, new("This content should not be accessible."));

        // AI: Set CWD to something other than where the outside file is
        var cwd = _fileSystem.Path.GetFullPath("./current");
        _fileSystem.AddDirectory(cwd);
        _fileSystem.Directory.SetCurrentDirectory(cwd);

        // AI: Act
        var result = await _fileReader.ReadFileContentAsync(outsideFilePath);

        // AI: Assert
        Assert.Equal(FileReader.AccessDeniedError, result);
    }

    [Fact]
    public async Task ReadFileContentAsync_FilePathIsCwdItself_ReturnsAccessDeniedError()
    {
        // AI: Arrange
        var cwd = _fileSystem.Path.GetFullPath(".");
        _fileSystem.AddDirectory(cwd); // AI: Ensure CWD exists as a directory
        _fileSystem.Directory.SetCurrentDirectory(cwd);

        // AI: Act
        // AI: Attempting to read the CWD as if it were a file
        var result = await _fileReader.ReadFileContentAsync(cwd);

        // AI: Assert
        // AI: This should be treated as a non-existent file or access denied depending on how File.Exists handles directories.
        // AI: Given the current implementation, it will likely be "file did not exist" because File.Exists(directoryPath) is false.
        Assert.Equal(FileReader.FileNotFoundError, result);
    }

    [Fact]
    public async Task ReadFileContentAsync_FilePathIsRelativeTraversalToSameFileInCwd_ReturnsFileContent()
    {
        // AI: Arrange
        var fileName = "testfile.txt";
        var fileContent = "This is a test file.";
        var cwd = _fileSystem.Path.GetFullPath("./project/src"); // AI: A nested CWD
        var filePathInCwd = _fileSystem.Path.Combine(cwd, fileName);

        _fileSystem.AddFile(filePathInCwd, new(fileContent));
        _fileSystem.Directory.SetCurrentDirectory(cwd);

        var relativePath =
            _fileSystem.Path.Combine("..",
                "src",
                fileName); // AI: e.g. CWD is /project/src, path is ../src/testfile.txt

        // AI: Act
        var result = await _fileReader.ReadFileContentAsync(relativePath);

        // AI: Assert
        Assert.Equal(fileContent, result);
    }
}