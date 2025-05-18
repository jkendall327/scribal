using System.IO.Abstractions;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Scribal.Agency;
using Scribal.AI;
using Scribal.Cli.Infrastructure;
using Scribal.Cli.Interface;
using Scribal.Config;
using Scribal.Context;
using Scribal.Workspace;
using Spectre.Console;

namespace Scribal.Cli.Features;

public class ChapterDrafterService(
    IAiChatService chat,
    PromptRenderer renderer,
    Kernel kernel,
    IAnsiConsole console,
    IOptions<AiSettings> options,
    IFileSystem fileSystem,
    WorkspaceManager workspaceManager,
    IGitService gitService,
    RefinementService refinementService,
    ILogger<ChapterDrafterService> logger,
    ConsoleChatRenderer consoleChatRenderer)
{
    public async Task DraftChapterAsync(ChapterState chapter, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Starting draft for Chapter {ChapterNumber}: {ChapterTitle}",
            chapter.Number,
            chapter.Title);

        var workspacePath = workspaceManager.CurrentWorkspacePath;

        if (string.IsNullOrWhiteSpace(workspacePath))
        {
            console.MarkupLine("[red]Could not determine current workspace path. Cannot draft chapter.[/]");

            logger.LogWarning(
                "Current workspace path is not set in WorkspaceManager. Chapter {ChapterNumber} drafting aborted",
                chapter.Number);

            return;
        }

        if (options.Value.Primary is null)
        {
            console.MarkupLine("[red]No primary model is configured. Cannot draft chapter.[/]");
            logger.LogWarning("Primary model not configured, cannot draft chapter");

            return;
        }

        var sid = options.Value.Primary.Provider;

        var initialDraft = await GenerateInitialDraft(chapter, sid, cancellationToken);

        if (string.IsNullOrWhiteSpace(initialDraft))
        {
            console.MarkupLine("[red]Failed to generate an initial draft.[/]");
            logger.LogWarning("Initial draft generation failed for chapter {ChapterNumber}", chapter.Number);

            return;
        }

        console.MarkupLine("[cyan]Initial Draft Generated:[/]");
        console.WriteLine(Markup.Escape(initialDraft));

        var ok = await console.ConfirmAsync("Do you want to refine this draft?", cancellationToken: cancellationToken);

        string finalDraft;

        if (!ok)
        {
            console.MarkupLine("[yellow]Chapter drafting complete (initial draft accepted).[/]");
            finalDraft = initialDraft;
        }
        else
        {
            var systemPrompt =
                "You are an assistant helping to refine a chapter draft. Focus on improving it based on user feedback. Be concise and helpful.";

            finalDraft = await refinementService.RefineAsync(initialDraft, systemPrompt, sid, cancellationToken);
        }

        console.MarkupLine("[yellow]Final chapter draft:[/]");
        console.WriteLine(Markup.Escape(finalDraft));

        await CommitDraftAsync(chapter, finalDraft, cancellationToken);
    }

    private async Task<string> GenerateInitialDraft(ChapterState chapter, string sid, CancellationToken ct)
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
        var prompt = await renderer.RenderPromptTemplateFromFileAsync(kernel, request, cancellationToken: ct);

        var cid = $"draft-init-{chapter.Number}-{Guid.NewGuid()}";
        var history = new ChatHistory();
        history.AddSystemMessage(prompt);

        console.MarkupLine(
            $"[yellow]Generating initial draft for Chapter {chapter.Number}: {Markup.Escape(chapter.Title)}...[/]");

        var initialUserMessage = "Generate the chapter draft based on the details provided in your instructions.";

        var chatRequest = new ChatRequest(initialUserMessage, cid, sid);

        var draftStream = chat.StreamAsync(chatRequest, history, ct);
        var draftBuilder = new StringBuilder();
        await consoleChatRenderer.StreamWithSpinnerAsync(draftStream.CollectWhileStreaming(draftBuilder, ct), ct);

        var generatedDraft = draftBuilder.ToString().Trim();

        logger.LogInformation("Initial draft generated for Chapter {ChapterNumber}, length {Length}",
            chapter.Number,
            generatedDraft.Length);

        return generatedDraft;
    }

    private async Task CommitDraftAsync(ChapterState chapter, string content, CancellationToken cancellationToken)
    {
        var workspacePath = workspaceManager.CurrentWorkspacePath;

        if (string.IsNullOrWhiteSpace(workspacePath))
        {
            console.MarkupLine("[red]Could not determine workspace path from WorkspaceManager. Draft not saved.[/]");

            logger.LogError(
                "Cannot save draft, workspace path unknown (from WorkspaceManager.CurrentWorkspacePath) for chapter {ChapterNumber}",
                chapter.Number);

            return;
        }

        // The project root is the parent of the .scribal workspace directory
        var projectRootPath = fileSystem.DirectoryInfo.New(workspacePath).Parent?.FullName;

        if (string.IsNullOrWhiteSpace(projectRootPath))
        {
            console.MarkupLine("[red]Could not determine project root path. Draft not saved.[/]");

            logger.LogError(
                "Cannot save draft, project root path could not be determined from workspace path {WorkspacePath} for chapter {ChapterNumber}",
                workspacePath,
                chapter.Number);

            return;
        }

        var chaptersDir = fileSystem.Path.Combine(projectRootPath, "chapters");
        fileSystem.Directory.CreateDirectory(chaptersDir); // Ensure directory exists

        // Sanitize title for filename
        var sanitizedTitle = string.Join("_", chapter.Title.Split(fileSystem.Path.GetInvalidFileNameChars()));
        sanitizedTitle = string.Join("_", sanitizedTitle.Split(fileSystem.Path.GetInvalidPathChars()));

        if (string.IsNullOrWhiteSpace(sanitizedTitle))
        {
            sanitizedTitle = "untitled";
        }

        // Limit length of sanitized title to avoid overly long filenames
        const int maxTitleLengthInFilename = 50;

        if (sanitizedTitle.Length > maxTitleLengthInFilename)
        {
            sanitizedTitle = sanitizedTitle[..maxTitleLengthInFilename];
        }

        var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var draftFileName = $"chapter_{chapter.Number:D2}_{sanitizedTitle}_draft_{timestamp}.md";
        var draftFilePath = fileSystem.Path.Combine(chaptersDir, draftFileName);

        try
        {
            await fileSystem.File.WriteAllTextAsync(draftFilePath, content, cancellationToken);
            console.MarkupLine($"[green]Draft saved successfully to: {Markup.Escape(draftFilePath)}[/]");
            logger.LogInformation("Chapter {ChapterNumber} draft saved to {FilePath}", chapter.Number, draftFilePath);

            if (gitService.Enabled)
            {
                var commitMessage = $"Drafted Chapter {chapter.Number}: {chapter.Title}";

                var commitSuccess = await gitService.CreateCommitAsync(draftFilePath, commitMessage, cancellationToken);

                if (commitSuccess)
                {
                    console.MarkupLine($"[green]Committed draft to git: {Markup.Escape(commitMessage)}[/]");
                    logger.LogInformation("Successfully committed draft for chapter {ChapterNumber}", chapter.Number);
                }
                else
                {
                    console.MarkupLine($"[red]Failed to commit draft for chapter {chapter.Number} to git.[/]");
                    logger.LogWarning("Failed to commit draft for chapter {ChapterNumber}", chapter.Number);
                }
            }
            else
            {
                logger.LogInformation("Git service not enabled. Skipping commit for chapter {ChapterNumber} draft",
                    chapter.Number);
            }

            // Optionally, update ChapterState with the new draft path and save workspace state
            // chapter.DraftFilePath = draftFilePath; // Assuming ChapterState has such a property
            // chapter.State = ChapterStatus.Drafted; // Or similar
            // await _workspaceManager.SaveWorkspaceStateAsync(state, cancellationToken: cancellationToken);
            // _console.MarkupLine("[green]Workspace state updated with new draft information.[/]");
        }
        catch (Exception ex)
        {
            console.MarkupLine($"[red]Failed to save draft: {Markup.Escape(ex.Message)}[/]");

            logger.LogError(ex,
                "Failed to save draft for chapter {ChapterNumber} to {FilePath}",
                chapter.Number,
                draftFilePath);
        }
    }
}