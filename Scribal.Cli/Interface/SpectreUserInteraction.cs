using System.Text;
using Scribal.AI;
using Spectre.Console;

namespace Scribal.Cli.Interface;

public class SpectreUserInteraction(IAnsiConsole console, ConsoleChatRenderer consoleChatRenderer) : IUserInteraction
{
    public async Task<bool> ConfirmAsync(string prompt, CancellationToken cancellationToken = default)
    {
        return await console.ConfirmAsync(prompt, cancellationToken: cancellationToken);
    }

    public Task<string> GetUserInputAsync(CancellationToken cancellationToken = default)
    {
        var line = ReadLine.Read();
        return Task.FromResult(line);
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
                MessageType.None => null,
                MessageType.Informational => "cyan",
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

    public async Task NotifyError(string message, Exception? ex = null)
    {
        await NotifyAsync(message, new(MessageType.Error));

        if (ex != null)
        {
            ExceptionDisplay.DisplayException(ex);
        }
    }

    public async Task<string> DisplayAssistantResponseAsync(IAsyncEnumerable<ChatModels> stream, CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        
        await consoleChatRenderer.StreamWithSpinnerAsync(stream.CollectWhileStreaming(sb, ct), ct);

        return sb.ToString();
    }

    public void DisplayProsePassage(string prose, string? header = null)
    {
        console.DisplayProsePassage(prose, string.Empty);
    }
}