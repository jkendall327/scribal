using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Scribal.AI;
using Scribal.Cli.Infrastructure;
using Scribal.Cli.Interface;
using Scribal.Config;
using Scribal.Context;
using Scribal.Workspace;
using Spectre.Console;

namespace Scribal.Cli.Features;

public class NewChapterCreator(
    WorkspaceManager workspaceManager,
    IAiChatService chat,
    PromptRenderer renderer,
    Kernel kernel,
    ConsoleChatRenderer consoleChatRenderer, // This is likely part of what IUserInteraction wraps or replaces for direct UI
    IUserInteraction userInteraction,
    IRefinementService refinementService,
    IOptions<AiSettings> options,
    ILogger<NewChapterCreator> logger)
{
    private readonly IUserInteraction _userInteraction = userInteraction;

    public async Task CreateNewChapterAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Starting new chapter creation process");

        var state = await workspaceManager.LoadWorkspaceStateAsync(cancellationToken: cancellationToken);

        if (state is null)
        {
            await _userInteraction.NotifyAsync("Could not load workspace state. Cannot create new chapter.", new(MessageType.Error));
            logger.LogWarning("Failed to load workspace state during new chapter creation");

            return;
        }

        var nextOrdinal = state.Chapters.Any() ? state.Chapters.Max(c => c.Number) + 1 : 1;
        var ordinal = await _userInteraction.AskAsync<int>($"Enter ordinal position for the new chapter (e.g., {nextOrdinal}):", cancellationToken: cancellationToken);

        if (ordinal <= 0)
        {
            await _userInteraction.NotifyAsync("Ordinal position must be a positive number.", new(MessageType.Error));
            logger.LogWarning("Invalid ordinal position entered: {Ordinal}", ordinal);

            return;
        }

        var title = await _userInteraction.AskAsync<string>("Enter title for the new chapter:", cancellationToken: cancellationToken);

        if (string.IsNullOrWhiteSpace(title))
        {
            await _userInteraction.NotifyAsync("Chapter title cannot be empty.", new(MessageType.Error));
            logger.LogWarning("Empty chapter title entered");

            return;
        }

        var initialContent = await GetInitialContent(title, ordinal, cancellationToken);

        var success = await workspaceManager.AddNewChapterAsync(ordinal, title, initialContent, cancellationToken);

        if (success)
        {
            await _userInteraction.NotifyAsync($"Successfully created new chapter: {ordinal}. {Markup.Escape(title)}", new(MessageType.Informational));
            logger.LogInformation("Successfully created and saved new chapter {Ordinal}: {Title}", ordinal, title);
        }
        else
        {
            await _userInteraction.NotifyAsync($"Failed to create new chapter: {ordinal}. {Markup.Escape(title)}", new(MessageType.Error));
            logger.LogError("Failed to save new chapter {Ordinal}: {Title} via WorkspaceManager", ordinal, title);
        }
    }

    private async Task<string> GetInitialContent(string title, int ordinal, CancellationToken cancellationToken)
    {
        var initialContent = string.Empty;

        var provideDraftYourself =
            await _userInteraction.ConfirmAsync("Do you want to provide an initial draft yourself?", cancellationToken);

        if (provideDraftYourself)
        {
            initialContent = await _userInteraction.GetMultilineInputAsync(
                "Enter your initial draft content. Press Ctrl+D (Unix) or Ctrl+Z then Enter (Windows) on a new line when done:");
            logger.LogInformation("User provided initial draft for new chapter '{Title}'", title);
        }
        else
        {
            var generateWithAi = await _userInteraction.ConfirmAsync("Do you want the AI to generate an initial draft?", cancellationToken);

            if (generateWithAi)
            {
                var summary =
                    await _userInteraction.AskAsync<string>("Provide a brief summary/goal for the AI to draft this chapter (optional):", cancellationToken: cancellationToken);

                initialContent = await GenerateAiDraftAsync(ordinal, title, summary, cancellationToken);

                if (!string.IsNullOrWhiteSpace(initialContent))
                {
                    return initialContent;
                }

                await _userInteraction.NotifyAsync(
                    "AI draft generation was cancelled or resulted in empty content. Chapter will be created empty.", new(MessageType.Warning));

                logger.LogWarning(
                    "AI draft generation for new chapter '{Title}' resulted in empty content or was cancelled",
                    title);

                initialContent = string.Empty; // Ensure it's empty
            }
            else
            {
                await _userInteraction.NotifyAsync("New chapter will be created with empty content.", new(MessageType.Warning));
                logger.LogInformation("User opted to create new chapter '{Title}' with empty content", title);
            }
        }

        return initialContent.Trim(); // Trim applies if GetMultilineInputAsync doesn't already.
    }

    private async Task<string> GenerateAiDraftAsync(int chapterNumber,
        string chapterTitle,
        string? chapterSummary,
        CancellationToken ct)
    {
        logger.LogInformation("Starting AI draft generation for new Chapter {ChapterNumber}: {ChapterTitle}",
            chapterNumber,
            chapterTitle);

        if (options.Value.Primary is null)
        {
            await _userInteraction.NotifyAsync("No primary model is configured. Cannot generate AI draft.", new(MessageType.Error));
            logger.LogWarning("Primary model not configured, cannot generate AI draft for new chapter");

            return string.Empty;
        }

        var sid = options.Value.Primary.Provider;

        var arguments = new KernelArguments
        {
            {
                "chapter_number", chapterNumber.ToString()
            },
            {
                "chapter_title", chapterTitle
            },
            {
                "chapter_summary", chapterSummary ?? string.Empty
            }
        };

        var request = new RenderRequest(
            "DraftChapter", // Reusing existing prompt, consider a new one if specific nuances are needed
            "DraftChapter",
            "Prompt for drafting a chapter based on its details.",
            arguments);

        var prompt = await renderer.RenderPromptTemplateFromFileAsync(kernel, request, cancellationToken: ct);

        var cid = $"new-chapter-draft-{chapterNumber}-{Guid.NewGuid()}";
        var history = new ChatHistory();
        history.AddSystemMessage(prompt);

        await _userInteraction.NotifyAsync(
            $"Generating initial AI draft for new Chapter {chapterNumber}: {Markup.Escape(chapterTitle)}...", new(MessageType.Warning)); // Yellow -> Warning

        var initialUserMessage = "Generate the chapter draft based on the details provided in your instructions.";
        var chatRequest = new ChatRequest(initialUserMessage, cid, sid);

        var draftStream = chat.StreamAsync(chatRequest, history, ct);
        
        // Replaced consoleChatRenderer.StreamWithSpinnerAsync with _userInteraction.DisplayAssistantResponseAsync
        var generatedDraft = await _userInteraction.DisplayAssistantResponseAsync(draftStream, ct);
        // var draftBuilder = new StringBuilder();
        // await consoleChatRenderer.StreamWithSpinnerAsync(draftStream.CollectWhileStreaming(draftBuilder, ct), ct);
        // var generatedDraft = draftBuilder.ToString().Trim();


        logger.LogInformation("Initial AI draft generated for new Chapter {ChapterNumber}, length {Length}",
            chapterNumber,
            generatedDraft.Length);

        if (string.IsNullOrWhiteSpace(generatedDraft))
        {
            await _userInteraction.NotifyAsync("AI failed to generate an initial draft.", new(MessageType.Error));

            return string.Empty;
        }

        await _userInteraction.NotifyAsync("Initial AI Draft Generated:", new(MessageType.Informational)); // Cyan -> Informational
        await _userInteraction.NotifyAsync(Markup.Escape(generatedDraft));

        var okToRefine = await _userInteraction.ConfirmAsync("Do you want to refine this AI draft?", ct);

        if (!okToRefine)
        {
            await _userInteraction.NotifyAsync("AI draft accepted for new chapter.", new(MessageType.Warning));

            return generatedDraft;
        }

        var systemPrompt =
            "You are an assistant helping to refine a new chapter draft. Focus on improving it based on user feedback. Be concise and helpful.";
        
        var finalDraft = await refinementService.RefineAsync(generatedDraft, systemPrompt, sid, ct);
        await _userInteraction.NotifyAsync("Chapter draft refinement finished for new chapter.", new(MessageType.Warning));

        return finalDraft;
    }
}