using Microsoft.SemanticKernel.ChatCompletion;

// Required for ChatHistory

namespace Scribal.AI;

public interface IAiChatService
{
    IAsyncEnumerable<ChatStreamItem> StreamAsync(string conversationId,
        string userMessage,
        string sid,
        CancellationToken ct = default);

    IAsyncEnumerable<ChatStreamItem> StreamWithExplicitHistoryAsync(string conversationId,
        ChatHistory history,
        string userMessage,
        string sid,
        CancellationToken ct = default);
}