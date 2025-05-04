using System.IO.Abstractions;
using Microsoft.Extensions.AI;
using Scribal.AI;
using Spectre.Console;

namespace Scribal.Cli;

public class InterfaceManager(
    CommandService commands,
    IFileSystem fileSystem,
    IAiChatService aiChatService,
    PromptBuilder builder,
    RepoMapStore repoMapStore)
{
    private Guid _conversationId = Guid.NewGuid();

    public Task DisplayWelcome()
    {
        AnsiConsole.Clear();

        var figlet = new FigletText("Scribal").LeftJustified().Color(Color.Green);

        AnsiConsole.Write(figlet);
        AnsiConsole.WriteLine();

        var cwd = fileSystem.Directory.GetCurrentDirectory();

        AnsiConsole.MarkupLine($"[bold]Current working directory: {cwd}[/]");
        AnsiConsole.MarkupLine("Type [blue]/help[/] for available commands or just start typing to talk.");
        AnsiConsole.WriteLine();
        return Task.CompletedTask;
    }

    public async Task RunMainLoop()
    {
        var isRunning = true;

        while (isRunning)
        {
            // Display prompt
            AnsiConsole.Markup("[green]> [/]");

            // Get user input
            var input = Console.ReadLine() ?? string.Empty;

            // Process the input
            if (input.StartsWith('/'))
            {
                // Extract command and arguments
                var commandText = input.Split(' ')[0].ToLower();
                var arguments = input.Length > commandText.Length
                    ? input[(commandText.Length + 1)..].Trim()
                    : string.Empty;

                // Execute command if it exists
                if (commands.TryGetCommand(commandText, out var command))
                {
                    isRunning = await command(arguments);
                }
                else
                {
                    AnsiConsole.MarkupLine("[red]Unknown command. Type [blue]/help[/] for available commands.[/]");
                }
            }
            else if (!string.IsNullOrWhiteSpace(input))
            {
                // Handle as conversation with AI
                await ProcessConversation(input);
            }
        }

        AnsiConsole.MarkupLine("[yellow]Thank you for using Scribal. Goodbye![/]");
    }

    private async Task ProcessConversation(string userInput)
    {
        var rule = new Rule();

        AnsiConsole.Write(rule);

        var files = repoMapStore.Paths.ToList();

        foreach (var file in files)
        {
            AnsiConsole.MarkupLine($"[yellow]{file}[/]");
        }

        var enumerable = aiChatService.StreamAsync(_conversationId.ToString(),
            userInput,
            "gemini",
            CancellationToken.None);

        await foreach (var update in enumerable)
        {
            switch (update)
            {
                case ChatStreamItem.TokenChunk tc: AnsiConsole.Write(tc.Content); break;
                case ChatStreamItem.Metadata md:
                {
                    AnsiConsole.WriteLine();

                    AnsiConsole.Decoration = Decoration.Italic;

                    var time = FormatTimeSpan(md.Elapsed);
                    
                    AnsiConsole.Write($"{md.ServiceId}. Total time: {time}, {md.CompletionTokens} output tokens.");
                    
                    AnsiConsole.ResetDecoration();
                    break;
                }
            }
        }

        AnsiConsole.WriteLine();
        AnsiConsole.Write(rule);
    }

    private static string FormatTimeSpan(TimeSpan timeSpan)
    {
        return timeSpan.TotalSeconds < 60 ?
            // Less than a minute, just display seconds
            $"{timeSpan.Seconds}s" :
            // Display minutes and seconds
            $"{(int)timeSpan.TotalMinutes}m{timeSpan.Seconds}s";
    }
}