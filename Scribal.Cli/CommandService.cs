using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.IO.Abstractions;
using Spectre.Console;

namespace Scribal.Cli;

public class CommandService(IFileSystem fileSystem, RepoMapStore repoStore, IChatSessionStore conversationStore)
{
    public Parser Build()
    {
        var quit = new Command("/quit", "Exit Scribal");
        quit.AddAlias("exit");
        quit.SetHandler(QuitCommand);

        var model = new Command("/model", "Set model details and API key");
        model.SetHandler(SetModelCommand);

        var clear = new Command("/clear", "Clear conversation history");
        clear.SetHandler(ClearCommand);

        var tree = new Command("/tree", "Set files to be included in context");
        tree.SetHandler(TreeCommand);

        var root = new RootCommand("Scribal interactive shell")
        {
            model,
            clear,
            tree,
            quit,
        };

        return new CommandLineBuilder(root).UseDefaults().Build();
    }

    public List<string> GetCommandNames() => [];

    private Task ClearCommand(InvocationContext ctx)
    {
        _ = conversationStore.TryClearConversation(string.Empty);

        return Task.FromResult(true);
    }

    private Task TreeCommand(InvocationContext ctx)
    {
        var cwd = fileSystem.Directory.GetCurrentDirectory();

        var files = StickyTreeSelector.Scan(cwd);

        repoStore.Paths = files;

        return Task.FromResult(true);
    }

    private static Task QuitCommand(InvocationContext ctx)
    {
        if (AnsiConsole.Confirm("Are you sure you want to quit?"))
        {
            AnsiConsole.MarkupLine("[yellow]Thank you for using Scribal. Goodbye![/]");
            Environment.Exit(0);
            
            return Task.CompletedTask;
        }
        
        return Task.CompletedTask;
    }

    private static Task SetModelCommand(InvocationContext ctx)
    {
        var choices = ModelSelector.BeginConfiguration();

        AnsiConsole.WriteLine("Settings saved.");
        AnsiConsole.WriteLine(
            "Your API key will not be persisted; use environment variables or config to avoid setting it each time.");

        AnsiConsole.Write(new Rule());
        
        return Task.CompletedTask;
    }
}