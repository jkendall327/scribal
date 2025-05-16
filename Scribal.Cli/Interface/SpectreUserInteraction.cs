using Spectre.Console;

namespace Scribal.Cli;

public class SpectreUserInteraction : IUserInteraction
{
    public async Task<bool> ConfirmAsync(string prompt)
    {
        return await AnsiConsole.ConfirmAsync(prompt);
    }
}