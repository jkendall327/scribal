using Microsoft.SemanticKernel.ChatCompletion;

namespace Scribal.AI;

public interface IAiChatService
{
    /// <summary>
    /// Streams the assistant's response in a non-blocking manner.
    /// </summary>
    /// <param name="request">A request encapsulating the user's input, the conversation ID, and the service ID of the model to use.</param>
    /// <param name="history">Optionally, a prepared chat history for the assistant to use.
    /// If not provided, one will be created with a default system prompt for fiction-writing.</param>
    /// <param name="ct">A cancellation token allowing the operation to be aborted.</param>
    /// <returns>An asynchronous stream of discriminated unions, which either represent a chunk of
    /// the assistant's response or (ultimately) a record of the response's metadata, such as token usage.</returns>
    IAsyncEnumerable<ChatStreamItem> StreamAsync(ChatRequest request,
        ChatHistory? history = null,
        CancellationToken ct = default);

    /// <summary>
    /// Get the assistant's response in one go.
    /// </summary>
    /// <param name="request">A request encapsulating the user's input, the conversation ID, and the service ID of the model to use.</param>
    /// <param name="history">A prepared chat history for the assistant to use.</param>
    /// <param name="ct">A cancellation token allowing the operation to be aborted.</param>
    /// <returns>A tuple containing the final response, and associated metadata, such as token usage.</returns>
    Task<ChatMessage> GetFullResponseWithExplicitHistoryAsync(ChatRequest request,
        ChatHistory history,
        CancellationToken ct = default);
}