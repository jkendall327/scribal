using System.Runtime.CompilerServices;
using System.Text;

namespace Scribal.AI;

/// <summary>
///     Discriminated union between a chunk of the model's streamed output and a metadata record produced when it's done.
/// </summary>
public record ChatModels
{
    private ChatModels()
    {
    }

    public sealed record TokenChunk(string Content) : ChatModels;

    public sealed record Metadata(TimeSpan Elapsed, int PromptTokens, int CompletionTokens) : ChatModels;
}

public record ChatMessage(string Message, ChatModels.Metadata Metadata);

public record ChatRequest(string UserMessage, string ConversationId, string ServiceId);

public static class ChatModelExtensions
{
    /// <summary>
    /// Collects the output of an asynchronous chat stream while continuing to yield its output. 
    /// </summary>
    /// <param name="stream">The chat to stream asynchronously.</param>
    /// <param name="collector">The StringBuilder which will be given the complete output of the chat message.</param>
    /// <param name="ct">A token allowing asynchronous cancellation.</param>
    /// <returns>An asynchronous iteration of the same chat stream.</returns>
    public static async IAsyncEnumerable<ChatModels> CollectWhileStreaming(
        this IAsyncEnumerable<ChatModels> stream,
        StringBuilder collector,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var item in stream.WithCancellation(ct))
        {
            if (item is ChatModels.TokenChunk tc)
            {
                collector.Append(tc.Content);
            }

            yield return item;
        }
    }
}