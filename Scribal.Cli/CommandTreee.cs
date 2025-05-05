using System.CommandLine.Builder;
using System.CommandLine.Parsing;

namespace Scribal.Cli;

// CommandTree.cs
using System.CommandLine;

internal static class CommandTree
{
    public static Parser Build()
    {
        // ------ /quit  -------------------------------------------------
        var quitCmd = new Command("quit", "Exit Scribal");
        quitCmd.AddAlias("exit");   // "exit" was already in your dictionary
        quitCmd.SetHandler(() => Environment.Exit(0));   // just flip the isRunning flag

        // ------ /new <name?> ------------------------------------------
        var nameArg = new Argument<string>(
            "name",     () => "UntitledProject",
            "Project name (optional, defaults to UntitledProject)");

        var newCmd = new Command("new", "Create a new fiction project") { nameArg };
        //newCmd.SetHandler(async (string n) => await projects.CreateAsync(n), nameArg);

        // ------ assemble the tree -------------------------------------
        var root = new RootCommand("Scribal interactive shell")
        {
            quitCmd,
            newCmd,
            /* add the rest of the verbs here in the same style */
        };

        return new CommandLineBuilder(root)
            .UseDefaults()                 // --help, --version, error rendering
            .CancelOnProcessTermination()  // honours Ctrl+C, Ctrl+Break
            .Build();
    }
}
