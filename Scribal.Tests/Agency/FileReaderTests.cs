using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Scribal.Agency;

namespace Scribal.Tests.Agency;

public class FileReaderTests
{
    private readonly MockFileSystem _fileSystem = new();
    private readonly FileReader _fileReader;

    public FileReaderTests()
    {
        _fileReader = new(_fileSystem, new(_fileSystem), NullLogger<FileReader>.Instance);
    }

    [Fact]
    public async Task ReadFileContentAsync_FileExistsInCwd_ReturnsFileContent()
    {
        var filePath = "testfile.txt";
        var fileContent = "This is a test file.";
        _fileSystem.AddFile(filePath, new(fileContent));
        _fileSystem.Directory.SetCurrentDirectory(_fileSystem.Path.GetFullPath("."));
        var result = await _fileReader.ReadFileContentAsync(filePath);
        Assert.Equal(fileContent, result);
    }

    [Fact]
    public async Task ReadFileContentAsync_FileDoesNotExist_ReturnsError()
    {
        var filePath = "nonexistentfile.txt";
        _fileSystem.Directory.SetCurrentDirectory(_fileSystem.Path.GetFullPath("."));
        var result = await _fileReader.ReadFileContentAsync(filePath);
        Assert.Equal(FileReader.FileNotFoundError, result);
    }

    [Fact]
    public async Task ReadFileContentAsync_FileOutsideCwd_ReturnsAccessDeniedError()
    {
        var outsideFilePath = "/outsidetest.txt";
        var cwd = "/example";
        _fileSystem.AddDirectory(cwd);
        _fileSystem.Directory.SetCurrentDirectory(cwd);
        var absoluteOutsidePath = _fileSystem.Path.GetFullPath(outsideFilePath);
        _fileSystem.AddFile(absoluteOutsidePath, new("This content should not be accessible."));
        var result = await _fileReader.ReadFileContentAsync(outsideFilePath);
        Assert.Equal(FileReader.AccessDeniedError, result);
    }

    [Fact]
    public async Task ReadFileContentAsync_FileOutsideCwdWithAbsolutePath_ReturnsAccessDeniedError()
    {
        var outsideDir = _fileSystem.Path.GetFullPath(_fileSystem.Path.Combine("..", "somedir"));
        var outsideFilePath = _fileSystem.Path.Combine(outsideDir, "absolutepathtest.txt");

        _fileSystem.AddDirectory(outsideDir);
        _fileSystem.AddFile(outsideFilePath, new("This content should not be accessible."));
        var cwd = _fileSystem.Path.GetFullPath("./current");
        _fileSystem.AddDirectory(cwd);
        _fileSystem.Directory.SetCurrentDirectory(cwd);
        var result = await _fileReader.ReadFileContentAsync(outsideFilePath);
        Assert.Equal(FileReader.AccessDeniedError, result);
    }

    [Fact]
    public async Task ReadFileContentAsync_FilePathIsCwdItself_ReturnsAccessDeniedError()
    {
        var cwd = _fileSystem.Path.GetFullPath(".");
        _fileSystem.AddDirectory(cwd);
        _fileSystem.Directory.SetCurrentDirectory(cwd);
        var result = await _fileReader.ReadFileContentAsync(cwd);
        Assert.Equal(FileReader.FileNotFoundError, result);
    }

    [Fact]
    public async Task ReadFileContentAsync_FilePathIsRelativeTraversalToSameFileInCwd_ReturnsFileContent()
    {
        var fileName = "testfile.txt";
        var fileContent = "This is a test file.";
        var cwd = _fileSystem.Path.GetFullPath("./project/src");
        var filePathInCwd = _fileSystem.Path.Combine(cwd, fileName);

        _fileSystem.AddFile(filePathInCwd, new(fileContent));
        _fileSystem.Directory.SetCurrentDirectory(cwd);

        var relativePath = _fileSystem.Path.Combine("..", "src", fileName);
        var result = await _fileReader.ReadFileContentAsync(relativePath);
        Assert.Equal(fileContent, result);
    }
}