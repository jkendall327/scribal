using Spectre.Console;

namespace Scribal.Cli.Interface;

public class SpectreUserInteraction : IUserInteraction
{
    public async Task<bool> ConfirmAsync(string prompt)
    {
        return await AnsiConsole.ConfirmAsync(prompt);
    }
}