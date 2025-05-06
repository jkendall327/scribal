using System.IO.Abstractions;
using Microsoft.SemanticKernel.Memory;
using Microsoft.SemanticKernel.Text;

#pragma warning disable SKEXP0050
#pragma warning disable SKEXP0001

namespace Scribal.Context;

public class MarkdownIngestor(ISemanticTextMemory memoryStore, IFileSystem fileSystem)
{
    public const string CollectionName = "markdown";
    
    public static IEnumerable<string> FindMarkdownFiles(string rootDirectory)
    {
        try
        {
            // Validate directory exists
            if (!Directory.Exists(rootDirectory))
            {
                throw new DirectoryNotFoundException($"Directory not found: {rootDirectory}");
            }

            // Search for all files with .md or .markdown extensions
            return Directory.EnumerateFiles(rootDirectory, "*.*", SearchOption.AllDirectories)
                .Where(f => !f.StartsWith("."))
                .Where(file => file.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ||
                               file.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error while searching for markdown files: {ex.Message}");
            return Enumerable.Empty<string>();
        }
    }

    public async Task Ingest(List<string> markdownFiles, CancellationToken cancellationToken = default)
    {
        var files = FindMarkdownFiles("/home/jackkendall/Documents/writing/eskar/plans/eiyren");

        var content = files.Take(1).Select(s => fileSystem.File.ReadAllLinesAsync(s, cancellationToken)).ToList();

        await Task.WhenAll(content);

        foreach (var task in content)
        {
            var chunk = TextChunker.SplitMarkdownParagraphs(task.Result, 1024);

            foreach (var se in chunk.Take(5))
            {
                await memoryStore.SaveInformationAsync(CollectionName,
                    se,
                    Guid.NewGuid().ToString(),
                    cancellationToken: cancellationToken);
            }
        }
    }
}