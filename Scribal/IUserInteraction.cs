namespace Scribal;

public interface IUserInteraction
{
    Task<bool> ConfirmAsync(string prompt);
    
    Task NotifyAsync(string message, MessageOptions? options = null);
}