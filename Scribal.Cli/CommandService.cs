using System.IO.Abstractions;
using Spectre.Console;

namespace Scribal.Cli;

public class CommandService
{
    private readonly Dictionary<string, Func<string, Task<bool>>> _commands = new()
    {
        {
            "/quit", QuitCommand
        },
        {
            "/exit", QuitCommand
        },
        {
            "/help", HelpCommand
        },
        {
            "/new", NewProjectCommand
        },
        {
            "/open", OpenProjectCommand
        },
        {
            "/save", SaveCommand
        },
        {
            "/characters", ListCharactersCommand
        },
        {
            "/plot", ViewPlotCommand
        }
    };

    private readonly IFileSystem _fileSystem;
    private readonly RepoMapStore _repoStore;
    private readonly IConversationStore _conversationStore;
    
    public CommandService(IFileSystem fileSystem, RepoMapStore repoStore, IConversationStore conversationStore)
    {
        _fileSystem = fileSystem;
        _repoStore = repoStore;
        _conversationStore = conversationStore;
        
        _commands.Add("/tree", TreeCommand);
        _commands.Add("/clear", ClearCommand);
    }

    private Task<bool> ClearCommand(string arg)
    {
        _conversationStore.ClearConversation();
        
        return Task.FromResult(true);
    }

    public bool TryGetCommand(string command, out Func<string, Task<bool>> func)
    {
        return _commands.TryGetValue(command, out func!);
    }

    private Task<bool> TreeCommand(string arg)
    {
        var cwd = _fileSystem.Directory.GetCurrentDirectory();
        
        var files = StickyTreeSelector.Scan(cwd);

        _repoStore.Paths = files;
        
        return Task.FromResult(true);
    }
    
    private static Task<bool> QuitCommand(string arguments)
    {
        if (AnsiConsole.Confirm("Are you sure you want to quit?"))
        {
            return Task.FromResult(false); // Return false to exit the main loop
        }

        return Task.FromResult(true);
    }

    private static Task<bool> HelpCommand(string arguments)
    {
        var table = new Table().Border(TableBorder.Rounded).AddColumn("Command").AddColumn("Description");

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

        return Task.FromResult(true);
    }

    private static Task<bool> NewProjectCommand(string arguments)
    {
        AnsiConsole.MarkupLine("[bold]Creating new project:[/] [green]{0}[/]",
            string.IsNullOrEmpty(arguments) ? "Untitled Project" : arguments);

        // Project creation logic would go here

        return Task.FromResult(true);
    }

    private static Task<bool> OpenProjectCommand(string arguments)
    {
        if (string.IsNullOrEmpty(arguments))
        {
            AnsiConsole.MarkupLine("[yellow]Please specify a project path.[/]");
            return Task.FromResult(true);
        }

        AnsiConsole.MarkupLine("[bold]Opening project:[/] [green]{0}[/]", arguments);

        // Project opening logic would go here

        return Task.FromResult(true);
    }

    private static async Task<bool> SaveCommand(string arguments)
    {
        var spinner = AnsiConsole.Status();

        await spinner.StartAsync("Saving project...",
            async ctx =>
            {
                // Simulate saving delay
                await Task.Delay(1000);
                ctx.Status("Finalizing...");
                await Task.Delay(500);
            });

        AnsiConsole.MarkupLine("[green]Project saved successfully![/]");
        return true;
    }

    private static Task<bool> ListCharactersCommand(string arguments)
    {
        // This would pull from actual data in a real implementation
        var characters = new List<(string Name, string Role, string Description)>
        {
            ("Alice", "Protagonist", "A curious young woman with a vivid imagination"),
            ("Bob", "Supporting", "Alice's loyal friend and occasional voice of reason"),
            ("Zara", "Antagonist", "Mysterious figure with unclear motivations")
        };

        var table = new Table().Border(TableBorder.Rounded)
            .AddColumn("Name")
            .AddColumn("Role")
            .AddColumn("Description");

        foreach (var character in characters)
        {
            table.AddRow(character.Name, character.Role, character.Description);
        }

        AnsiConsole.Write(table);

        return Task.FromResult(true);
    }

    private static Task<bool> ViewPlotCommand(string arguments)
    {
        AnsiConsole.MarkupLine("[bold underline]Current Plot Structure[/]");
        AnsiConsole.WriteLine();

        // This would be dynamic in a real implementation
        var tree = new Tree("Story Arc").Style(Style.Parse("green"));

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

        return Task.FromResult(true);
    }
}