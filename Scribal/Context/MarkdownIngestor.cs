using System.Net;
using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Embeddings;
using Microsoft.SemanticKernel.Memory;

#pragma warning disable SKEXP0001

namespace Scribal.Context;

public class MarkdownIngestor(
    IVectorStoreRecordCollection<Guid, TextSnippet<Guid>> store,
    ISemanticTextMemory memoryStore,
    ITextEmbeddingGenerationService embedder)
{
    public async Task Ingest(List<string> markdownFiles, CancellationToken cancellationToken = default)
    {
        string collectionName = "test";
        await memoryStore.SaveInformationAsync(collectionName,
            markdownFiles.Single(),
            Guid.NewGuid().ToString(),
            cancellationToken: cancellationToken);

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