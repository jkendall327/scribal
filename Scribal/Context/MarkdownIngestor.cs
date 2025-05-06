using System.IO.Abstractions;
using Microsoft.SemanticKernel.Memory;
using Microsoft.SemanticKernel.Text;

#pragma warning disable SKEXP0050
#pragma warning disable SKEXP0001

namespace Scribal.Context;

public class MarkdownIngestor(
    //ISemanticTextMemory memoryStore, 
    IFileSystem fileSystem)
{
    public const string CollectionName = "markdown";

    public async Task IngestAllMarkdown(IDirectoryInfo root,
        SearchOption searchOption,
        CancellationToken cancellationToken = default)
    {
        var files = FindMarkdownFiles(root.FullName, searchOption);

        var content = files.Select(s => fileSystem.File.ReadAllLinesAsync(s, cancellationToken)).ToList();

        await Task.WhenAll(content);

        foreach (var task in content)
        {
            var chunk = TextChunker.SplitMarkdownParagraphs(task.Result, 1024);

            foreach (var se in chunk)
            {
                // await memoryStore.SaveInformationAsync(CollectionName,
                //     se,
                //     Guid.NewGuid().ToString(),
                //     cancellationToken: cancellationToken);
            }
        }
    }

    private IEnumerable<string> FindMarkdownFiles(string rootDirectory, SearchOption searchOption)
    {
        // Validate directory exists
        if (!fileSystem.Directory.Exists(rootDirectory))
        {
            throw new DirectoryNotFoundException($"Directory not found: {rootDirectory}");
        }

        // Search for all files with .md or .markdown extensions
        return fileSystem.Directory
            .EnumerateFiles(rootDirectory, "*.md", searchOption)
            .Where(f => !f.StartsWith('.'));
    }
}