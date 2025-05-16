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
            {
                "/project/README.md", new("# Project Documentation\nThis is the main project documentation.")
            },
            {
                "/project/CONTRIBUTING.md", new("# How to Contribute\nGuidelines for contributors.")
            },

            // Characters directory with markdown files
            {
                "/project/characters/protagonist.md", new("# Main Character\nDetails about the protagonist.")
            },
            {
                "/project/characters/antagonist.md", new("# Villain\nDetails about the antagonist.")
            },
            {
                "/project/characters/.hidden-character.md",
                new("# Secret Character\nThis character is not revealed yet.")
            },

            // Chapters directory with nested structure
            {
                "/project/chapters/chapter1.md", new("# Chapter 1\nThe beginning of the story.")
            },
            {
                "/project/chapters/chapter2.md", new("# Chapter 2\nThe plot thickens.")
            },
            {
                "/project/chapters/drafts/chapter3-draft.md", new("# Chapter 3 (Draft)\nStill working on this.")
            },
            {
                "/project/chapters/drafts/chapter4-draft.md", new("# Chapter 4 (Draft)\nNeeds revision.")
            },

            // Empty directory
            {
                "/project/empty/", new MockDirectoryData()
            },

            // Hidden directory with markdown
            {
                "/project/.notes/important.md", new("# Important Notes\nDon't forget these details.")
            },
            {
                "/project/.notes/timeline.md", new("# Timeline\nChronological order of events.")
            },

            // Directory with mixed content
            {
                "/project/resources/images/cover.jpg", new([0x01, 0x02, 0x03, 0x04])
            },
            {
                "/project/resources/images/character-sketches.png", new([0x05, 0x06, 0x07, 0x08])
            },
            {
                "/project/resources/references.md", new("# References\nSources and inspirations.")
            },
            {
                "/project/resources/outline.md", new("# Story Outline\nThe overall structure.")
            },

            // Directory with non-markdown files only
            {
                "/project/assets/style.css", new("body { font-family: Arial; }")
            },
            {
                "/project/assets/script.js", new("console.log('Hello world');")
            }
        });

        var sut = new DocumentScanService(filesystem);

        var projectDir = filesystem.DirectoryInfo.New("/project");

        // Act
        var result = await sut.ScanDirectoryForMarkdownAsync(projectDir);

        // Assert
        await Verify(result).UseDirectory("Snapshots").UseFileName("DocumentScanner_ComplexFileSystem");
    }
}