using System.IO.Abstractions;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Scribal.AI;
using Scribal.Context;
using Scribal.Workspace;
using Spectre.Console;

namespace Scribal.Cli.Features;

public class ChapterDrafterService
{
    private readonly IAiChatService _chat;
    private readonly PromptRenderer _renderer;
    private readonly Kernel _kernel;
    private readonly IAnsiConsole _console;
    private readonly IOptions<AiSettings> _options;
    private readonly IFileSystem _fileSystem;
    private readonly WorkspaceManager _workspaceManager;
    private readonly ILogger<ChapterDrafterService> _logger;

    public ChapterDrafterService(IAiChatService chat,
        PromptRenderer renderer,
        Kernel kernel,
        IAnsiConsole console,
        IOptions<AiSettings> options,
        IFileSystem fileSystem,
        WorkspaceManager workspaceManager,
        ILogger<ChapterDrafterService> logger)
    {
        _chat = chat;
        _renderer = renderer;
        _kernel = kernel;
        _console = console;
        _options = options;
        _fileSystem = fileSystem;
        _workspaceManager = workspaceManager;
        _logger = logger;
    }

    public async Task DraftChapterAsync(ChapterState chapter, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting draft for Chapter {ChapterNumber}: {ChapterTitle}",
            chapter.Number,
            chapter.Title);

        var workspacePath = _workspaceManager.CurrentWorkspacePath;

        if (string.IsNullOrWhiteSpace(workspacePath))
        {
            _console.MarkupLine("[red]Could not determine current workspace path. Cannot draft chapter.[/]");

            _logger.LogWarning(
                "Current workspace path is not set in WorkspaceManager. Chapter {ChapterNumber} drafting aborted.",
                chapter.Number);

            return;
        }

        if (_options.Value.Primary is null)
        {
            _console.MarkupLine("[red]No primary model is configured. Cannot draft chapter.[/]");
            _logger.LogWarning("Primary model not configured, cannot draft chapter.");

            return;
        }

        var sid = _options.Value.Primary.Provider;

        var initialDraft = await GenerateInitialDraft(chapter, cancellationToken, sid);

        if (string.IsNullOrWhiteSpace(initialDraft))
        {
            _console.MarkupLine("[red]Failed to generate an initial draft.[/]");
            _logger.LogWarning("Initial draft generation failed for chapter {ChapterNumber}", chapter.Number);

            return;
        }

        _console.MarkupLine("[cyan]Initial Draft Generated:[/]");
        _console.WriteLine(Markup.Escape(initialDraft)); // Display initial draft

        var ok = await _console.ConfirmAsync("Do you want to refine this draft?", cancellationToken: cancellationToken);

        string finalDraft;

        if (!ok)
        {
            _console.MarkupLine("[yellow]Chapter drafting complete (initial draft accepted).[/]");
            finalDraft = initialDraft;
        }
        else
        {
            var refinementCid = $"draft-refine-{chapter.Number}-{Guid.NewGuid()}";
            var refinementHistory = new ChatHistory();

            var sb = new StringBuilder("You are an assistant helping to refine a chapter draft. The current draft is:");
            sb.AppendLine("---");
            sb.AppendLine(initialDraft);
            sb.AppendLine("---");
            sb.AppendLine("Focus on improving it based on user feedback. Be concise and helpful.");

            refinementHistory.AddSystemMessage(sb.ToString());
            refinementHistory.AddAssistantMessage(initialDraft);

            _console.MarkupLine(
                "Entering chapter draft refinement chat. Type [blue]/done[/] when finished or [blue]/cancel[/] to abort.");

            finalDraft = await RefineDraft(cancellationToken, refinementCid, refinementHistory, sid, initialDraft);
            _console.MarkupLine("[yellow]Chapter draft refinement finished.[/]");
        }

        _console.MarkupLine("[yellow]Final chapter draft:[/]");
        _console.WriteLine(Markup.Escape(finalDraft));

