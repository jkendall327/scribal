using System.Runtime.CompilerServices;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion; // Required for ChatHistory
using Scribal.AI;
using Scribal.Cli; // Required for ConsoleChatRenderer and ReadLine
using Scribal.Context;
using Spectre.Console; // Required for AnsiConsole
using System.Text;

namespace Scribal.Cli.Features;
// Changed namespace to Scribal.Cli.Features

public class OutlineService(IAiChatService chat, PromptRenderer renderer, Kernel kernel, IOptions<AiSettings> options)
{
    public async Task CreateOutlineFromPremise(string premise, CancellationToken ct = default)
    {
        if (options.Value.Primary is null)
        {
            AnsiConsole.MarkupLine("[red]No primary model is configured. Cannot generate outline.[/]");
            return;
        }

        var sid = options.Value.Primary.Provider;

        var generatedOutline = await GenerateInitialOutline(premise, ct, sid);

        // Refinement loop.
        var ok = await AnsiConsole.ConfirmAsync("Do you want to refine this plot outline?", cancellationToken: ct);

        if (!ok)
        {
            // Here you might want to save the generatedOutline to a file, e.g., plot_outline.md
            AnsiConsole.MarkupLine("[yellow]Plot outline generation complete. Outline not saved yet.[/]");
            AnsiConsole.WriteLine(generatedOutline); // Display the final outline if not refining
            return;
        }

        var refinementCid = $"outline-refine-{Guid.NewGuid()}";
        var refinementHistory = new ChatHistory();

        var sb = new StringBuilder(
            "You are an assistant helping to refine a story plot outline. The current outline is:");
        sb.AppendLine("---");
        sb.AppendLine(generatedOutline);
        sb.AppendLine("---");
        sb.AppendLine(
            "Focus on improving it based on user feedback. Ensure the chapter breakdown is clear and detailed as per original instructions. Be concise and helpful.");

        refinementHistory.AddSystemMessage(sb.ToString());
        refinementHistory
            .AddAssistantMessage(generatedOutline); // Add the AI's generated outline as the first assistant message

        AnsiConsole.MarkupLine(
            "Entering plot outline refinement chat. Type [blue]/done[/] when finished or [blue]/cancel[/] to abort.");

        var finalOutline = await RefineOutline(ct, refinementCid, refinementHistory, sid, generatedOutline);

        AnsiConsole.MarkupLine("[yellow]Plot outline refinement finished.[/]");
        // Here you might want to save the finalOutline to a file
        AnsiConsole.MarkupLine("[yellow]Final plot outline (not saved yet):[/]");
        AnsiConsole.WriteLine(finalOutline);
    }

    private async Task<string> GenerateInitialOutline(string premise, CancellationToken ct, string sid)
    {
        // Assuming a prompt template file named "OutlineFromPremise.scribal-prompt" exists
        // The prompt should instruct the AI to create a synopsis and chapter breakdown from the premise.
        var request = new RenderRequest("Outline",
            "OutlineFromPremise",
            "Prompt for turning a story premise into a plot outline with chapter breakdown.",
            new()
            {
                {
                    "premise", premise
                }
            });

        var prompt = await renderer.RenderPromptTemplateFromFileAsync(kernel, request);

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

        var outlineStream = chat.StreamWithExplicitHistoryAsync(cid, history, initialUserMessage, sid, ct);

        var outlineBuilder = new StringBuilder();

        await ConsoleChatRenderer.StreamWithSpinnerAsync(CollectWhileStreaming(outlineStream, outlineBuilder, ct), ct);

        var generatedOutline = outlineBuilder.ToString().Trim();

        AnsiConsole.MarkupLine("[cyan]Initial Plot Outline Generated.[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine(generatedOutline); // Display the generated outline
        AnsiConsole.WriteLine();
        return generatedOutline;
    }

    private async Task<string> RefineOutline(CancellationToken ct,
        string refinementCid,
        ChatHistory refinementHistory,
        string sid,
        string currentOutline)
    {
        string lastAssistantResponse = currentOutline;

        while (true)
        {
            if (ct.IsCancellationRequested)
            {
                AnsiConsole.MarkupLine("[yellow]Refinement cancelled by host.[/]");
                break;
            }

            AnsiConsole.WriteLine();
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
            var responseBuilder = new StringBuilder();

            try
            {
                // Add user's latest message to history for the next turn
                refinementHistory.AddUserMessage(userInput);

                var refinementStream = chat.StreamWithExplicitHistoryAsync(refinementCid,
                    refinementHistory,
                    userInput, // This is somewhat redundant if history is managed correctly
                    sid,
                    ct);

                // We need to collect the AI's response to update refinementHistory and lastAssistantResponse
                await ConsoleChatRenderer.StreamWithSpinnerAsync(
                    CollectWhileStreaming(refinementStream, responseBuilder, ct),
                    ct);

                lastAssistantResponse = responseBuilder.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(lastAssistantResponse))
                {
                    refinementHistory.AddAssistantMessage(lastAssistantResponse); // Add AI's response to history
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
                AnsiConsole.WriteException(e);
                AnsiConsole.MarkupLine("[red]An error occurred during refinement.[/]");
                // Potentially remove the last user message if the turn failed
                // Or allow user to retry. For now, just log and continue.
            }
        }

        return lastAssistantResponse; // Return the latest version of the outline
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

            // You might want to handle other ChatStreamItem types like Metadata explicitly
            yield return item;
        }
    }
}