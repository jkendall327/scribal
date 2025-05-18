using Scribal.AI;

namespace Scribal;

public interface IUserInteraction
{
    Task<bool> ConfirmAsync(string prompt, CancellationToken cancellationToken = default);
    
    Task NotifyAsync(string message, MessageOptions? options = null);
    Task NotifyError(string message, Exception? exception = null);
    Task<string> DisplayAssistantResponseAsync(IAsyncEnumerable<ChatModels> stream, CancellationToken ct = default);
    void DisplayProsePassage(string prose, string? header = null);
}