using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Configuration;
using Scribal.Cli;
using Xunit;

namespace Scribal.Tests;

public class DiffEditorTests
{
    private readonly MockFileSystem _fileSystem;
    private readonly IConfiguration _configuration;
    private readonly DiffEditor _diffEditor;

    public DiffEditorTests()
    {
        // Setup mock file system
        _fileSystem = new MockFileSystem();
        
        // Setup configuration
        var configBuilder = new ConfigurationBuilder();
        configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
        {
            { "DryRun", "false" }
        });
        _configuration = configBuilder.Build();
        
        // Create the DiffEditor instance
        _diffEditor = new DiffEditor(_fileSystem, _configuration);
    }

    [Fact]
    public async Task ApplyUnifiedDiffAsync_ShouldAddLine()
    {
        // Arrange
        var filePath = "/test/file.txt";
        var originalContent = new[]
        {
            "Line 1",
            "Line 2",
            "Line 3"
        };
        
        _fileSystem.AddFile(filePath, new MockFileData(string.Join(Environment.NewLine, originalContent)));
        
        var diff = @"--- a/test/file.txt
+++ b/test/file.txt
@@ -2,1 +2,2 @@
 Line 2
+New Line";

        // Act
        await _diffEditor.ApplyUnifiedDiffAsync(filePath, diff);

        // Assert
        var result = _fileSystem.GetFile(filePath).TextContents.Split(Environment.NewLine);
        Assert.Equal(4, result.Length);
        Assert.Equal("Line 1", result[0]);
        Assert.Equal("Line 2", result[1]);
        Assert.Equal("New Line", result[2]);
        Assert.Equal("Line 3", result[3]);
    }

    [Fact]
    public async Task ApplyUnifiedDiffAsync_ShouldRemoveLine()
    {
        // Arrange
        var filePath = "/test/file.txt";
        var originalContent = new[]
        {
            "Line 1",
            "Line 2",
            "Line 3",
            "Line 4"
        };
        
        _fileSystem.AddFile(filePath, new MockFileData(string.Join(Environment.NewLine, originalContent)));
        
        var diff = @"--- a/test/file.txt
+++ b/test/file.txt
@@ -2,2 +2,1 @@
 Line 2
-Line 3";

        // Act
        await _diffEditor.ApplyUnifiedDiffAsync(filePath, diff);

        // Assert
        var result = _fileSystem.GetFile(filePath).TextContents.Split(Environment.NewLine);
        Assert.Equal(3, result.Length);
        Assert.Equal("Line 1", result[0]);
        Assert.Equal("Line 2", result[1]);
        Assert.Equal("Line 4", result[2]);
    }

    [Fact]
    public async Task ApplyUnifiedDiffAsync_ShouldReplaceLine()
    {
        // Arrange
        var filePath = "/test/file.txt";
        var originalContent = new[]
        {
            "Line 1",
            "Line 2",
            "Line 3"
        };
        
        _fileSystem.AddFile(filePath, new MockFileData(string.Join(Environment.NewLine, originalContent)));
        
        var diff = @"--- a/test/file.txt
+++ b/test/file.txt
@@ -2,2 +2,2 @@
 Line 2
-Line 3
+Modified Line 3";

        // Act
        await _diffEditor.ApplyUnifiedDiffAsync(filePath, diff);

        // Assert
        var result = _fileSystem.GetFile(filePath).TextContents.Split(Environment.NewLine);
        Assert.Equal(3, result.Length);
        Assert.Equal("Line 1", result[0]);
        Assert.Equal("Line 2", result[1]);
        Assert.Equal("Modified Line 3", result[2]);
    }

    [Fact]
    public async Task ApplyUnifiedDiffAsync_ShouldRespectDryRunSetting()
    {
        // Arrange
        var filePath = "/test/file.txt";
        var originalContent = new[]
        {
            "Line 1",
            "Line 2",
            "Line 3"
        };
        
        _fileSystem.AddFile(filePath, new MockFileData(string.Join(Environment.NewLine, originalContent)));
        
        // Create a new configuration with DryRun set to true
        var dryRunConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "DryRun", "true" }
            })
            .Build();
        
        var dryRunEditor = new DiffEditor(_fileSystem, dryRunConfig);
        
        var diff = @"--- a/test/file.txt
+++ b/test/file.txt
@@ -2,2 +2,2 @@
 Line 2
-Line 3
+Modified Line 3";

        // Act
        await dryRunEditor.ApplyUnifiedDiffAsync(filePath, diff);

        // Assert - file should remain unchanged
        var result = _fileSystem.GetFile(filePath).TextContents.Split(Environment.NewLine);
        Assert.Equal(3, result.Length);
        Assert.Equal("Line 1", result[0]);
        Assert.Equal("Line 2", result[1]);
        Assert.Equal("Line 3", result[2]); // Not modified because of DryRun
    }
}
