using System.Runtime.CompilerServices;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion; // Required for ChatHistory
using Scribal.AI;
using Scribal.Cli; // Required for ConsoleChatRenderer and ReadLine
using Scribal.Context;
using Spectre.Console; // Required for AnsiConsole
using System.Text; // Required for StringBuilder

namespace Scribal;

public class PitchService(
    IAiChatService chat,
    PromptRenderer renderer,
    Kernel kernel,
    IOptions<AiSettings> options)
{
    public async Task CreatePremiseFromPitch(string initialPitch, CancellationToken commandCancellationToken = default)
    {
        if (options.Value.Primary is null)
        {
            AnsiConsole.MarkupLine("[red]No primary model is configured. Cannot generate premise.[/]");
            return;
        }

        var primaryProviderSid = options.Value.Primary.Provider;

        // --- Step 1: Generate the initial premise ---
        var premiseRenderRequest =
            new RenderRequest("Premise", "Premise", "Prompt for turning a story pitch into a premise", new());
        var premiseSystemPrompt = await renderer.RenderPromptTemplateFromFileAsync(kernel, premiseRenderRequest);

        string initialPremiseConversationId = $"pitch-init-{Guid.NewGuid()}";
        var initialHistory = new ChatHistory();
        initialHistory.AddSystemMessage(premiseSystemPrompt);
        // user message (initialPitch) will be added by StreamWithExplicitHistoryAsync

        AnsiConsole.MarkupLine("[yellow]Generating initial premise...[/]");

        var premiseStream = chat.StreamWithExplicitHistoryAsync(initialPremiseConversationId,
            initialHistory,
            initialPitch,
            primaryProviderSid,
            commandCancellationToken);

        var premiseBuilder = new StringBuilder();
        // Use ConsoleChatRenderer to show the stream for initial premise generation
        await ConsoleChatRenderer.StreamWithSpinnerAsync(
            CollectWhileStreaming(premiseStream, premiseBuilder, commandCancellationToken),
            commandCancellationToken);

        string generatedPremise = premiseBuilder.ToString().Trim();
        // The ConsoleChatRenderer already adds a newline, so we might not need extra ones here.
        // AnsiConsole.WriteLine(); // Already handled by StreamWithSpinnerAsync
        AnsiConsole.MarkupLine("[cyan]Initial Premise Generated.[/]");
        AnsiConsole.WriteLine();

        // --- Step 2: Interactive Refinement Loop ---
        if (!await AnsiConsole.ConfirmAsync("Do you want to refine this premise?",
                cancellationToken: commandCancellationToken))
        {
            return;
        }

        string refinementConversationId = $"pitch-refine-{Guid.NewGuid()}";
        var refinementHistory = new ChatHistory();
        // System prompt for refinement
        refinementHistory.AddSystemMessage(
            $"You are an assistant helping to refine a story premise. The current premise is:\n---\n{generatedPremise
            }\n---\nFocus on improving it based on user feedback. Be concise and helpful.");
        // Add the generated premise as the first AI message in the refinement history
        refinementHistory.AddAssistantMessage(generatedPremise);

        AnsiConsole.MarkupLine(
            "Entering premise refinement chat. Type [blue]/done[/] when finished or [blue]/cancel[/] to abort.");

        while (true)
        {
            if (commandCancellationToken.IsCancellationRequested)
            {
                AnsiConsole.MarkupLine("[yellow]Refinement cancelled by host.[/]");
                break;
            }

            AnsiConsole.WriteLine();
            AnsiConsole.Markup("[green]Refine Premise > [/]");
            var userInput = ReadLine.Read(); // ReadLine does not directly support CancellationToken

            if (string.IsNullOrWhiteSpace(userInput)) continue;
            if (userInput.Equals("/done", StringComparison.OrdinalIgnoreCase)) break;
            if (userInput.Equals("/cancel", StringComparison.OrdinalIgnoreCase))
            {
                AnsiConsole.MarkupLine("[yellow]Refinement aborted by user.[/]");
                break;
            }

            AnsiConsole.WriteLine(); // Newline after prompt before AI response

            try
            {
                var refinementStream = chat.StreamWithExplicitHistoryAsync(refinementConversationId,
                    refinementHistory,
                    userInput,
                    primaryProviderSid,
                    commandCancellationToken);

                await ConsoleChatRenderer.StreamWithSpinnerAsync(refinementStream, commandCancellationToken);
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
            }
        }

        AnsiConsole.MarkupLine("[yellow]Premise refinement finished.[/]");
    }

    // Helper to collect content while streaming for the initial premise
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
}