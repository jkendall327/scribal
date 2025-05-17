using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Scribal.AI;
using Scribal.Cli.Interface;
using Scribal.Config;
using Scribal.Context;
using Scribal.Workspace;
using Spectre.Console;

namespace Scribal.Cli.Features;

public class PitchService(
    IAiChatService chat,
    PromptRenderer renderer,
    Kernel kernel,
    IAnsiConsole console,
    IOptions<AiSettings> options,
    WorkspaceManager workspaceManager,
    ConsoleChatRenderer consoleChatRenderer) // AI: Added ConsoleChatRenderer
{
    public async Task CreatePremiseFromPitch(string pitch, CancellationToken ct = default)
    {
        if (options.Value.Primary is null)
        {
            console.MarkupLine("[red]No primary model is configured. Cannot generate premise.[/]");

            return;
        }

        var sid = options.Value.Primary.Provider;

        var initialGeneratedPremise = await GenerateInitialPremise(pitch, sid, ct);
        var finalPremiseToSave = initialGeneratedPremise;

        // Refinement loop.
        var okToRefine = await console.ConfirmAsync("Do you want to refine this premise?", cancellationToken: ct);

        if (okToRefine)
        {
            var refinementCid = $"pitch-refine-{Guid.NewGuid()}";
            var refinementHistory = new ChatHistory();

            var sb = new StringBuilder(
                "You are an assistant helping to refine a story premise. The current premise is:");

            sb.AppendLine("---");
            sb.AppendLine(initialGeneratedPremise);
            sb.AppendLine("---");
            sb.AppendLine("Focus on improving it based on user feedback. Be concise and helpful.");

            refinementHistory.AddSystemMessage(sb.ToString());
            refinementHistory.AddAssistantMessage(initialGeneratedPremise);

            console.MarkupLine(
                "Entering premise refinement chat. Type [blue]/done[/] when finished or [blue]/cancel[/] to abort.");

            var refinementCompletedSuccessfully = await RefinePremise(refinementCid, refinementHistory, sid, ct);

            if (refinementCompletedSuccessfully)
            {
                var lastAssistantMessage = refinementHistory.LastOrDefault(m => m.Role == AuthorRole.Assistant);

                if (lastAssistantMessage?.Content is not null)
                {
                    finalPremiseToSave = lastAssistantMessage.Content.Trim();
                    console.MarkupLine("[green]Premise successfully refined.[/]");
                }
                else
                {
                    console.MarkupLine(
                        "[yellow]Refinement marked complete, but no refined premise found. Using initial premise.[/]");
                }
            }
            else
            {
                console.MarkupLine(
                    "[yellow]Premise refinement was cancelled or not completed. Using initial premise.[/]");
            }
        }
        else
        {
            console.MarkupLine("[yellow]Initial premise accepted without refinement.[/]");
        }

        await UpdateWorkspaceAfterPremiseFinalizedAsync(finalPremiseToSave, ct);
    }

    private async Task<string> GenerateInitialPremise(string initialPitch, string sid, CancellationToken ct)
    {
        var request = new RenderRequest("Premise", "Premise", "Prompt for turning a story pitch into a premise", new());

        var prompt = await renderer.RenderPromptTemplateFromFileAsync(kernel, request, cancellationToken: ct);

        var cid = $"pitch-init-{Guid.NewGuid()}";

        var history = new ChatHistory();
        history.AddSystemMessage(prompt);

        console.MarkupLine("[yellow]Generating initial premise...[/]");

        var chatRequest = new ChatRequest(initialPitch, cid, sid);

        var premiseStream = chat.StreamAsync(chatRequest, history, ct);

        var premiseBuilder = new StringBuilder();

        // Stream the initial premise generation.
        // AI: Use injected ConsoleChatRenderer instance
        await consoleChatRenderer.StreamWithSpinnerAsync(CollectWhileStreaming(premiseStream, premiseBuilder, ct), ct);

        var generatedPremise = premiseBuilder.ToString().Trim();

        console.MarkupLine("[cyan]Initial Premise Generated.[/]");
        console.WriteLine();

        return generatedPremise;
    }

    private async Task<bool> RefinePremise(string refinementCid,
        ChatHistory refinementHistory,
        string sid,
        CancellationToken ct)
    {
        while (true)
        {
            if (ct.IsCancellationRequested)
            {
                console.MarkupLine("[yellow]Refinement cancelled by host.[/]");

                return false;
            }

            console.WriteLine();
            console.Markup("[green]Refine Premise > [/]");
            var userInput = ReadLine.Read();

            if (string.IsNullOrWhiteSpace(userInput))
            {
                continue;
            }

            if (userInput.Equals("/done", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (userInput.Equals("/cancel", StringComparison.OrdinalIgnoreCase))
            {
                console.MarkupLine("[yellow]Cancelling refinement...[/]");

                return false;
            }

            console.WriteLine();

            try
            {
                var request = new ChatRequest(userInput, refinementCid, sid);

                var refinementStream = chat.StreamAsync(request, refinementHistory, ct);

                // AI: Use injected ConsoleChatRenderer instance
                await consoleChatRenderer.StreamWithSpinnerAsync(refinementStream, ct);
            }
            catch (OperationCanceledException)
            {
                console.WriteLine();
                console.MarkupLine("[yellow](Refinement stream cancelled)[/]");

                return false;
            }
            catch (Exception e)
            {
                console.WriteException(e);
                console.MarkupLine("[red]An error occurred during refinement.[/]");

                return false;
            }
        }
    }

    private async Task UpdateWorkspaceAfterPremiseFinalizedAsync(string premise, CancellationToken ct)
    {
        if (!workspaceManager.InWorkspace)
        {
            console.MarkupLine("[yellow]Not in a Scribal workspace. Premise not saved to workspace state.[/]");

            return;
        }

        var state = await workspaceManager.LoadWorkspaceStateAsync(cancellationToken: ct);

        if (state is null)
        {
            console.MarkupLine("[red]Could not load workspace state. Premise not saved.[/]");

            return;
        }

        state.Premise = premise;
        state.PipelineStage = PipelineStageType.AwaitingOutline;
        await workspaceManager.SaveWorkspaceStateAsync(state, cancellationToken: ct);
        console.MarkupLine("[green]Premise saved and pipeline stage updated to AwaitingOutline.[/]");
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