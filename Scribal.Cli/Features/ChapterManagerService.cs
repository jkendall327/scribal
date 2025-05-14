using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.IO.Abstractions;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Scribal.Workspace;
using Spectre.Console;

namespace Scribal.Cli.Features;

public class ChapterManagerService
{
    private readonly IFileSystem _fileSystem;
    private readonly WorkspaceManager _workspaceManager;
    private readonly IUserInteraction _userInteraction;
    private readonly ILogger<ChapterManagerService> _logger;
    private readonly IChapterDeletionService _chapterDeletionService; // Added

    public ChapterManagerService(IFileSystem fileSystem,
        WorkspaceManager workspaceManager,
        IUserInteraction userInteraction,
        ILogger<ChapterManagerService> logger,
        IChapterDeletionService chapterDeletionService) // Added
    {
        _fileSystem = fileSystem;
        _workspaceManager = workspaceManager;
        _userInteraction = userInteraction;
        _logger = logger;
        _chapterDeletionService = chapterDeletionService; // Added
    }

    public async Task ManageChaptersAsync(InvocationContext context)
    {
        var token = context.GetCancellationToken();

        if (!_workspaceManager.InWorkspace)
        {
            var foundWorkspace = WorkspaceManager.TryFindWorkspaceFolder(_fileSystem);
            if (foundWorkspace == null)
            {
                AnsiConsole.MarkupLine(
                    "[red]You are not currently in a Scribal workspace. Use '/init' to create one.[/]");
                return;
            }
            // If a workspace is found but not loaded, LoadWorkspaceStateAsync will handle it.
        }

        var state = await _workspaceManager.LoadWorkspaceStateAsync();

        if (state == null)
        {
            AnsiConsole.MarkupLine("[red]Could not load workspace state.[/]");
            return;
        }

        if (state.Chapters == null || !state.Chapters.Any())
        {
            AnsiConsole.MarkupLine(
                "[yellow]No chapters found in the workspace. Generate an outline first using '/outline'.[/]");
            return;
        }

        while (!token.IsCancellationRequested)
        {
            AnsiConsole.WriteLine();
            var chapterChoices = state.Chapters.OrderBy(c => c.Number)
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

            var selectedChapterState =
                state.Chapters.FirstOrDefault(c => $"{c.Number}. {Markup.Escape(c.Title)} ({c.State})" == choice);
            if (selectedChapterState != null)
            {
                await ChapterSubMenuAsync(selectedChapterState, token);
            }
        }
    }

    private Parser BuildChapterSubMenuParser(ChapterState chapter, CancellationTokenSource linkedCts)
    {
        var dummyCmd = new Command("/dummy", "A dummy action for the chapter.");
        dummyCmd.SetHandler(async () =>
        {
            AnsiConsole.MarkupLine($"[grey]Executed dummy action for chapter {chapter.Number}: {Markup.Escape(chapter.Title)}.[/]");
            try
            {
                await Task.Delay(100, linkedCts.Token);
            }
            catch (OperationCanceledException)
            {
                AnsiConsole.MarkupLine("[yellow]Dummy action cancelled.[/]");
            }
        });

        var backCmd = new Command("/back", "Return to chapter selection.");
        backCmd.SetHandler(() =>
        {
            AnsiConsole.MarkupLine("[blue]Returning to chapter selection...[/]");
            linkedCts.Cancel();
        });

        var deleteCmd = new Command("/delete", "Delete this chapter.");
        deleteCmd.SetHandler(async () => { await DeleteChapterAsync(chapter, linkedCts); });

        var chapterRootCommand =
            new RootCommand($"Actions for Chapter {chapter.Number}: {Markup.Escape(chapter.Title)}")
            {
                dummyCmd,
                deleteCmd,
                backCmd
            };

        return new CommandLineBuilder(chapterRootCommand).UseDefaults().UseHelp("/help").Build();
    }

    private async Task ChapterSubMenuAsync(ChapterState selectedChapter, CancellationToken parentToken)
    {
        using var chapterSubMenuCts = CancellationTokenSource.CreateLinkedTokenSource(parentToken);
        var chapterSubMenuParser = BuildChapterSubMenuParser(selectedChapter, chapterSubMenuCts);

        while (!chapterSubMenuCts.IsCancellationRequested && !parentToken.IsCancellationRequested)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"Managing Chapter {selectedChapter.Number}: [yellow]{Markup.Escape(selectedChapter.Title)}[/] ({selectedChapter.State})");
            AnsiConsole.MarkupLine(
                "Enter a command for this chapter ([blue]/help[/] for options, [blue]/back[/] to return to chapter list):");
            AnsiConsole.Markup($"Chapter {selectedChapter.Number} > ");

            var input = ReadLine.Read();

            if (parentToken.IsCancellationRequested)
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
                await chapterSubMenuParser.InvokeAsync(input);
            }
            catch (OperationCanceledException) when (chapterSubMenuCts.IsCancellationRequested)
            {
                AnsiConsole.MarkupLine("[yellow](Chapter action cancelled or /back invoked)[/]");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error processing chapter command: {ex.Message}[/]");
            }
        }
    }

    private async Task DeleteChapterAsync(ChapterState chapterToDelete, CancellationTokenSource subMenuCts)
    {
        var confirmPrompt = $"Are you sure you want to delete Chapter {chapterToDelete.Number}: '{Markup.Escape(chapterToDelete.Title)}'? This action cannot be undone.";
        if (!await _userInteraction.ConfirmAsync(confirmPrompt))
        {
            AnsiConsole.MarkupLine("[yellow]Chapter deletion cancelled.[/]");
            return;
        }

        var deletionResult = await _chapterDeletionService.DeleteChapterAsync(chapterToDelete, subMenuCts.Token);

        foreach (var error in deletionResult.Errors)
        {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(error)}[/]");
        }
        foreach (var warning in deletionResult.Warnings)
        {
            AnsiConsole.MarkupLine($"[yellow]{Markup.Escape(warning)}[/]");
        }
        foreach (var action in deletionResult.ActionsTaken)
        {
            AnsiConsole.MarkupLine($"[green]{Markup.Escape(action)}[/]");
        }

        if (deletionResult.Success)
        {
            AnsiConsole.MarkupLine($"[bold green]{Markup.Escape(deletionResult.OverallMessage ?? "Chapter deleted successfully.")}[/]");
            subMenuCts.Cancel(); // Exit the current chapter's sub-menu
        }
        else
        {
            AnsiConsole.MarkupLine($"[bold red]{Markup.Escape(deletionResult.OverallMessage ?? "Chapter deletion failed.")}[/]");
            if (deletionResult.Exception != null && !(deletionResult.Exception is OperationCanceledException))
            {
                AnsiConsole.WriteException(deletionResult.Exception);
            }
        }
    }
}
