using Spectre.Console;

namespace Scribal.Cli;

public class InterfaceManager(CommandService commands)
{
    public async Task DisplayWelcome()
    {
        AnsiConsole.Clear();

        var figlet = new FigletText("Fiction Aider").LeftJustified().Color(Color.Green);

        AnsiConsole.Write(figlet);
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine("[bold]Your AI fiction writing assistant[/]");
        AnsiConsole.MarkupLine(
            "Type [blue]/help[/] for available commands or just start typing to talk to your assistant.");
        AnsiConsole.WriteLine();
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

    static async Task ProcessAiConversation(string userInput)
    {
        // In a real implementation, this would call the AI service
        // For now, we'll just simulate a response

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync("Thinking...",
                async ctx =>
                {
                    // Simulate AI processing time
                    await Task.Delay(1500);
                });

        var panel = new Panel(GetDummyAiResponse(userInput)).Header("AI Assistant")
            .HeaderAlignment(Justify.Center)
            .Padding(1, 1, 1, 1)
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Blue);

        AnsiConsole.Write(panel);
    }

    static string GetDummyAiResponse(string userInput)
    {
        // Just a simple response system for demonstration
        if (userInput.Contains("character", StringComparison.OrdinalIgnoreCase))
        {
            return
                "I can help you develop your character! Consider aspects like their background, motivations, flaws, and how they might grow through your story.";
        }

        if (userInput.Contains("plot", StringComparison.OrdinalIgnoreCase))
        {
            return
                "When developing your plot, remember to include a compelling inciting incident, rising action with appropriate complications, and a satisfying resolution that ties up the important threads.";
        }

        if (userInput.Contains("scene", StringComparison.OrdinalIgnoreCase))
        {
            return
                "For engaging scenes, balance action, dialogue, and description. Make sure each scene moves the story forward in some way - either through plot advancement or character development.";
        }

        return
            "I'm your fiction writing assistant. I can help with character development, plot structure, scene crafting, world-building, and more. What aspect of your story would you like to work on?";
    }
}