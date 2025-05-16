using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Scribal.AI;
using Scribal.Cli;
using Scribal.Context;
using Spectre.Console;

// Required for ChatHistory
// Required for ConsoleChatRenderer and ReadLine
// Required for AnsiConsole

namespace Scribal;

public class PitchService(IAiChatService chat, PromptRenderer renderer, Kernel kernel, IOptions<AiSettings> options)
{
    public async Task CreatePremiseFromPitch(string pitch, CancellationToken ct = default)
    {
        if (options.Value.Primary is null)
        {
            AnsiConsole.MarkupLine("[red]No primary model is configured. Cannot generate premise.[/]");

            return;
        }

        var sid = options.Value.Primary.Provider;

        var generatedPremise = await GenerateInitialPremise(pitch, ct, sid);

        // Refinement loop.
        var ok = await AnsiConsole.ConfirmAsync("Do you want to refine this premise?", cancellationToken: ct);

        if (!ok)
        {
            return;
        }

        var refinementCid = $"pitch-refine-{Guid.NewGuid()}";
        var refinementHistory = new ChatHistory();

        var sb = new StringBuilder("You are an assistant helping to refine a story premise. The current premise is:");
        sb.AppendLine("---");
        sb.AppendLine(generatedPremise);
        sb.AppendLine("---");
        sb.AppendLine("Focus on improving it based on user feedback. Be concise and helpful.");

        refinementHistory.AddSystemMessage(sb.ToString());

        refinementHistory.AddAssistantMessage(generatedPremise);

        AnsiConsole.MarkupLine(
            "Entering premise refinement chat. Type [blue]/done[/] when finished or [blue]/cancel[/] to abort.");

        await RefinePremise(ct, refinementCid, refinementHistory, sid);

        AnsiConsole.MarkupLine("[yellow]Premise refinement finished.[/]");
    }

    private async Task<string> GenerateInitialPremise(string initialPitch, CancellationToken ct, string sid)
    {
        var request = new RenderRequest("Premise", "Premise", "Prompt for turning a story pitch into a premise", new());

        var prompt = await renderer.RenderPromptTemplateFromFileAsync(kernel, request);

        var cid = $"pitch-init-{Guid.NewGuid()}";

        var history = new ChatHistory();
        history.AddSystemMessage(prompt);

        AnsiConsole.MarkupLine("[yellow]Generating initial premise...[/]");

        var premiseStream = chat.StreamWithExplicitHistoryAsync(cid, history, initialPitch, sid, ct);

        var premiseBuilder = new StringBuilder();

        // Stream the initial premise generation.
        await ConsoleChatRenderer.StreamWithSpinnerAsync(CollectWhileStreaming(premiseStream, premiseBuilder, ct), ct);

        var generatedPremise = premiseBuilder.ToString().Trim();

        AnsiConsole.MarkupLine("[cyan]Initial Premise Generated.[/]");
        AnsiConsole.WriteLine();

        return generatedPremise;
    }

    private async Task RefinePremise(CancellationToken ct,
        string refinementCid,
        ChatHistory refinementHistory,
        string sid)
    {
        while (true)
        {
            if (ct.IsCancellationRequested)
            {
                AnsiConsole.MarkupLine("[yellow]Refinement cancelled by host.[/]");

                break;
            }

            AnsiConsole.WriteLine();
            AnsiConsole.Markup("[green]Refine Premise > [/]");
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
                AnsiConsole.MarkupLine("[yellow]Cancelling...[/]");

                break;
            }

            AnsiConsole.WriteLine();

            try
            {
                var refinementStream = chat.StreamWithExplicitHistoryAsync(refinementCid,
                    refinementHistory,
                    userInput,
                    sid,
                    ct);

                await ConsoleChatRenderer.StreamWithSpinnerAsync(refinementStream, ct);
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