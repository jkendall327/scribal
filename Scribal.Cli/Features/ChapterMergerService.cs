// AI: New file for Chapter Merger Service Implementation

using Microsoft.Extensions.Logging;
using Scribal.Workspace;
using Spectre.Console;

namespace Scribal.Cli.Features;

public class ChapterMergerService : IChapterMergerService
{
    private readonly WorkspaceManager _workspaceManager;
    private readonly IAnsiConsole _console;
    private readonly IUserInteraction _userInteraction;
    private readonly ILogger<ChapterMergerService> _logger;

    private enum SummaryChoice
    {
        KeepTarget,
        UseSource,
        EnterNew
    }

    public ChapterMergerService(WorkspaceManager workspaceManager,
        IAnsiConsole console,
        IUserInteraction userInteraction,
        ILogger<ChapterMergerService> logger)
    {
        _workspaceManager = workspaceManager;
        _console = console;
        _userInteraction = userInteraction;
        _logger = logger;
    }

    public async Task<bool> MergeChapterAsync(ChapterState sourceChapter, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Initiating merge for Chapter {SourceChapterNumber}: {SourceChapterTitle}",
            sourceChapter.Number,
            sourceChapter.Title);

        _console.MarkupLine($"Merging Chapter {sourceChapter.Number}: {Markup.Escape(sourceChapter.Title)}");

        var state = await _workspaceManager.LoadWorkspaceStateAsync(cancellationToken: cancellationToken);

        if (state is null || !state.Chapters.Any())
        {
            _console.MarkupLine("[red]Could not load workspace state or no chapters available to merge into.[/]");
            _logger.LogWarning("Failed to load workspace state or no chapters found for merging");

            return false;
        }

        var targetableChapters = state.Chapters
                                      .Where(c => c.Number != sourceChapter.Number)
                                      .OrderBy(c => c.Number)
                                      .ToList();

        if (!targetableChapters.Any())
        {
            _console.MarkupLine("[yellow]No other chapters available to merge into.[/]");

            _logger.LogInformation("No target chapters available for merging with source chapter {SourceChapterNumber}",
                sourceChapter.Number);

            return false;
        }

        var targetChapterChoices = targetableChapters
                                   .Select(c => $"{c.Number}. {Markup.Escape(c.Title)} ([grey]{c.State}[/])")
                                   .ToList();

        var targetSelectionPrompt = new SelectionPrompt<string>()
                                    .Title("Select the [green]target chapter[/] (the chapter to merge into):")
                                    .PageSize(10)
                                    .AddChoices(targetChapterChoices);

        var targetChoiceString = _console.Prompt(targetSelectionPrompt);

        if (cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        var selectedTargetChapterState = targetableChapters.FirstOrDefault(c =>
            $"{c.Number}. {Markup.Escape(c.Title)} ([grey]{c.State}[/])" == targetChoiceString);

        if (selectedTargetChapterState is null)
        {
            _console.MarkupLine("[red]Invalid target chapter selection. Aborting merge.[/]");
            _logger.LogWarning("Invalid target chapter selected for merge");

            return false;
        }

        _logger.LogInformation(
            "Source Chapter: {SourceChapterNumber} ('{SourceChapterTitle}'). Target Chapter: {TargetChapterNumber} ('{TargetChapterTitle}')",
            sourceChapter.Number,
            sourceChapter.Title,
            selectedTargetChapterState.Number,
            selectedTargetChapterState.Title);

        _console.MarkupLine(
            $"Merging [yellow]'{Markup.Escape(sourceChapter.Title)}'[/] into [green]'{Markup.Escape(selectedTargetChapterState.Title)}'[/].");

        var summaryPrompt = new SelectionPrompt<SummaryChoice>()
                            .Title("How should the target chapter's summary be updated?")
                            .AddChoices(SummaryChoice.KeepTarget, SummaryChoice.UseSource, SummaryChoice.EnterNew)
                            .UseConverter(choice => choice switch
                            {
                                SummaryChoice.KeepTarget =>
                                    $"Keep target's current summary ('{Markup.Escape(selectedTargetChapterState.Summary ?? "empty")}')",
                                SummaryChoice.UseSource =>
                                    $"Use source's summary ('{Markup.Escape(sourceChapter.Summary ?? "empty")}')",
                                SummaryChoice.EnterNew => "Enter a new summary for the target chapter",
                                var _ => choice.ToString()
                            });

        var summaryDecision = _console.Prompt(summaryPrompt);

        if (cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        string newTargetSummary;

        switch (summaryDecision)
        {
            case SummaryChoice.KeepTarget:
                newTargetSummary = selectedTargetChapterState.Summary ?? string.Empty;

                break;
            case SummaryChoice.UseSource:
                newTargetSummary = sourceChapter.Summary ?? string.Empty;

                break;
            case SummaryChoice.EnterNew:
                newTargetSummary = _console
                                   .Ask<string>(
                                       $"Enter the new summary for the merged chapter '{Markup.Escape(selectedTargetChapterState.Title)}':")
                                   .Trim();

                if (cancellationToken.IsCancellationRequested)
                {
                    return false;
                }

                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        var confirmMessage =
            $"Confirm merge: Chapter {sourceChapter.Number} ('{Markup.Escape(sourceChapter.Title)}') " + Environment.NewLine +
            $"will be merged into Chapter {selectedTargetChapterState.Number} ('{Markup.Escape(selectedTargetChapterState.Title)}'). " + Environment.NewLine +
            $"The source chapter will be deleted. The target chapter's summary will be '{Markup.Escape(newTargetSummary)}'." + Environment.NewLine + 
            "Proceed?";

        var confirmed = await _userInteraction.ConfirmAsync(confirmMessage);

        if (!confirmed || cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation(
                "User cancelled chapter merge operation between source {SourceChapterNumber} and target {TargetChapterNumber}",
                sourceChapter.Number,
                selectedTargetChapterState.Number);

            _console.MarkupLine("[yellow]Chapter merge cancelled.[/]");

            return false;
        }

        _logger.LogInformation(
            "User confirmed merge. Source: C{SourceChapterNumber}, Target: C{TargetChapterNumber}, New Target Summary: '{NewTargetSummary}'",
            sourceChapter.Number,
            selectedTargetChapterState.Number,
            newTargetSummary);

        var success = await _workspaceManager.MergeChaptersAsync(sourceChapter.Number,
            selectedTargetChapterState.Number,
            newTargetSummary,
            cancellationToken);

        if (success)
        {
            _logger.LogInformation(
                "Successfully merged chapter {SourceChapterNumber} into {TargetChapterNumber} via WorkspaceManager",
                sourceChapter.Number,
                selectedTargetChapterState.Number);

            _console.MarkupLine(
                $"[bold green]Successfully merged '{Markup.Escape(sourceChapter.Title)}' into '{Markup.Escape(selectedTargetChapterState.Title)}'.[/]");
        }
        else
        {
            _logger.LogError(
                "WorkspaceManager failed to merge chapter {SourceChapterNumber} into {TargetChapterNumber}",
                sourceChapter.Number,
                selectedTargetChapterState.Number);

            _console.MarkupLine("[bold red]Chapter merge operation failed. Check logs for details.[/]");
        }

        return success;
    }
}