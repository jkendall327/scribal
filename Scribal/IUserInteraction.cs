namespace Scribal;

public interface IUserInteraction
{
    Task<bool> ConfirmAsync(string prompt, CancellationToken cancellationToken = default);
    
    Task NotifyAsync(string message, MessageOptions? options = null);
    Task NotifyError(string message, Exception? exception = null);
    void DisplayProsePassage(string prose, string? header = null);
}