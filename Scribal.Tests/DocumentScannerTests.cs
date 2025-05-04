using System.IO.Abstractions.TestingHelpers;
using Scribal.Context;

namespace Scribal.Tests;

public class DocumentScannerTests
{
    [Fact]
    public async Task ScanDirectoryForMarkdownAsync_WithComplexStructure_ReturnsCorrectTree()
    {
        // Arrange
        var filesystem = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            // Root level markdown files
            { "/project/README.md", new MockFileData("# Project Documentation\nThis is the main project documentation.") },
            { "/project/CONTRIBUTING.md", new MockFileData("# How to Contribute\nGuidelines for contributors.") },
            
            // Characters directory with markdown files
            { "/project/characters/protagonist.md", new MockFileData("# Main Character\nDetails about the protagonist.") },
            { "/project/characters/antagonist.md", new MockFileData("# Villain\nDetails about the antagonist.") },
            { "/project/characters/.hidden-character.md", new MockFileData("# Secret Character\nThis character is not revealed yet.") },
            
            // Chapters directory with nested structure
            { "/project/chapters/chapter1.md", new MockFileData("# Chapter 1\nThe beginning of the story.") },
            { "/project/chapters/chapter2.md", new MockFileData("# Chapter 2\nThe plot thickens.") },
            { "/project/chapters/drafts/chapter3-draft.md", new MockFileData("# Chapter 3 (Draft)\nStill working on this.") },
            { "/project/chapters/drafts/chapter4-draft.md", new MockFileData("# Chapter 4 (Draft)\nNeeds revision.") },
            
            // Empty directory
            { "/project/empty/", new MockDirectoryData() },
            
            // Hidden directory with markdown
            { "/project/.notes/important.md", new MockFileData("# Important Notes\nDon't forget these details.") },
            { "/project/.notes/timeline.md", new MockFileData("# Timeline\nChronological order of events.") },
            
            // Directory with mixed content
            { "/project/resources/images/cover.jpg", new MockFileData(new byte[] { 0x01, 0x02, 0x03, 0x04 }) },
            { "/project/resources/images/character-sketches.png", new MockFileData(new byte[] { 0x05, 0x06, 0x07, 0x08 }) },
            { "/project/resources/references.md", new MockFileData("# References\nSources and inspirations.") },
            { "/project/resources/outline.md", new MockFileData("# Story Outline\nThe overall structure.") },
            
            // Directory with non-markdown files only
            { "/project/assets/style.css", new MockFileData("body { font-family: Arial; }") },
            { "/project/assets/script.js", new MockFileData("console.log('Hello world');") },
        });
        
        var sut = new DocumentScanService(filesystem);

        var projectDir = filesystem.DirectoryInfo.New("/project");
        
        // Act
        var result = await sut.ScanDirectoryForMarkdownAsync(projectDir);
        
        // Assert
        await Verifier.Verify(result)
            .UseDirectory("Snapshots")
            .UseFileName("DocumentScanner_ComplexFileSystem");
    }
}
