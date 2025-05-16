using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Scribal.AI;
using Scribal.Cli;
using Scribal.Context;
using Scribal.Workspace; // AI: Added for WorkspaceManager and PipelineStageType
using Spectre.Console;

// Required for ChatHistory
// Required for ConsoleChatRenderer and ReadLine
// Required for AnsiConsole

namespace Scribal;

public class PitchService(
    IAiChatService chat,
    PromptRenderer renderer,
    Kernel kernel,
    IOptions<AiSettings> options,
    WorkspaceManager workspaceManager) // AI: Added WorkspaceManager
{
    private readonly WorkspaceManager _workspaceManager = workspaceManager; // AI: Store WorkspaceManager instance

    public async Task CreatePremiseFromPitch(string pitch, CancellationToken ct = default)
    {
        if (options.Value.Primary is null)
        {
            AnsiConsole.MarkupLine("[red]No primary model is configured. Cannot generate premise.[/]");

            return;
        }

        var sid = options.Value.Primary.Provider;

        var initialGeneratedPremise = await GenerateInitialPremise(pitch, sid, ct);
        string finalPremiseToSave = initialGeneratedPremise; // AI: Default to initial premise

        // Refinement loop.
        var okToRefine = await AnsiConsole.ConfirmAsync("Do you want to refine this premise?", cancellationToken: ct);

        if (okToRefine)
        {
            var refinementCid = $"pitch-refine-{Guid.NewGuid()}";
            var refinementHistory = new ChatHistory();

            var sb = new StringBuilder("You are an assistant helping to refine a story premise. The current premise is:");
            sb.AppendLine("---");
            sb.AppendLine(initialGeneratedPremise); // AI: Use initial premise for refinement context
            sb.AppendLine("---");
            sb.AppendLine("Focus on improving it based on user feedback. Be concise and helpful.");

            refinementHistory.AddSystemMessage(sb.ToString());
            refinementHistory.AddAssistantMessage(initialGeneratedPremise); // AI: Add initial premise as assistant's starting point

            AnsiConsole.MarkupLine(
                "Entering premise refinement chat. Type [blue]/done[/] when finished or [blue]/cancel[/] to abort.");

            bool refinementCompletedSuccessfully = await RefinePremise(refinementCid, refinementHistory, sid, ct);

            if (refinementCompletedSuccessfully)
            {
                var lastAssistantMessage = refinementHistory.LastOrDefault(m => m.Role == AuthorRole.Assistant);
                if (lastAssistantMessage?.Content is not null)
                {
                    finalPremiseToSave = lastAssistantMessage.Content.Trim();
                    AnsiConsole.MarkupLine("[green]Premise successfully refined.[/]");
                }
                else
                {
                    AnsiConsole.MarkupLine("[yellow]Refinement marked complete, but no refined premise found. Using initial premise.[/]");
                }
            }
            else
            {
                AnsiConsole.MarkupLine("[yellow]Premise refinement was cancelled or not completed. Using initial premise.[/]");
            }
        }
        else
        {
            AnsiConsole.MarkupLine("[yellow]Initial premise accepted without refinement.[/]");
        }

        // AI: Save the determined premise and update workspace state
        await UpdateWorkspaceAfterPremiseFinalizedAsync(finalPremiseToSave, ct);
    }

    private async Task<string> GenerateInitialPremise(string initialPitch, string sid, CancellationToken ct)
    {
        var request = new RenderRequest("Premise", "Premise", "Prompt for turning a story pitch into a premise", new());

        var prompt = await renderer.RenderPromptTemplateFromFileAsync(kernel, request, cancellationToken: ct);

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

    // AI: Modified to return true if user types /done, false otherwise
    private async Task<bool> RefinePremise(string refinementCid,
        ChatHistory refinementHistory,
        string sid,
        CancellationToken ct)
    {
        while (true)
        {
            if (ct.IsCancellationRequested)
            {
                AnsiConsole.MarkupLine("[yellow]Refinement cancelled by host.[/]");
                return false; // AI: Cancelled
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
                return true; // AI: Successfully completed by user
            }

            if (userInput.Equals("/cancel", StringComparison.OrdinalIgnoreCase))
            {
                AnsiConsole.MarkupLine("[yellow]Cancelling refinement...[/]");
                return false; // AI: User cancelled
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
                AnsiConsole.MarkupLine("[yellow](Refinement stream cancelled)[/]");
                return false; // AI: Stream cancelled
            }
            catch (Exception e)
            {
                AnsiConsole.WriteException(e);
                AnsiConsole.MarkupLine("[red]An error occurred during refinement.[/]");
                return false; // AI: Error during refinement
            }
        }
    }

    // AI: New method to update workspace state after premise is finalized
    private async Task UpdateWorkspaceAfterPremiseFinalizedAsync(string premise, CancellationToken ct)
    {
        if (!_workspaceManager.InWorkspace)
        {
            AnsiConsole.MarkupLine("[yellow]Not in a Scribal workspace. Premise not saved to workspace state.[/]");
            return;
        }

        var state = await _workspaceManager.LoadWorkspaceStateAsync(cancellationToken: ct);
        if (state is null)
        {
            AnsiConsole.MarkupLine("[red]Could not load workspace state. Premise not saved.[/]");
            return;
        }

        state.Premise = premise;
        state.PipelineStage = PipelineStageType.AwaitingOutline; // AI: Update pipeline stage
        await _workspaceManager.SaveWorkspaceStateAsync(state, cancellationToken: ct);
        AnsiConsole.MarkupLine("[green]Premise saved and pipeline stage updated to AwaitingOutline.[/]");
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
