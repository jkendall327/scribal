using Microsoft.Extensions.AI;
using Spectre.Console;

namespace Scribal.Cli;

public class InterfaceManager(CommandService commands, IModelClient client)
{
    public Task DisplayWelcome()
    {
        AnsiConsole.Clear();

        var figlet = new FigletText("Fiction Aider").LeftJustified().Color(Color.Green);

        AnsiConsole.Write(figlet);
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine("[bold]Your AI fiction writing assistant[/]");
        AnsiConsole.MarkupLine(
            "Type [blue]/help[/] for available commands or just start typing to talk to your assistant.");
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
                await ProcessAiConversation(input);
            }
        }

        AnsiConsole.MarkupLine("[yellow]Thank you for using Fiction Aider. Goodbye![/]");
    }

    private async Task ProcessAiConversation(string userInput)
    {
        var rule = new Rule();
        
        AnsiConsole.Write(rule);
        
        var response = client.GetResponse(userInput);
                
        await foreach (var chunk in response)
        {
            AnsiConsole.Write(chunk.Text);
        }

        AnsiConsole.WriteLine();
        
        AnsiConsole.Write(rule);
    }
}