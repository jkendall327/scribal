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
    private const string PlotOutlineFileName = "plot_outline.json"; // From WorkspaceManager
    private const string ChaptersDirectoryName = "chapters";

    public ChapterManagerService(IFileSystem fileSystem,
        WorkspaceManager workspaceManager,
        IUserInteraction userInteraction,
        ILogger<ChapterManagerService> logger)
    {
        _fileSystem = fileSystem;
        _workspaceManager = workspaceManager;
        _userInteraction = userInteraction;
        _logger = logger;
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
            AnsiConsole.MarkupLine($"[grey]Executed dummy action for chapter {chapter.Number}: {
                Markup.Escape(chapter.Title)}.[/]");
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
            AnsiConsole.MarkupLine($"Managing Chapter {selectedChapter.Number}: [yellow]{
                Markup.Escape(selectedChapter.Title)}[/] ({selectedChapter.State})");
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
        var confirmPrompt = $"Are you sure you want to delete Chapter {chapterToDelete.Number}: '{
            Markup.Escape(chapterToDelete.Title)}'? This action cannot be undone.";
        if (!await _userInteraction.ConfirmAsync(confirmPrompt))
        {
            AnsiConsole.MarkupLine("[yellow]Chapter deletion cancelled.[/]");
            return;
        }

        _logger.LogInformation("Attempting to delete Chapter {ChapterNumber}: {ChapterTitle}",
            chapterToDelete.Number,
            chapterToDelete.Title);

        var workspaceDir = WorkspaceManager.TryFindWorkspaceFolder(_fileSystem, _logger);
        if (string.IsNullOrEmpty(workspaceDir))
        {
            AnsiConsole.MarkupLine("[red]Could not find workspace directory. Cannot delete chapter.[/]");
            _logger.LogError("Workspace directory not found, aborting chapter deletion.");
            return;
        }

        var projectRootDir = _fileSystem.DirectoryInfo.New(workspaceDir).Parent?.FullName;
        if (string.IsNullOrEmpty(projectRootDir))
        {
            AnsiConsole.MarkupLine("[red]Could not determine project root directory. Cannot delete chapter.[/]");
            _logger.LogError("Project root directory not found, aborting chapter deletion.");
            return;
        }

        var mainChaptersDirPath = _fileSystem.Path.Join(projectRootDir, ChaptersDirectoryName);
        var plotOutlineFilePath = _fileSystem.Path.Join(workspaceDir, PlotOutlineFileName);

        try
        {
            // 1. Delete chapter subfolder
            var chapterDirName = $"chapter_{chapterToDelete.Number:D2}";
            var chapterDirPath = _fileSystem.Path.Join(mainChaptersDirPath, chapterDirName);
            if (_fileSystem.Directory.Exists(chapterDirPath))
            {
                _fileSystem.Directory.Delete(chapterDirPath, recursive: true);
                _logger.LogInformation("Deleted chapter directory: {ChapterDirectoryPath}", chapterDirPath);
                AnsiConsole.MarkupLine($"[green]Deleted directory: {chapterDirPath}[/]");
            }
            else
            {
                _logger.LogWarning("Chapter directory not found, skipping deletion: {ChapterDirectoryPath}",
                    chapterDirPath);
                AnsiConsole.MarkupLine($"[yellow]Directory not found, skipped deletion: {chapterDirPath}[/]");
            }

            // 2. Update StoryOutline
            StoryOutline? storyOutline = null;
            if (_fileSystem.File.Exists(plotOutlineFilePath))
            {
                var outlineJson = await _fileSystem.File.ReadAllTextAsync(plotOutlineFilePath);
                storyOutline = JsonSerializer.Deserialize<StoryOutline>(outlineJson,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
            }

            if (storyOutline?.Chapters == null)
            {
                storyOutline = new StoryOutline(); // Should not happen if chapters exist, but good for safety
                _logger.LogWarning("Plot outline file was missing or empty. Initializing new one.");
            }

            var chapterToRemoveFromOutline =
                storyOutline.Chapters.FirstOrDefault(c => c.ChapterNumber == chapterToDelete.Number);
            List<(int OriginalNumber, Chapter ChapterRef)> originalChapterMap = [];

            if (chapterToRemoveFromOutline != null)
            {
                storyOutline.Chapters.Remove(chapterToRemoveFromOutline);
                _logger.LogInformation("Removed chapter {ChapterNumber} from StoryOutline object.",
                    chapterToDelete.Number);
            }

            // Re-number chapters in StoryOutline and store original numbers for folder renaming
            var newChapterNumber = 1;
            foreach (var ch in
                     storyOutline.Chapters.OrderBy(c => c.ChapterNumber)) // Order by old numbers before re-assigning
            {
                originalChapterMap.Add((ch.ChapterNumber, ch)); // Store old number before it's changed
                ch.ChapterNumber = newChapterNumber++;
            }

            var updatedOutlineJson = JsonSerializer.Serialize(storyOutline,
                new JsonSerializerOptions
                {
                    WriteIndented = true
                });
            await _fileSystem.File.WriteAllTextAsync(plotOutlineFilePath, updatedOutlineJson);
            _logger.LogInformation("Updated and saved plot_outline.json.");
            AnsiConsole.MarkupLine("[green]Plot outline updated.[/]");

            // 3. Update WorkspaceState
            var workspaceState = await _workspaceManager.LoadWorkspaceStateAsync(workspaceDir) ?? new WorkspaceState();
            var chapterToRemoveFromState =
                workspaceState.Chapters.FirstOrDefault(cs => cs.Number == chapterToDelete.Number);
            if (chapterToRemoveFromState != null)
            {
                workspaceState.Chapters.Remove(chapterToRemoveFromState);
                _logger.LogInformation("Removed chapter {ChapterNumber} from WorkspaceState object.",
                    chapterToDelete.Number);
            }

            newChapterNumber = 1;
            foreach (var cs in workspaceState.Chapters.OrderBy(c => c.Number)) // Order by old numbers
            {
                cs.Number = newChapterNumber++;
            }

            await _workspaceManager.SaveWorkspaceStateAsync(workspaceState, workspaceDir);
            _logger.LogInformation("Updated and saved workspace state.");
            AnsiConsole.MarkupLine("[green]Workspace state updated.[/]");

            // 4. Rename remaining chapter subfolders (in reverse order of their new numbers)
            // Use the originalChapterMap which contains chapters that *remained* and their *original* numbers
            // And their references now have *new* numbers.
            foreach (var (originalNum, chapterRef) in originalChapterMap.OrderByDescending(m =>
                         m.ChapterRef.ChapterNumber))
            {
                var currentChapterNewNumber = chapterRef.ChapterNumber; // This is the new, re-ordered number

                if (originalNum != currentChapterNewNumber)
                {
                    var oldDirName = $"chapter_{originalNum:D2}";
                    var newDirName = $"chapter_{currentChapterNewNumber:D2}";
                    var oldPath = _fileSystem.Path.Join(mainChaptersDirPath, oldDirName);
                    var newPath = _fileSystem.Path.Join(mainChaptersDirPath, newDirName);

                    if (_fileSystem.Directory.Exists(oldPath) && oldPath != newPath)
                    {
                        _logger.LogInformation("Renaming chapter directory from {OldPath} to {NewPath}",
                            oldPath,
                            newPath);
                        _fileSystem.Directory.Move(oldPath, newPath);
                        AnsiConsole.MarkupLine($"[green]Renamed directory: {Markup.Escape(oldDirName)} -> {
                            Markup.Escape(newDirName)}[/]");
                    }
                    else if (!_fileSystem.Directory.Exists(oldPath))
                    {
                        _logger.LogWarning("Expected old chapter directory {OldPath} not found for renaming.", oldPath);
                    }
                }
            }

            AnsiConsole.MarkupLine("[green]Chapter directories re-organized.[/]");

            AnsiConsole.MarkupLine($"[bold green]Chapter {chapterToDelete.Number}: '{
                Markup.Escape(chapterToDelete.Title)}' successfully deleted and workspace updated.[/]");
            subMenuCts.Cancel(); // Exit the current chapter's sub-menu
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete chapter {ChapterNumber}", chapterToDelete.Number);
            AnsiConsole.MarkupLine($"[red]An error occurred while deleting the chapter: {ex.Message}[/]");
            AnsiConsole.WriteException(ex);
        }
    }
}