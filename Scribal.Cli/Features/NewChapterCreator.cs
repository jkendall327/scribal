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
    IAnsiConsole console,
    WorkspaceManager workspaceManager,
    IAiChatService chat,
    PromptRenderer renderer,
    Kernel kernel,
    ConsoleChatRenderer consoleChatRenderer,
    IUserInteraction userInteraction,
    RefinementService refinementService,
    IOptions<AiSettings> options,
    ILogger<NewChapterCreator> logger)
{
    public async Task CreateNewChapterAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Starting new chapter creation process");

        var state = await workspaceManager.LoadWorkspaceStateAsync(cancellationToken: cancellationToken);

        if (state is null)
        {
            console.MarkupLine("[red]Could not load workspace state. Cannot create new chapter.[/]");
            logger.LogWarning("Failed to load workspace state during new chapter creation");

            return;
        }

        var nextOrdinal = state.Chapters.Any() ? state.Chapters.Max(c => c.Number) + 1 : 1;
        var ordinal = console.Ask<int>($"Enter ordinal position for the new chapter (e.g., {nextOrdinal}):");

        if (ordinal <= 0)
        {
            console.MarkupLine("[red]Ordinal position must be a positive number.[/]");
            logger.LogWarning("Invalid ordinal position entered: {Ordinal}", ordinal);

            return;
        }

        var title = console.Ask<string>("Enter title for the new chapter:");

        if (string.IsNullOrWhiteSpace(title))
        {
            console.MarkupLine("[red]Chapter title cannot be empty.[/]");
            logger.LogWarning("Empty chapter title entered");

            return;
        }

        var initialContent = await GetInitialContent(title, ordinal, cancellationToken);

        var success = await workspaceManager.AddNewChapterAsync(ordinal, title, initialContent, cancellationToken);

        if (success)
        {
            console.MarkupLine($"[green]Successfully created new chapter: {ordinal}. {Markup.Escape(title)}[/]");
            logger.LogInformation("Successfully created and saved new chapter {Ordinal}: {Title}", ordinal, title);
        }
        else
        {
            console.MarkupLine($"[red]Failed to create new chapter: {ordinal}. {Markup.Escape(title)}[/]");
            logger.LogError("Failed to save new chapter {Ordinal}: {Title} via WorkspaceManager", ordinal, title);
        }
    }

    private async Task<string> GetInitialContent(string title, int ordinal, CancellationToken cancellationToken)
    {
        var initialContent = string.Empty;

        var provideDraftYourself =
            await userInteraction.ConfirmAsync("Do you want to provide an initial draft yourself?");

        if (provideDraftYourself)
        {
            console.MarkupLine(
                "[yellow]Enter your initial draft content. Press Ctrl+D (Unix) or Ctrl+Z then Enter (Windows) on a new line when done.[/]");

            var sb = new StringBuilder();

            // ReadLine.Read() handles multi-line input better than console.Ask for this
            while (ReadLine.Read() is { } line)
            {
                sb.AppendLine(line);
            }

            initialContent = sb.ToString().Trim();
            logger.LogInformation("User provided initial draft for new chapter '{Title}'", title);
        }
        else
        {
            var generateWithAi = await userInteraction.ConfirmAsync("Do you want the AI to generate an initial draft?");

            if (generateWithAi)
            {
                var summary =
                    console.Ask<string>("Provide a brief summary/goal for the AI to draft this chapter (optional):");

                initialContent = await GenerateAiDraftAsync(ordinal, title, summary, cancellationToken);

                if (!string.IsNullOrWhiteSpace(initialContent))
                {
                    return initialContent;
                }

                console.MarkupLine(
                    "[yellow]AI draft generation was cancelled or resulted in empty content. Chapter will be created empty.[/]");

                logger.LogWarning(
                    "AI draft generation for new chapter '{Title}' resulted in empty content or was cancelled",
                    title);

                initialContent = string.Empty; // Ensure it's empty
            }
            else
            {
                console.MarkupLine("[yellow]New chapter will be created with empty content.[/]");
                logger.LogInformation("User opted to create new chapter '{Title}' with empty content", title);
            }
        }

        return initialContent;
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
            console.MarkupLine("[red]No primary model is configured. Cannot generate AI draft.[/]");
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

        console.MarkupLine(
            $"[yellow]Generating initial AI draft for new Chapter {chapterNumber}: {Markup.Escape(chapterTitle)}...[/]");

        var initialUserMessage = "Generate the chapter draft based on the details provided in your instructions.";
        var chatRequest = new ChatRequest(initialUserMessage, cid, sid);

        var draftStream = chat.StreamAsync(chatRequest, history, ct);
        var draftBuilder = new StringBuilder();
        await consoleChatRenderer.StreamWithSpinnerAsync(draftStream.CollectWhileStreaming(draftBuilder, ct), ct);

        var generatedDraft = draftBuilder.ToString().Trim();

        logger.LogInformation("Initial AI draft generated for new Chapter {ChapterNumber}, length {Length}",
            chapterNumber,
            generatedDraft.Length);

        if (string.IsNullOrWhiteSpace(generatedDraft))
        {
            console.MarkupLine("[red]AI failed to generate an initial draft.[/]");

            return string.Empty;
        }

        console.MarkupLine("[cyan]Initial AI Draft Generated:[/]");
        console.WriteLine(Markup.Escape(generatedDraft));

        var okToRefine = await userInteraction.ConfirmAsync("Do you want to refine this AI draft?");

        if (!okToRefine)
        {
            console.MarkupLine("[yellow]AI draft accepted for new chapter.[/]");

            return generatedDraft;
        }

        var systemPrompt =
            "You are an assistant helping to refine a new chapter draft. Focus on improving it based on user feedback. Be concise and helpful.";
        
        var finalDraft = await refinementService.RefineAsync(generatedDraft, systemPrompt, sid, ct);
        console.MarkupLine("[yellow]Chapter draft refinement finished for new chapter.[/]");

        return finalDraft;
    }
}