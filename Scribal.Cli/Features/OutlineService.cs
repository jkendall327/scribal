using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Scribal.AI;
using Scribal.Cli.Interface;
using Scribal.Config;
using Scribal.Context;
using Scribal.Workspace;
using Spectre.Console;

// Required for ChatHistory
// Required for ConsoleChatRenderer and ReadLine
// Required for StoryOutline and Chapter
// Required for AnsiConsole

// Required for JsonSerializer
// Scribal.Workspace; // This was the duplicate, removed. WorkspaceManager is covered by the other Scribal.Workspace using.

namespace Scribal.Cli.Features;

public class OutlineService(
    IAiChatService chat,
    PromptRenderer renderer,
    ConsoleChatRenderer consoleRenderer,
    Kernel kernel,
    IOptions<AiSettings> options,
    WorkspaceManager workspaceManager) // Injected WorkspaceManager
{
    public async Task CreateOutlineFromPremise(string premise, CancellationToken ct = default)
    {
        if (options.Value.Primary is null)
        {
            AnsiConsole.MarkupLine("[red]No primary model is configured. Cannot generate outline.[/]");

            return;
        }

        var sid = options.Value.Primary.Provider;

        var generatedOutlineJson = await GenerateInitialOutline(premise, sid, ct);

        if (!TryParseOutline(generatedOutlineJson, out var storyOutline))
        {
            AnsiConsole.MarkupLine("[red]Failed to parse the initial outline JSON.[/]");
            AnsiConsole.MarkupLine("[yellow]Displaying raw output:[/]");
            AnsiConsole.WriteLine(generatedOutlineJson);
        }
        else
        {
            DisplayParsedOutline(storyOutline!);
        }

        // Refinement loop.
        var ok = await AnsiConsole.ConfirmAsync("Do you want to refine this plot outline?", cancellationToken: ct);

        if (!ok)
        {
            AnsiConsole.MarkupLine("[yellow]Plot outline generation complete.[/]");

            if (storyOutline != null)
            {
                await workspaceManager.SavePlotOutlineAsync(storyOutline, premise);

                AnsiConsole.MarkupLine(
                    $"[green]Initial plot outline saved to workspace: .scribal/{PlotOutlineFileName}[/]");
            }
            else
            {
                AnsiConsole.MarkupLine(
                    "[yellow]Initial plot outline was not parsed successfully and therefore not saved.[/]");
            }

            return;
        }

        var refinementCid = $"outline-refine-{Guid.NewGuid()}";
        var refinementHistory = new ChatHistory();

        var sb = new StringBuilder(
            "You are an assistant helping to refine a story plot outline. The current outline is:");

        sb.AppendLine("---");
        sb.AppendLine(generatedOutlineJson); // Use the raw JSON for the refinement prompt
        sb.AppendLine("---");

        sb.AppendLine(
            "Focus on improving it based on user feedback. Ensure the chapter breakdown is clear and detailed as per original instructions. Be concise and helpful.");

        refinementHistory.AddSystemMessage(sb.ToString());

        // Add the AI's generated outline as the first assistant message
        refinementHistory.AddAssistantMessage(generatedOutlineJson); // Use the raw JSON

        AnsiConsole.MarkupLine(
            "Entering plot outline refinement chat. Type [blue]/done[/] when finished or [blue]/cancel[/] to abort.");

        var finalOutlineJson = await RefineOutline(refinementCid, refinementHistory, sid, generatedOutlineJson, ct);

        AnsiConsole.MarkupLine("[yellow]Plot outline refinement finished.[/]");
        AnsiConsole.MarkupLine("[yellow]Final plot outline (not saved yet):[/]");

        if (!TryParseOutline(finalOutlineJson, out var finalStoryOutline))
        {
            AnsiConsole.MarkupLine("[red]Failed to parse the final refined outline JSON.[/]");
            AnsiConsole.MarkupLine("[yellow]Displaying raw output:[/]");
            AnsiConsole.WriteLine(finalOutlineJson);
        }
        else
        {
            DisplayParsedOutline(finalStoryOutline!);
            await workspaceManager.SavePlotOutlineAsync(finalStoryOutline!, premise);
            AnsiConsole.MarkupLine($"[green]Final plot outline saved to workspace: .scribal/{PlotOutlineFileName}[/]");
        }
    }

    // Added PlotOutlineFileName constant for messaging, assuming it's not directly accessible here
    // Alternatively, WorkspaceManager could return the path, or this message could be more generic.
    private const string PlotOutlineFileName = "plot_outline.json";

    private bool TryParseOutline(string jsonText, out StoryOutline? outline)
    {
        outline = null;

        if (string.IsNullOrWhiteSpace(jsonText))
        {
            AnsiConsole.MarkupLine("[red]Outline JSON is empty.[/]");

            return false;
        }

        // Attempt to find the start of the JSON block if it's embedded
        var jsonStartIndex = jsonText.IndexOf('{');
        var jsonEndIndex = jsonText.LastIndexOf('}');

        if (jsonStartIndex == -1 || jsonEndIndex == -1 || jsonEndIndex < jsonStartIndex)
        {
            AnsiConsole.MarkupLine("[red]Could not find valid JSON structure in the output.[/]");

            return false;
        }

        var actualJson = jsonText.Substring(jsonStartIndex, jsonEndIndex - jsonStartIndex + 1);

        try
        {
            outline = JsonSerializer.Deserialize<StoryOutline>(actualJson, JsonDefaults.Context.StoryOutline);

            return outline?.Chapters is {Count: > 0};
        }
        catch (JsonException ex)
        {
            AnsiConsole.MarkupLine($"[red]Error deserializing outline JSON: {ex.Message}[/]");
            AnsiConsole.MarkupLine($"[dim]Path: {ex.Path}, Line: {ex.LineNumber}, BytePos: {ex.BytePositionInLine}[/]");

            return false;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]An unexpected error occurred during JSON parsing: {ex.Message}[/]");

            return false;
        }
    }

    private void DisplayParsedOutline(StoryOutline storyOutline)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[bold cyan]Story Outline[/]").RuleStyle("blue").LeftJustified());

        if (!storyOutline.Chapters.Any())
        {
            AnsiConsole.MarkupLine("[yellow]No chapters found in the outline.[/]");

            return;
        }

        foreach (var chapter in storyOutline.Chapters.OrderBy(c => c.ChapterNumber))
        {
            AnsiConsole.WriteLine();
            var title = $"Chapter {chapter.ChapterNumber}: {Markup.Escape(chapter.Title)}";
            AnsiConsole.Write(new Rule($"[bold yellow]{title}[/]").RuleStyle("green").LeftJustified());

            AnsiConsole.MarkupLine($"[bold]Summary:[/] {Markup.Escape(chapter.Summary)}");

            if (chapter.Beats.Any())
            {
                AnsiConsole.MarkupLine("[bold]Beats:[/]");

                foreach (var beat in chapter.Beats)
                {
                    AnsiConsole.MarkupLine($"  - {Markup.Escape(beat)}");
                }
            }

            if (chapter.EstimatedWordCount.HasValue)
            {
                AnsiConsole.MarkupLine($"[bold]Estimated Word Count:[/] {chapter.EstimatedWordCount.Value}");
            }

            if (chapter.KeyCharacters.Any())
            {
                AnsiConsole.MarkupLine(
                    $"[bold]Key Characters:[/] {Markup.Escape(string.Join(", ", chapter.KeyCharacters))}");
            }
        }

        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule().RuleStyle("blue"));
        AnsiConsole.WriteLine();
    }

    private async Task<string> GenerateInitialOutline(string premise, string sid, CancellationToken ct)
    {
        var request = new RenderRequest("Outline",
            "Outline",
            "Prompt for turning a story premise into a plot outline with chapter breakdown.",
            new()
            {
                {
                    "premise", premise
                }
            });

        var prompt = await renderer.RenderPromptTemplateFromFileAsync(kernel, request, cancellationToken: ct);

        var cid = $"outline-init-{Guid.NewGuid()}";

        var history = new ChatHistory();
        history.AddSystemMessage(prompt); // The prompt itself is the system message, premise is part of it via template

        AnsiConsole.MarkupLine("[yellow]Generating initial plot outline...[/]");

        // The user message to kick off the generation will be the premise,
        // or an empty message if the premise is already fully incorporated into the system prompt.
        // For this setup, the premise is part of the rendered system prompt.
        // So, an empty user message or a simple "Proceed." might be appropriate.
        // Let's use a simple instruction.
        var initialUserMessage = "Generate the plot outline based on the premise provided in your instructions.";

        var chatRequest = new ChatRequest(initialUserMessage, cid, sid);

        var task = chat.GetFullResponseWithExplicitHistoryAsync(chatRequest, history, ct);

        await consoleRenderer.WaitWithSpinnerAsync(task, ct);

        AnsiConsole.MarkupLine("[cyan]Initial Plot Outline Generated.[/]");

        // Displaying the outline (parsed or raw) will now be handled by the calling method CreateOutlineFromPremise
        AnsiConsole.WriteLine();

        return task.Result.Message;
    }

    private async Task<string> RefineOutline(string refinementCid,
        ChatHistory refinementHistory,
        string sid,
        string currentOutline,
        CancellationToken ct)
    {
        var lastAssistantResponse = currentOutline;

        while (true)
        {
            if (ct.IsCancellationRequested)
            {
                AnsiConsole.MarkupLine("[yellow]Refinement cancelled by host.[/]");

                break;
            }

            AnsiConsole.WriteLine();
            
            AnsiConsole.MarkupLine("(available commands: [blue]/done[/], [blue]/cancel[/])");

            AnsiConsole.Markup("[green]Refine Plot Outline > [/]");
            
            var userInput = ReadLine.Read();

            if (string.IsNullOrWhiteSpace(userInput))
            {
                continue;
            }

            if (userInput.Equals("/done", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            if (userInput.Equals("/cancel", StringComparison.OrdinalIgnoreCase))
            {
                AnsiConsole.MarkupLine("[yellow]Cancelling refinement...[/]");

                // Optionally revert to the outline before this cancel command
                return lastAssistantResponse; // Or perhaps the outline before starting refinement
            }

            AnsiConsole.WriteLine();

            try
            {
                // Add user's latest message to history for the next turn
                refinementHistory.AddUserMessage(userInput);

                var chatRequest = new ChatRequest(userInput, refinementCid, sid);

                var refinementStream = chat.GetFullResponseWithExplicitHistoryAsync(chatRequest, refinementHistory, ct);

                await consoleRenderer.WaitWithSpinnerAsync(refinementStream, ct);

                lastAssistantResponse = refinementStream.Result.Message;

                if (!string.IsNullOrWhiteSpace(lastAssistantResponse))
                {
                    refinementHistory.AddAssistantMessage(lastAssistantResponse);
                }
            }
            catch (OperationCanceledException)
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[yellow](Refinement cancelled)[/]");

                break;
            }
            catch (Exception e)
            {
                ExceptionDisplay.DisplayException(e);
                AnsiConsole.MarkupLine("[red]An error occurred during refinement.[/]");

                // Potentially remove the last user message if the turn failed
                // Or allow user to retry. For now, just log and continue.
            }
        }

        return lastAssistantResponse; // Return the latest version of the outline
    }
}