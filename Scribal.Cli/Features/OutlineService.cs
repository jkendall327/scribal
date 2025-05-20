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
    // ConsoleChatRenderer consoleRenderer, // No longer needed
    Kernel kernel,
    IOptions<AiSettings> options,
    WorkspaceManager workspaceManager, // Injected WorkspaceManager
    IUserInteraction userInteraction) 
{
    private readonly IUserInteraction _userInteraction = userInteraction;
    // private readonly ConsoleChatRenderer _consoleRenderer = consoleRenderer; // No longer needed

    public async Task CreateOutlineFromPremise(string? premise, CancellationToken ct = default)
    {
        if (options.Value.Primary is null)
        {
            await _userInteraction.NotifyAsync("No primary model is configured. Cannot generate outline.", new(MessageType.Error));

            return;
        }

        var sid = options.Value.Primary.Provider;

        premise = await CheckInputAgainstPotentialExistingPremise(premise, ct);

        if (premise is null)
        {
            return;
        }

        var generatedOutlineJson = await GenerateInitialOutline(premise, sid, ct);

        if (!await TryParseOutline(generatedOutlineJson, out var storyOutline, ct)) // Pass ct
        {
            await _userInteraction.NotifyAsync("Failed to parse the initial outline JSON.", new(MessageType.Error));
            await _userInteraction.NotifyAsync("Displaying raw output:", new(MessageType.Warning));
            await _userInteraction.NotifyAsync(generatedOutlineJson ?? string.Empty);
        }
        else
        {
            await DisplayParsedOutline(storyOutline!);
        }

        // Refinement loop.
        var ok = await _userInteraction.ConfirmAsync("Do you want to refine this plot outline?", ct);

        if (!ok)
        {
            await _userInteraction.NotifyAsync("Plot outline generation complete.", new(MessageType.Warning));

            if (storyOutline != null)
            {
                await workspaceManager.SavePlotOutlineAsync(storyOutline, premise);

                await _userInteraction.NotifyAsync(
                    $"Initial plot outline saved to workspace: .scribal/{PlotOutlineFileName}", new(MessageType.Informational));
            }
            else
            {
                await _userInteraction.NotifyAsync(
                    "Initial plot outline was not parsed successfully and therefore not saved.", new(MessageType.Warning));
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

        await _userInteraction.NotifyAsync(
            "Entering plot outline refinement chat. Type [blue]/done[/] when finished or [blue]/cancel[/] to abort.");

        var finalOutlineJson = await RefineOutline(refinementCid, refinementHistory, sid, generatedOutlineJson, ct);

        await _userInteraction.NotifyAsync("Plot outline refinement finished.", new(MessageType.Warning));
        await _userInteraction.NotifyAsync("Final plot outline (not saved yet):", new(MessageType.Warning));

        if (!await TryParseOutline(finalOutlineJson, out var finalStoryOutline, ct)) // Pass ct
        {
            await _userInteraction.NotifyAsync("Failed to parse the final refined outline JSON.", new(MessageType.Error));
            await _userInteraction.NotifyAsync("Displaying raw output:", new(MessageType.Warning));
            await _userInteraction.NotifyAsync(finalOutlineJson ?? string.Empty);
        }
        else
        {
            await DisplayParsedOutline(finalStoryOutline!);
            await workspaceManager.SavePlotOutlineAsync(finalStoryOutline!, premise);
            await _userInteraction.NotifyAsync($"Final plot outline saved to workspace: .scribal/{PlotOutlineFileName}", new(MessageType.Informational));
        }
    }

    private async Task<string?> CheckInputAgainstPotentialExistingPremise(string? premise, CancellationToken ct)
    {
        var state = await workspaceManager.LoadWorkspaceStateAsync(cancellationToken: ct);

        var existingPremise = state?.Premise;

        var userSuppliedAPremise = !string.IsNullOrWhiteSpace(premise);

        var workspaceHasPremise = !string.IsNullOrWhiteSpace(existingPremise);

        if (userSuppliedAPremise && workspaceHasPremise)
        {
            var selectionPrompt = new SelectionPrompt<string>()
                                  .Title(
                                      "You supplied a premise, but your workspace already has one. Pick which one to use:")
                                  .PageSize(3)
                                  .AddChoices("my existing premise", "the premise I just supplied");
            
            // AnsiConsole.Prompt is synchronous, IUserInteraction.PromptAsync is async.
            // This method is called by CreateOutlineFromPremise which is async.
            var response = await _userInteraction.PromptAsync(selectionPrompt);

            return response is "my existing premise" ? existingPremise : premise;
        }

        if (!userSuppliedAPremise && workspaceHasPremise)
        {
            await _userInteraction.DisplayProsePassageAsync(existingPremise!, "Existing premise");
            await _userInteraction.NotifyAsync(""); // For spacing

            var useSavedPremise =
                await _userInteraction.ConfirmAsync("You have a saved premise. Use this to generate the outline?", ct);

            if (useSavedPremise)
            {
                return existingPremise;
            }

            await _userInteraction.NotifyAsync(
                "Rerun the /outline command with your desired premise, e.g. '/outline \"a sci-fi epic about a horse\"'.");

            return null;
        }

        if (userSuppliedAPremise && !workspaceHasPremise)
        {
            return premise;
        }

        if (!userSuppliedAPremise && !workspaceHasPremise)
        {
            await _userInteraction.NotifyAsync("Premise cannot be empty.", new(MessageType.Error));
            await _userInteraction.NotifyAsync("Either use the /pitch command to generate one,", new(MessageType.Warning));
            await _userInteraction.NotifyAsync(
                "or rerun the /outline command with your desired premise, e.g. '/outline \"a sci-fi epic about a horse\"'.", new(MessageType.Warning));

            return null;
        }

        return premise;
    }

    // Added PlotOutlineFileName constant for messaging, assuming it's not directly accessible here
    // Alternatively, WorkspaceManager could return the path, or this message could be more generic.
    private const string PlotOutlineFileName = "plot_outline.json";

    private async Task<bool> TryParseOutline(string? jsonText, out StoryOutline? outline, CancellationToken ct) // Added ct
    {
        outline = null;

        if (string.IsNullOrWhiteSpace(jsonText))
        {
            await _userInteraction.NotifyAsync("Outline JSON is empty.", new(MessageType.Error), ct);
            return false;
        }

        // Attempt to find the start of the JSON block if it's embedded
        var jsonStartIndex = jsonText.IndexOf('{');
        var jsonEndIndex = jsonText.LastIndexOf('}');

        if (jsonStartIndex == -1 || jsonEndIndex == -1 || jsonEndIndex < jsonStartIndex)
        {
            await _userInteraction.NotifyAsync("Could not find valid JSON structure in the output.", new(MessageType.Error), ct);
            return false;
        }

        var actualJson = jsonText.Substring(jsonStartIndex, jsonEndIndex - jsonStartIndex + 1);

        try
        {
            outline = JsonSerializer.Deserialize<StoryOutline>(actualJson, JsonDefaults.Context.StoryOutline);
            if (outline?.Chapters is not {Count: > 0})
            {
                await _userInteraction.NotifyAsync("Parsed outline has no chapters.", new(MessageType.Warning), ct);
                return false; // Or true if an empty outline is valid but undesirable
            }
            return true;
        }
        catch (JsonException ex)
        {
            await _userInteraction.NotifyAsync($"Error deserializing outline JSON: {ex.Message}", new(MessageType.Error), ct);
            await _userInteraction.NotifyAsync($"Path: {ex.Path}, Line: {ex.LineNumber}, BytePos: {ex.BytePositionInLine}", new(MessageType.Error), ct);
            return false;
        }
        catch (Exception ex)
        {
            await _userInteraction.NotifyError("An unexpected error occurred during JSON parsing.", ex, ct);
            return false;
        }
    }

    private async Task DisplayParsedOutline(StoryOutline storyOutline)
    {
        await _userInteraction.NotifyAsync(""); // For spacing
        await _userInteraction.WriteRuleAsync(new Rule("[bold cyan]Story Outline[/]").RuleStyle("blue").LeftJustified());

        if (!storyOutline.Chapters.Any())
        {
            await _userInteraction.NotifyAsync("No chapters found in the outline.", new(MessageType.Warning));
            return;
        }

        foreach (var chapter in storyOutline.Chapters.OrderBy(c => c.ChapterNumber))
        {
            await _userInteraction.NotifyAsync(""); // For spacing
            var title = $"Chapter {chapter.ChapterNumber}: {Markup.Escape(chapter.Title)}";
            await _userInteraction.WriteRuleAsync(new Rule($"[bold yellow]{title}[/]").RuleStyle("green").LeftJustified());

            // Using DisplayProsePassageAsync for summary if it's potentially long
            await _userInteraction.DisplayProsePassageAsync(Markup.Escape(chapter.Summary), "Summary");
            // await _userInteraction.NotifyAsync($"[bold]Summary:[/] {Markup.Escape(chapter.Summary)}");


            if (chapter.Beats.Any())
            {
                await _userInteraction.NotifyAsync("[bold]Beats:[/]");
                foreach (var beat in chapter.Beats)
                {
                    await _userInteraction.NotifyAsync($"  - {Markup.Escape(beat)}");
                }
            }

            if (chapter.EstimatedWordCount.HasValue)
            {
                await _userInteraction.NotifyAsync($"[bold]Estimated Word Count:[/] {chapter.EstimatedWordCount.Value}");
            }

            if (chapter.KeyCharacters.Any())
            {
                await _userInteraction.NotifyAsync(
                    $"[bold]Key Characters:[/] {Markup.Escape(string.Join(", ", chapter.KeyCharacters))}");
            }
        }
        await _userInteraction.NotifyAsync(""); // For spacing
        await _userInteraction.WriteRuleAsync(new Rule().RuleStyle("blue"));
        await _userInteraction.NotifyAsync(""); // For spacing
    }

    private async Task<string> GenerateInitialOutline(string? premise, string sid, CancellationToken ct)
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

        await _userInteraction.NotifyAsync("Generating initial plot outline...", new(MessageType.Warning)); // Yellow -> Warning

        var initialUserMessage = "Generate the plot outline based on the premise provided in your instructions.";
        var chatRequest = new ChatRequest(initialUserMessage, cid, sid);
        
        string? resultMessage = null;
        await _userInteraction.StatusAsync(async statusContext => {
            var response = await chat.GetFullResponseWithExplicitHistoryAsync(chatRequest, history, ct);
            resultMessage = response.Message;
        });

        await _userInteraction.NotifyAsync("Initial Plot Outline Generated.", new(MessageType.Informational)); // Cyan -> Informational
        await _userInteraction.NotifyAsync(""); // For spacing

        return resultMessage ?? string.Empty;
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
                await _userInteraction.NotifyAsync("Refinement cancelled by host.", new(MessageType.Warning), ct);
                break;
            }

            await _userInteraction.NotifyAsync(""); // For spacing
            await _userInteraction.NotifyAsync("(available commands: [blue]/done[/], [blue]/cancel[/])");
            
            // IUserInteraction.GetUserInputAsync does not display a prompt itself.
            // The prompt needs to be sent via NotifyAsync if a visual prefix is desired.
            // For now, relying on the above help text.
            // await _userInteraction.NotifyAsync("[green]Refine Plot Outline > [/]", new MessageOptions { NoNewLine = true });
            var userInput = await _userInteraction.GetUserInputAsync(ct);

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
                await _userInteraction.NotifyAsync("Cancelling refinement...", new(MessageType.Warning), ct);
                return lastAssistantResponse; 
            }

            await _userInteraction.NotifyAsync(""); // For spacing

            try
            {
                refinementHistory.AddUserMessage(userInput);
                var chatRequest = new ChatRequest(userInput, refinementCid, sid);

                string? newResponse = null;
                await _userInteraction.StatusAsync(async statusContext => {
                    var response = await chat.GetFullResponseWithExplicitHistoryAsync(chatRequest, refinementHistory, ct);
                    newResponse = response.Message;
                });
                
                if (!string.IsNullOrWhiteSpace(newResponse))
                {
                    lastAssistantResponse = newResponse;
                    refinementHistory.AddAssistantMessage(lastAssistantResponse);
                    // Display the refined outline after each successful step
                    if (await TryParseOutline(lastAssistantResponse, out var parsedRefinedOutline, ct))
                    {
                        await DisplayParsedOutline(parsedRefinedOutline!);
                    }
                    else
                    {
                        await _userInteraction.NotifyAsync("Displaying raw refined output due to parse failure:", new(MessageType.Warning), ct);
                        await _userInteraction.NotifyAsync(lastAssistantResponse, cancellationToken: ct);
                    }
                }
                else
                {
                    await _userInteraction.NotifyAsync("AI did not return a response for refinement.", new(MessageType.Warning), ct);
                }
            }
            catch (OperationCanceledException)
            {
                await _userInteraction.NotifyAsync(""); // For spacing
                await _userInteraction.NotifyAsync("(Refinement cancelled)", new(MessageType.Warning), ct);
                break;
            }
            catch (Exception e)
            {
                await _userInteraction.NotifyError("An error occurred during refinement.", e, ct);
            }
        }
        return lastAssistantResponse;
    }
}