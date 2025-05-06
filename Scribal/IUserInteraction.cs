namespace Scribal;

public interface IUserInteraction
{
    Task<bool> ConfirmAsync(string prompt);
}