        await SaveDraftToFileAsync(chapter, finalDraft, cancellationToken);
    }

    private async Task<string> GenerateInitialDraft(ChapterState chapter, CancellationToken ct, string sid)
    {
        var arguments = new KernelArguments
        {
            {
                "chapter_number", chapter.Number.ToString()
            },
            {
                "chapter_title", chapter.Title
            },
            {
                "chapter_summary", chapter.Summary ?? string.Empty
            }

            // TODO: Add more context like beats, characters if available in ChapterState and useful for the prompt
            // { "chapter_beats", JsonSerializer.Serialize(chapter.Beats) }, 
            // { "key_characters", JsonSerializer.Serialize(chapter.KeyCharacters) }
        };

        var request = new RenderRequest("DraftChapter",
            "DraftChapter",
            "Prompt for drafting a chapter based on its outline details.",
            arguments);

        // Corrected: Removed 'ct' (CancellationToken) as it's not an argument for this method.
        var prompt = await _renderer.RenderPromptTemplateFromFileAsync(_kernel, request, cancellationToken: ct);

        var cid = $"draft-init-{chapter.Number}-{Guid.NewGuid()}";
        var history = new ChatHistory();
        history.AddSystemMessage(prompt);

        _console.MarkupLine(
            $"[yellow]Generating initial draft for Chapter {chapter.Number}: {Markup.Escape(chapter.Title)}...[/]");

        var initialUserMessage = "Generate the chapter draft based on the details provided in your instructions.";
        var draftStream = _chat.StreamWithExplicitHistoryAsync(cid, history, initialUserMessage, sid, ct);
        var draftBuilder = new StringBuilder();

        await ConsoleChatRenderer.StreamWithSpinnerAsync(CollectWhileStreaming(draftStream, draftBuilder, ct), ct);

        var generatedDraft = draftBuilder.ToString().Trim();

        _logger.LogInformation("Initial draft generated for Chapter {ChapterNumber}, length {Length}",
            chapter.Number,
            generatedDraft.Length);

        return generatedDraft;
    }

    private async Task<string> RefineDraft(CancellationToken ct,
        string refinementCid,
        ChatHistory refinementHistory,
        string sid,
        string currentDraft)
    {
        var lastAssistantResponse = currentDraft;

        while (true)
        {
            if (ct.IsCancellationRequested)
            {
                _console.MarkupLine("[yellow]Refinement cancelled by host.[/]");
                _logger.LogInformation("Draft refinement cancelled by host for {RefinementCid}", refinementCid);

                break;
            }

            _console.WriteLine();
            _console.Markup("[green]Refine Chapter Draft > [/]");
            var userInput = ReadLine.Read(); // Assuming ReadLine is accessible or replace with _console.Ask/Prompt

            if (string.IsNullOrWhiteSpace(userInput))
            {
                continue;
            }

            if (userInput.Equals("/done", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("User finished draft refinement for {RefinementCid}", refinementCid);

                break;
            }

            if (userInput.Equals("/cancel", StringComparison.OrdinalIgnoreCase))
            {
                _console.MarkupLine("[yellow]Cancelling refinement...[/]");
                _logger.LogInformation("User cancelled draft refinement for {RefinementCid}", refinementCid);

                return lastAssistantResponse;
            }

            _console.WriteLine();
            var responseBuilder = new StringBuilder();

            try
            {
                refinementHistory.AddUserMessage(userInput);

                var refinementStream = _chat.StreamWithExplicitHistoryAsync(refinementCid,
                    refinementHistory,
                    userInput,
                    sid,
                    ct);

                await ConsoleChatRenderer.StreamWithSpinnerAsync(
                    CollectWhileStreaming(refinementStream, responseBuilder, ct),
                    ct);

                lastAssistantResponse = responseBuilder.ToString().Trim();

                if (!string.IsNullOrWhiteSpace(lastAssistantResponse))
                {
                    refinementHistory.AddAssistantMessage(lastAssistantResponse);

                    _logger.LogDebug("Refinement successful for {RefinementCid}, response length {Length}",
                        refinementCid,
                        lastAssistantResponse.Length);
                }
            }
            catch (OperationCanceledException)
            {
                _console.WriteLine();
                _console.MarkupLine("[yellow](Refinement stream cancelled)[/]");
                _logger.LogInformation("Refinement stream cancelled for {RefinementCid}", refinementCid);

                break;
            }
            catch (Exception e)
            {
                _console.WriteException(e);
                _console.MarkupLine("[red]An error occurred during refinement.[/]");
                _logger.LogError(e, "Error during draft refinement for {RefinementCid}", refinementCid);
            }
        }

        return lastAssistantResponse;
    }

    private async IAsyncEnumerable<ChatStreamItem> CollectWhileStreaming(IAsyncEnumerable<ChatStreamItem> stream,
        StringBuilder collector,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var item in stream.WithCancellation(ct))
        {
            if (item is ChatStreamItem.TokenChunk tc)
            {
                collector.Append(tc.Content);
            }

            yield return item;
        }
    }

    private async Task SaveDraftToFileAsync(ChapterState chapter, string content, CancellationToken cancellationToken)
    {
        var workspacePath = _workspaceManager.CurrentWorkspacePath;

        if (string.IsNullOrWhiteSpace(workspacePath))
        {
            _console.MarkupLine("[red]Could not determine workspace path from WorkspaceManager. Draft not saved.[/]");

            _logger.LogError(
                "Cannot save draft, workspace path unknown (from WorkspaceManager.CurrentWorkspacePath) for chapter {ChapterNumber}",
                chapter.Number);

            return;
        }

        // The project root is the parent of the .scribal workspace directory
        var projectRootPath = _fileSystem.DirectoryInfo.New(workspacePath).Parent?.FullName;

        if (string.IsNullOrWhiteSpace(projectRootPath))
        {
            _console.MarkupLine("[red]Could not determine project root path. Draft not saved.[/]");

            _logger.LogError(
                "Cannot save draft, project root path could not be determined from workspace path {WorkspacePath} for chapter {ChapterNumber}",
                workspacePath,
                chapter.Number);

            return;
        }

        var chaptersDir = _fileSystem.Path.Combine(projectRootPath, "chapters");
        _fileSystem.Directory.CreateDirectory(chaptersDir); // Ensure directory exists

        // Sanitize title for filename
        var sanitizedTitle = string.Join("_", chapter.Title.Split(_fileSystem.Path.GetInvalidFileNameChars()));
        sanitizedTitle = string.Join("_", sanitizedTitle.Split(_fileSystem.Path.GetInvalidPathChars()));

        if (string.IsNullOrWhiteSpace(sanitizedTitle))
        {
            sanitizedTitle = "untitled";
        }

        // Limit length of sanitized title to avoid overly long filenames
        const int maxTitleLengthInFilename = 50;

        if (sanitizedTitle.Length > maxTitleLengthInFilename)
        {
            sanitizedTitle = sanitizedTitle.Substring(0, maxTitleLengthInFilename);
        }

        var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var draftFileName = $"chapter_{chapter.Number:D2}_{sanitizedTitle}_draft_{timestamp}.md";
        var draftFilePath = _fileSystem.Path.Combine(chaptersDir, draftFileName);

        try
        {
            await _fileSystem.File.WriteAllTextAsync(draftFilePath, content, cancellationToken);
            _console.MarkupLine($"[green]Draft saved successfully to: {Markup.Escape(draftFilePath)}[/]");
            _logger.LogInformation("Chapter {ChapterNumber} draft saved to {FilePath}", chapter.Number, draftFilePath);

            // Optionally, update ChapterState with the new draft path and save workspace state
            // chapter.DraftFilePath = draftFilePath; // Assuming ChapterState has such a property
            // chapter.State = ChapterStatus.Drafted; // Or similar
            // await _workspaceManager.SaveWorkspaceStateAsync(state, cancellationToken: cancellationToken);
            // _console.MarkupLine("[green]Workspace state updated with new draft information.[/]");
        }
        catch (Exception ex)
        {
            _console.MarkupLine($"[red]Failed to save draft: {Markup.Escape(ex.Message)}[/]");

            _logger.LogError(ex,
                "Failed to save draft for chapter {ChapterNumber} to {FilePath}",
                chapter.Number,
                draftFilePath);
        }
    }
}