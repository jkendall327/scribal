using Spectre.Console;

namespace Scribal;

class Program
{
    // Dictionary of available commands
    private static readonly Dictionary<string, Func<string, Task<bool>>> Commands = new()
    {
        { "/quit", QuitCommand },
        { "/exit", QuitCommand },
        { "/help", HelpCommand },
        { "/new", NewProjectCommand },
        { "/open", OpenProjectCommand },
        { "/save", SaveCommand },
        { "/characters", ListCharactersCommand },
        { "/plot", ViewPlotCommand }
    };

    static async Task Main(string[] args)
    {
        await DisplayWelcome();
        await RunMainLoop();
    }

    private static async Task DisplayWelcome()
    {
        AnsiConsole.Clear();
            
        var figlet = new FigletText("Fiction Aider")
            .LeftJustified()
            .Color(Color.Green);
            
        AnsiConsole.Write(figlet);
        AnsiConsole.WriteLine();
            
        AnsiConsole.MarkupLine("[bold]Your AI fiction writing assistant[/]");
        AnsiConsole.MarkupLine("Type [blue]/help[/] for available commands or just start typing to talk to your assistant.");
        AnsiConsole.WriteLine();
    }

    private static async Task RunMainLoop()
    {
        var isRunning = true;
            
        while (isRunning)
        {
            // Display prompt
            AnsiConsole.Markup("[green]> [/]");
                
            // Get user input
            var input = Console.ReadLine() ?? string.Empty;
                
            // Process the input
            if (input.StartsWith("/"))
            {
                // Extract command and arguments
                var commandText = input.Split(' ')[0].ToLower();
                var arguments = input.Length > commandText.Length 
                    ? input[(commandText.Length + 1)..].Trim() 
                    : string.Empty;
                    
                // Execute command if it exists
                if (Commands.TryGetValue(commandText, out var command))
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

    private static async Task<bool> QuitCommand(string arguments)
    {
        if (AnsiConsole.Confirm("Are you sure you want to quit?"))
        {
            return false; // Return false to exit the main loop
        }
        return true;
    }
        
    private static async Task<bool> HelpCommand(string arguments)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Command")
            .AddColumn("Description");
            
        table.AddRow("/help", "Display this help message");
        table.AddRow("/quit or /exit", "Exit the application");
        table.AddRow("/new", "Create a new fiction project");
        table.AddRow("/open", "Open an existing project");
        table.AddRow("/save", "Save the current project");
        table.AddRow("/characters", "List and manage characters");
        table.AddRow("/plot", "View and modify plot structure");
            
        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[italic]Type anything without a leading / to talk directly to your AI assistant[/]");
            
        return true;
    }
        
    private static async Task<bool> NewProjectCommand(string arguments)
    {
        AnsiConsole.MarkupLine("[bold]Creating new project:[/] [green]{0}[/]", 
            string.IsNullOrEmpty(arguments) ? "Untitled Project" : arguments);
            
        // Project creation logic would go here
            
        return true;
    }
        
    private static async Task<bool> OpenProjectCommand(string arguments)
    {
        if (string.IsNullOrEmpty(arguments))
        {
            AnsiConsole.MarkupLine("[yellow]Please specify a project path.[/]");
            return true;
        }
            
        AnsiConsole.MarkupLine("[bold]Opening project:[/] [green]{0}[/]", arguments);
            
        // Project opening logic would go here
            
        return true;
    }
        
    private static async Task<bool> SaveCommand(string arguments)
    {
        var spinner = AnsiConsole.Status();
            
        await spinner.StartAsync("Saving project...", async ctx => 
        {
            // Simulate saving delay
            await Task.Delay(1000);
            ctx.Status("Finalizing...");
            await Task.Delay(500);
        });
            
        AnsiConsole.MarkupLine("[green]Project saved successfully![/]");
        return true;
    }
        
    private static async Task<bool> ListCharactersCommand(string arguments)
    {
        // This would pull from actual data in a real implementation
        var characters = new List<(string Name, string Role, string Description)>
        {
            ("Alice", "Protagonist", "A curious young woman with a vivid imagination"),
            ("Bob", "Supporting", "Alice's loyal friend and occasional voice of reason"),
            ("Zara", "Antagonist", "Mysterious figure with unclear motivations")
        };
            
        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Name")
            .AddColumn("Role")
            .AddColumn("Description");
            
        foreach (var character in characters)
        {
            table.AddRow(character.Name, character.Role, character.Description);
        }
            
        AnsiConsole.Write(table);
            
        return true;
    }
        
    private static async Task<bool> ViewPlotCommand(string arguments)
    {
        AnsiConsole.MarkupLine("[bold underline]Current Plot Structure[/]");
        AnsiConsole.WriteLine();
            
        // This would be dynamic in a real implementation
        var tree = new Tree("Story Arc")
            .Style(Style.Parse("green"));
            
        var act1 = tree.AddNode("[yellow]Act 1: Setup[/]");
        act1.AddNode("Introduction to main character");
        act1.AddNode("Inciting incident");
        act1.AddNode("First plot point");
            
        var act2 = tree.AddNode("[yellow]Act 2: Confrontation[/]");
        act2.AddNode("Rising action");
        act2.AddNode("Midpoint");
        act2.AddNode("Complications");
            
        var act3 = tree.AddNode("[yellow]Act 3: Resolution[/]");
        act3.AddNode("Climax");
        act3.AddNode("Falling action");
        act3.AddNode("Resolution");
            
        AnsiConsole.Write(tree);
            
        return true;
    }
        
    private static async Task ProcessAiConversation(string userInput)
    {
        // In a real implementation, this would call the AI service
        // For now, we'll just simulate a response
            
        AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .Start("Thinking...", ctx => 
            {
                // Simulate AI processing time
                Thread.Sleep(1500);
            });
            
        var panel = new Panel(GetDummyAiResponse(userInput))
            .Header("AI Assistant")
            .HeaderAlignment(Justify.Center)
            .Padding(1, 1, 1, 1)
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Blue);
            
        AnsiConsole.Write(panel);
    }
        
    private static string GetDummyAiResponse(string userInput)
    {
        // Just a simple response system for demonstration
        if (userInput.Contains("character", StringComparison.OrdinalIgnoreCase))
        {
            return "I can help you develop your character! Consider aspects like their background, motivations, flaws, and how they might grow through your story.";
        }

        if (userInput.Contains("plot", StringComparison.OrdinalIgnoreCase))
        {
            return "When developing your plot, remember to include a compelling inciting incident, rising action with appropriate complications, and a satisfying resolution that ties up the important threads.";
        }

        if (userInput.Contains("scene", StringComparison.OrdinalIgnoreCase))
        {
            return "For engaging scenes, balance action, dialogue, and description. Make sure each scene moves the story forward in some way - either through plot advancement or character development.";
        }

        return "I'm your fiction writing assistant. I can help with character development, plot structure, scene crafting, world-building, and more. What aspect of your story would you like to work on?";
    }
}