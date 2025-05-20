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
            console.MarkupLine("[bold red]An error occurred:[/]");

            var sb = new StringBuilder();
            var currentException = ex;
            var exceptionCount = 0;

            while (currentException != null)
            {
                if (exceptionCount > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("[grey]---> Inner Exception:[/]");
                }

                var fullName = currentException.GetType().FullName;

                if (fullName == null)
                {
                    // This case was throwing an exception before, 
                    // but we are already in an error handler.
                    // Fallback to a generic message.
                    fullName = "Unknown Exception Type";
                }

                sb.Append($"[bold aqua]{Markup.Escape(fullName)}[/]: ");
                sb.AppendLine($"[white]{Markup.Escape(currentException.Message)}[/]");

                if (!string.IsNullOrWhiteSpace(currentException.StackTrace))
                {
                    sb.AppendLine("[dim]Stack Trace:[/]");
                    var enumerable = currentException.StackTrace.Split([
                            Environment.NewLine
                        ],
                        StringSplitOptions.None);

                    foreach (var line in enumerable)
                    {
                        sb.AppendLine($"  [grey]{Markup.Escape(line.Trim())}[/]");
                    }
                }

                currentException = currentException.InnerException;
                exceptionCount++;
            }
            
            var panel = new Panel(sb.ToString())
                .Header("[yellow]Exception Details[/]")
                .Border(BoxBorder.Rounded)
                .BorderColor(Color.Red);

            console.Write(panel);
        }
    }

    public async Task<string> DisplayAssistantResponseAsync(IAsyncEnumerable<ChatModels> stream, CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        
        await consoleChatRenderer.StreamWithSpinnerAsync(stream.CollectWhileStreaming(sb, ct), ct);

        return sb.ToString();
    }

    public Task DisplayProsePassageAsync(string prose, string? header = null)
    {
        console.DisplayProsePassage(prose, string.Empty);
        return Task.CompletedTask;
    }

    public Task<T> AskAsync<T>(string prompt, T defaultValue)
    {
        return Task.FromResult(console.Ask<T>(prompt, defaultValue));
    }

    public Task<T> PromptAsync<T>(SelectionPrompt<T> prompt) where T : notnull
    {
        return Task.FromResult(console.Prompt(prompt));
    }

    public Task ClearAsync()
    {
        console.Clear();
        return Task.CompletedTask;
    }

    public Task WriteTableAsync(Table table)
    {
        console.Write(table);
        return Task.CompletedTask;
    }

    public Task WriteFigletAsync(FigletText figlet)
    {
        console.Write(figlet);
        return Task.CompletedTask;
    }

    public Task WriteRuleAsync(Rule rule)
    {
        console.Write(rule);
        return Task.CompletedTask;
    }

    public async Task StatusAsync(Func<StatusContext, Task> action)
    {
        await console.Status().StartAsync("", action);
    }

    public Task<string> GetMultilineInputAsync(string prompt)
    {
        console.MarkupLine(prompt);
        var sb = new StringBuilder();
        string? line;
        while ((line = ReadLine.Read()) != null)
        {
            sb.AppendLine(line);
        }
        return Task.FromResult(sb.ToString());
    }
}