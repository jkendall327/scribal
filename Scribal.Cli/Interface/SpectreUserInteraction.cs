using Spectre.Console;

namespace Scribal.Cli.Interface;

public class SpectreUserInteraction(IAnsiConsole console) : IUserInteraction
{
    public async Task<bool> ConfirmAsync(string prompt)
    {
        return await console.ConfirmAsync(prompt);
    }

    public Task NotifyAsync(string message, MessageOptions? options = null)
    {
        if (options == null)
        {
            console.MarkupLine(message);
        }
        else
        {
            var messageColour = options.Type switch
            {
                MessageType.Informational => null,
                MessageType.Hint => "yellow",
                MessageType.Warning => "yellow",
                MessageType.Error => "red",
                _ => throw new ArgumentOutOfRangeException()
            };

            var style = options.Style switch
            {
                MessageStyle.None => null,
                MessageStyle.Italics => "italic",
                MessageStyle.Bold => "bold",
                MessageStyle.Underline => "underline",
                _ => throw new ArgumentOutOfRangeException()
            };

            var formatted = message;
            
            if (messageColour != null)
            {
                formatted += $"[{messageColour}]{message}[/]";
            }

            if (style != null)
            {
                formatted += $"[{style}]{message}[/]";
            }
            
            console.MarkupLine(formatted);
        }
        
        return Task.CompletedTask;
    }
}