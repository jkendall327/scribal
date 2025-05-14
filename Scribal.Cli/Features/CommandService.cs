using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.IO.Abstractions;
using Scribal.AI;
using Scribal.Context;
using Scribal.Workspace;
using Spectre.Console;

namespace Scribal.Cli;

public class CommandService(
    IFileSystem fileSystem,
    RepoMapStore repoStore,
    IChatSessionStore conversationStore,
    PitchService pitchService,
    OutlineService outlineService, // Added OutlineService
    WorkspaceManager workspaceManager)
{
    private readonly Argument<string> _ideaArgument = new()
    {
        Name = "Idea",
        Arity = ArgumentArity.ExactlyOne,
        Description = "Your basic idea for the story",
    };

    private readonly Argument<string> _premiseArgument = new() // Added premise argument
    {
        Name = "Premise",
        Arity = ArgumentArity.ExactlyOne,
        Description = "The story premise to be turned into an outline",
    };

    public Parser Build()
    {
        Command Create(string name, string description, Func<InvocationContext, Task> action, string[]? aliases = null)
        {
            var cmd = new Command(name, description);
            
            foreach (var alias in aliases ?? [])
            {
                cmd.AddAlias(alias);
            }

            cmd.SetHandler(action);

            return cmd;
        }

        var quit = Create("/quit", "Exit Scribal", QuitCommand, ["/exit"]);
        var clear = Create("/clear", "Clear conversation history", ClearCommand);
        var tree = Create("/tree", "Set files to be included in context", TreeCommand);
        var init = Create("/init", "Creates a new Scribal workspace in the current folder", InitCommand);
        
        var pitch = Create("/pitch", "Turns an initial story idea into a fleshed-out premise", PitchCommand);
        pitch.AddArgument(_ideaArgument);

        var outline = Create("/outline", "Generates a plot outline from a premise", OutlineCommand); // Added outline command
        outline.AddArgument(_premiseArgument);

        var chaptersCmd = Create("/chapters", "Manage chapters in the workspace", ChaptersCommandAsync); // Added chapters command
        
        var root = new RootCommand("Scribal interactive shell")
        {
            init,
            clear,
            pitch,
            outline, // Added outline command to root
            chaptersCmd, // Added chapters command to root
            tree,
            quit,
        };

        return new CommandLineBuilder(root).UseDefaults().UseHelp("/help").Build();
    }

    private async Task OutlineCommand(InvocationContext arg) // Added OutlineCommand handler
    {
        var premise = arg.ParseResult.GetValueForArgument(_premiseArgument);
        var token = arg.GetCancellationToken();

        if (string.IsNullOrWhiteSpace(premise))
        {
            AnsiConsole.MarkupLine("[red]Premise cannot be empty.[/]");
            return;
        }
        
        try
        {
            await outlineService.CreateOutlineFromPremise(premise, token);
        }
        catch (Exception e)
        {
            AnsiConsole.WriteException(e);
        }
    }

    private async Task ChaptersCommandAsync(InvocationContext arg)
    {
        var token = arg.GetCancellationToken();

        if (!workspaceManager.InWorkspace)
        {
            var foundWorkspace = WorkspaceManager.TryFindWorkspaceFolder(fileSystem);
            if (foundWorkspace == null)
            {
                AnsiConsole.MarkupLine("[red]You are not currently in a Scribal workspace. Use '/init' to create one.[/]");
                return;
            }
            // If a workspace is found but not loaded (e.g. app just started),
            // LoadWorkspaceStateAsync will handle loading it.
        }

        var state = await workspaceManager.LoadWorkspaceStateAsync();

        if (state == null)
        {
            AnsiConsole.MarkupLine("[red]Could not load workspace state.[/]");
            return;
        }

        if (state.Chapters == null || !state.Chapters.Any())
        {
            AnsiConsole.MarkupLine("[yellow]No chapters found in the workspace. Generate an outline first using '/outline'.[/]");
            return;
        }

        while (!token.IsCancellationRequested)
        {
            AnsiConsole.WriteLine();
            var chapterChoices = state.Chapters
                .OrderBy(c => c.Number)
                .Select(c => $"{c.Number}. {Markup.Escape(c.Title)} ({c.State})")
                .ToList();
            
            var selectionPrompt = new SelectionPrompt<string>()
                .Title("Select a chapter to manage or [blue]Back[/] to return:")
                .PageSize(10)
                .AddChoices(chapterChoices.Append("Back"));

            var choice = AnsiConsole.Prompt(selectionPrompt);

            if (choice == "Back" || token.IsCancellationRequested)
            {
                break;
            }

            var selectedChapterState = state.Chapters.FirstOrDefault(c => $"{c.Number}. {Markup.Escape(c.Title)} ({c.State})" == choice);
            if (selectedChapterState != null)
            {
                await ChapterSubMenuAsync(selectedChapterState, token);
            }
        }
    }

    private Parser BuildChapterSubMenuParser(ChapterState chapter, CancellationTokenSource linkedCts)
    {
        var dummyCmd = new Command("/dummy", "A dummy action for the chapter.");
        dummyCmd.SetHandler(async () => {
            AnsiConsole.MarkupLine($"[grey]Executed dummy action for chapter {chapter.Number}: {Markup.Escape(chapter.Title)}.[/]");
            // In a real scenario, this would call a service method.
            try
            {
                await Task.Delay(100, linkedCts.Token); // Simulate work respecting cancellation
            }
            catch (OperationCanceledException)
            {
                AnsiConsole.MarkupLine("[yellow]Dummy action cancelled.[/]");
            }
        });

        var backCmd = new Command("/back", "Return to chapter selection.");
        backCmd.SetHandler(() => {
            AnsiConsole.MarkupLine("[blue]Returning to chapter selection...[/]");
            linkedCts.Cancel(); // Signal the sub-menu loop to terminate
        });
        
        var chapterRootCommand = new RootCommand($"Actions for Chapter {chapter.Number}: {Markup.Escape(chapter.Title)}")
        {
            dummyCmd,
            backCmd
            // Future actions: "/details", "/edit-summary", "/draft", "/mark-done", etc.
        };
        // Remove default execution if RootCommand directly executes something.
        // chapterRootCommand.Handler = null; // Ensure root command itself doesn't have a handler unless intended

        return new CommandLineBuilder(chapterRootCommand)
            .UseDefaults() // Includes help, version, etc.
            .UseHelp("/help") // Customize help command name
            .Build();
    }

    private async Task ChapterSubMenuAsync(ChapterState selectedChapter, CancellationToken parentToken)
    {
        using var chapterSubMenuCts = CancellationTokenSource.CreateLinkedTokenSource(parentToken);
        var chapterSubMenuParser = BuildChapterSubMenuParser(selectedChapter, chapterSubMenuCts);

        while (!chapterSubMenuCts.IsCancellationRequested && !parentToken.IsCancellationRequested)
        {
            AnsiConsole.WriteLine(); // For spacing
            AnsiConsole.MarkupLine($"Managing Chapter {selectedChapter.Number}: [yellow]{Markup.Escape(selectedChapter.Title)}[/] ({selectedChapter.State})");
            AnsiConsole.MarkupLine("Enter a command for this chapter ([blue]/help[/] for options, [blue]/back[/] to return to chapter list):");
            AnsiConsole.Markup($"Chapter {selectedChapter.Number} > ");
            
            var input = ReadLine.Read(); // Assuming ReadLine.Read() handles basic input reading

            if (parentToken.IsCancellationRequested) // Check parent token before processing
            {
                AnsiConsole.MarkupLine("[yellow]Exiting chapter menu due to external cancellation.[/]");
                break;
            }

            if (string.IsNullOrWhiteSpace(input))
            {
                continue;
            }

            try
            {
                // InvokeAsync will use the CancellationToken from chapterSubMenuCts
                await chapterSubMenuParser.InvokeAsync(input);
            }
            catch (OperationCanceledException) when (chapterSubMenuCts.IsCancellationRequested)
            {
                // This can happen if /back was called, or if a command itself was cancelled by chapterSubMenuCts.
                // If it was /back, the loop condition will handle exit.
                // If it was a command cancelled by this CTS, it's already handled by the command or loop.
                AnsiConsole.MarkupLine("[yellow](Chapter action cancelled or /back invoked)[/]");
            }
            catch (Exception ex) // Catch parsing errors or command execution errors
            {
                AnsiConsole.MarkupLine($"[red]Error processing chapter command: {ex.Message}[/]");
                // AnsiConsole.WriteException(ex); // Optionally print full exception for debugging
            }
        }
    }

    private async Task PitchCommand(InvocationContext arg)
    {
        var idea = arg.ParseResult.GetValueForArgument(_ideaArgument);
        var token = arg.GetCancellationToken();

        if (string.IsNullOrWhiteSpace(idea))
        {
            AnsiConsole.MarkupLine("[red]Idea cannot be empty.[/]");
            return;
        }
        
        try
        {
            await pitchService.CreatePremiseFromPitch(idea, token);
        }
        catch (Exception e)
        {
            AnsiConsole.WriteException(e);
        }
    }

    private async Task InitCommand(InvocationContext arg)
    {
        try
        {
            (var created, var initialised) = await workspaceManager.InitialiseWorkspace();

            var message = created ? "Workspace initialised." : "You are already in a Scribal workspace.";

            AnsiConsole.WriteLine(message);

            if (initialised)
            {
                AnsiConsole.WriteLine("Git repo initialised.");
            }
        }
        catch (Exception e)
        {
            AnsiConsole.WriteException(e);
        }
    }

    private Task ClearCommand(InvocationContext ctx)
    {
        _ = conversationStore.TryClearConversation(string.Empty);
        AnsiConsole.MarkupLine("[yellow]Conversation history cleared.[/]");
        return Task.FromResult(true);
    }

    private Task TreeCommand(InvocationContext ctx)
    {
        var cwd = fileSystem.Directory.GetCurrentDirectory();
        // Assuming StickyTreeSelector is available and working as intended.
        var files = StickyTreeSelector.Scan(cwd); 
        repoStore.Paths = files;
        AnsiConsole.MarkupLine($"[green]Context files updated. {files.Count} files/directories selected.[/]");
        return Task.FromResult(true);
    }

    private static Task QuitCommand(InvocationContext ctx)
    {
        if (AnsiConsole.Confirm("Are you sure you want to quit?"))
        {
            AnsiConsole.MarkupLine("[yellow]Thank you for using Scribal. Goodbye![/]");
            Environment.Exit(0);
        }
        return Task.CompletedTask;
    }
}
