using System.IO.Abstractions;
using System.Net;
using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Embeddings;
using Microsoft.SemanticKernel.Memory;
using Microsoft.SemanticKernel.Text;
#pragma warning disable SKEXP0050

#pragma warning disable SKEXP0001

namespace Scribal.Context;

public class MarkdownIngestor(
    IVectorStoreRecordCollection<Guid, TextSnippet<Guid>> store,
    ISemanticTextMemory memoryStore,
    IFileSystem fileSystem,
    
    ITextEmbeddingGenerationService embedder)
{
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
            return Directory.EnumerateFiles(
                    rootDirectory, 
                    "*.*", 
                    SearchOption.AllDirectories)
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
        string collectionName = "test";

        var files = FindMarkdownFiles("/home/jackkendall/Documents/writing/eskar/plans/eiyren");

        var content = files.Take(1).Select(s => fileSystem.File.ReadAllLinesAsync(s, cancellationToken)).ToList();

        await Task.WhenAll(content);
        
        foreach (var task in content)
        {
            var chunk = TextChunker.SplitMarkdownParagraphs(task.Result, 1024);
        
            foreach (var se in chunk.Take(5))
            {
                await memoryStore.SaveInformationAsync(collectionName,
                    se,
                    Guid.NewGuid().ToString(),
                    cancellationToken: cancellationToken);
            }
        }

        return;

        await store.CreateCollectionIfNotExistsAsync(cancellationToken);

        var recordTasks = markdownFiles.Select(async content => new TextSnippet<Guid>
        {
            Key = Guid.NewGuid(),
            Text = content,
            ReferenceDescription = "whatever",
            ReferenceLink = "whatever",
            TextEmbedding = await GenerateEmbeddingsWithRetryAsync(embedder,
                    content,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false)
        });

        // Upsert the records into the vector store.
        var records = await Task.WhenAll(recordTasks).ConfigureAwait(false);

        _ = await store.UpsertAsync(records, cancellationToken: cancellationToken);
    }

    private static async Task<ReadOnlyMemory<float>> GenerateEmbeddingsWithRetryAsync(
        ITextEmbeddingGenerationService textEmbeddingGenerationService,
        string text,
        CancellationToken cancellationToken)
    {
        var tries = 0;

        while (true)
        {
            try
            {
                return await textEmbeddingGenerationService
                    .GenerateEmbeddingAsync(text, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (HttpOperationException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
            {
                tries++;

                if (tries < 3)
                {
                    Console.WriteLine($"Failed to generate embedding. Error: {ex}");
                    Console.WriteLine("Retrying embedding generation...");
                    await Task.Delay(10_000, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    throw;
                }
            }
        }
    }
}