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

    private async Task ChapterSubMenuAsync(ChapterState selectedChapter, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"Managing Chapter {selectedChapter.Number}: [yellow]{Markup.Escape(selectedChapter.Title)}[/] ({selectedChapter.State})");

            var subMenuPrompt = new SelectionPrompt<string>()
                .Title("Choose an action:")
                .PageSize(5)
                .AddChoices("Dummy Action (Does Nothing)", "Back to Chapter Selection")
                // Future actions: "View Details", "Edit Summary", "Draft Content", "Mark as Done", etc.
               ;

            var subChoice = AnsiConsole.Prompt(subMenuPrompt);

            if (subChoice == "Back to Chapter Selection" || token.IsCancellationRequested)
            {
                break;
            }

            switch (subChoice)
            {
                case "Dummy Action (Does Nothing)":
                    AnsiConsole.MarkupLine($"[grey]Executed dummy action for chapter {selectedChapter.Number}.[/]");
                    // In a real scenario, this would call a service method.
                    await Task.Delay(100, token); // Simulate work
                    break;
                // Add cases for other actions here
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
