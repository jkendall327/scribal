using System.IO.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Scribal.Agency;
using Scribal.AI;
using Scribal.Cli.Infrastructure;
using Scribal.Config;
using Scribal.Context;

namespace Scribal.Workspace;

public class ChapterDrafterService(
    IAiChatService chat,
    PromptRenderer renderer,
    Kernel kernel,
    IOptions<AiSettings> options,
    IFileSystem fileSystem,
    WorkspaceManager workspaceManager,
    IGitService gitService,
    TimeProvider time,
    IUserInteraction userInteraction,
    IRefinementService refinementService,
    ILogger<ChapterDrafterService> logger)
{
    public async Task DraftChapterAsync(ChapterState chapter, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Starting draft for Chapter {ChapterNumber}: {ChapterTitle}",
            chapter.Number,
            chapter.Title);

        var workspacePath = workspaceManager.CurrentWorkspacePath;

        if (string.IsNullOrWhiteSpace(workspacePath))
        {
            await userInteraction.NotifyAsync("Could not determine current workspace path. Cannot draft chapter.",
                new(MessageType.Error));

            logger.LogWarning(
                "Current workspace path is not set in WorkspaceManager. Chapter {ChapterNumber} drafting aborted",
                chapter.Number);

            return;
        }

        if (options.Value.Primary is null)
        {
            await userInteraction.NotifyAsync("No primary model is configured. Cannot draft chapter.",
                new(MessageType.Error));

            logger.LogWarning("Primary model not configured, cannot draft chapter");

            return;
        }

        var sid = options.Value.Primary.Provider;

        var initialDraft = await GenerateInitialDraft(chapter, sid, cancellationToken);

        if (string.IsNullOrWhiteSpace(initialDraft))
        {
            await userInteraction.NotifyAsync("Failed to generate an initial draft.", new(MessageType.Error));

            logger.LogWarning("Initial draft generation failed for chapter {ChapterNumber}", chapter.Number);

            return;
        }

        await userInteraction.NotifyAsync("Initial draft generated.", new(MessageType.Informational));

        await userInteraction.DisplayProsePassageAsync(initialDraft, "Initial draft");

        var ok = await userInteraction.ConfirmAsync("Do you want to refine this draft?",
            cancellationToken: cancellationToken);

        string finalDraft;

        if (!ok)
        {
            await userInteraction.NotifyAsync("Chapter drafting complete (initial draft accepted).",
                new(MessageType.Informational));

            finalDraft = initialDraft;
        }
        else
        {
            var systemPrompt =
                "You are an assistant helping to refine a chapter draft. Focus on improving it based on user feedback. Be concise and helpful.";

            finalDraft = await refinementService.RefineAsync(initialDraft, systemPrompt, sid, cancellationToken);
        }

        await userInteraction.NotifyAsync("Final chapter draft:", new(MessageType.Informational));
        await userInteraction.DisplayProsePassageAsync(finalDraft, "Final draft");

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

        var prompt = await renderer.RenderPromptTemplateFromFileAsync(kernel, request, cancellationToken: ct);

        var cid = $"draft-init-{chapter.Number}-{Guid.NewGuid()}";
        var history = new ChatHistory();
        history.AddSystemMessage(prompt);

        await userInteraction.NotifyAsync($"Generating initial draft for Chapter {chapter.Number}: {chapter.Title}...");

        var initialUserMessage = "Generate the chapter draft based on the details provided in your instructions.";

        var chatRequest = new ChatRequest(initialUserMessage, cid, sid);

        var draftStream = chat.StreamAsync(chatRequest, history, ct);

        var response = await userInteraction.DisplayAssistantResponseAsync(draftStream, ct);

        var generatedDraft = response.Trim();

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
            await userInteraction.NotifyAsync(
                "Could not determine workspace path from WorkspaceManager. Draft not saved.",
                new(MessageType.Error));

            logger.LogError(
                "Cannot save draft, workspace path unknown (from WorkspaceManager.CurrentWorkspacePath) for chapter {ChapterNumber}",
                chapter.Number);

            return;
        }

        // The project root is the parent of the .scribal workspace directory
        var projectRootPath = fileSystem.DirectoryInfo.New(workspacePath).Parent?.FullName;

        if (string.IsNullOrWhiteSpace(projectRootPath))
        {
            await userInteraction.NotifyError("Could not determine project root path. Draft not saved.");

            logger.LogError(
                "Cannot save draft, project root path could not be determined from workspace path {WorkspacePath} for chapter {ChapterNumber}",
                workspacePath,
                chapter.Number);

            return;
        }

        var chaptersDir = fileSystem.Path.Combine(projectRootPath, "chapters");
        fileSystem.Directory.CreateDirectory(chaptersDir);

        var sanitizedTitle = GetSanitizedTitle(chapter);

        var timestamp = time.GetUtcNow().ToString("yyyyMMddHHmmss");
        var draftFileName = $"chapter_{chapter.Number:D2}_{sanitizedTitle}_draft_{timestamp}.md";
        var draftFilePath = fileSystem.Path.Combine(chaptersDir, draftFileName);

        try
        {
            var workspaceState = await workspaceManager.LoadWorkspaceStateAsync(cancellationToken: cancellationToken);

            if (workspaceState is null)
            {
                await userInteraction.NotifyAsync(
                    "Could not determine workspace path from WorkspaceManager. Draft not saved.",
                    new(MessageType.Error));

                return;
            }
            
            await fileSystem.File.WriteAllTextAsync(draftFilePath, content, cancellationToken);
            
            await userInteraction.NotifyAsync($"Draft saved successfully to: {draftFilePath}.", new(MessageType.Informational));

            logger.LogInformation("Chapter {ChapterNumber} draft saved to {FilePath}", chapter.Number, draftFilePath);

            await AttemptGitCommit(chapter, draftFilePath, cancellationToken);

            // Update the workspace's state.
            var chapterState = workspaceState.Chapters.Single(s => s.Number == chapter.Number);
            chapterState.State = ChapterStateType.Draft;
            chapterState.DraftFilePath = draftFilePath;

            await workspaceManager.SaveWorkspaceStateAsync(workspaceState, cancellationToken: cancellationToken);

            await userInteraction.NotifyAsync("Workspace state updated with new draft information.", new(MessageType.Informational));
        }
        catch (Exception ex)
        {
            await userInteraction.NotifyError("Failed to save draft.", ex);

            logger.LogError(ex,
                "Failed to save draft for chapter {ChapterNumber} to {FilePath}",
                chapter.Number,
                draftFilePath);
        }
    }

    private string GetSanitizedTitle(ChapterState chapter)
    {
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

        return sanitizedTitle;
    }

    private async Task AttemptGitCommit(ChapterState chapter, string draftFilePath, CancellationToken cancellationToken)
    {
        if (!gitService.Enabled)
        {
            logger.LogInformation("Git service not enabled. Skipping commit for chapter {ChapterNumber} draft",
                chapter.Number);

            return;
        }

        var commitMessage = $"Drafted Chapter {chapter.Number}: {chapter.Title}";

        var commitSuccess = await gitService.CreateCommitAsync(draftFilePath, commitMessage, cancellationToken);

        if (commitSuccess)
        {
            await userInteraction.NotifyAsync($"Committed draft to git: {commitMessage}",
                new(MessageType.Informational));

            logger.LogInformation("Successfully committed draft for chapter {ChapterNumber}", chapter.Number);
        }
        else
        {
            await userInteraction.NotifyAsync($"Failed to commit draft for chapter {chapter.Number} to git.",
                new(MessageType.Error));

            logger.LogWarning("Failed to commit draft for chapter {ChapterNumber}", chapter.Number);
        }
    }
}