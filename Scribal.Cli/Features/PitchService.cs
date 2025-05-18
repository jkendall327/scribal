using System.Runtime.CompilerServices;
using System.Text;
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

public class PitchService(
    IAiChatService chat,
    PromptRenderer renderer,
    Kernel kernel,
    IAnsiConsole console,
    IOptions<AiSettings> options,
    WorkspaceManager workspaceManager,
    IRefinementService refinementService,
    ConsoleChatRenderer consoleChatRenderer)
{
    public async Task CreatePremiseFromPitch(string pitch, CancellationToken ct = default)
    {
        if (options.Value.Primary is null)
        {
            console.MarkupLine("[red]No primary model is configured. Cannot generate premise.[/]");

            return;
        }

        var sid = options.Value.Primary.Provider;

        var initial = await GenerateInitialPremise(pitch, sid, ct);
        var final = initial;

        var ok = await console.ConfirmAsync("Do you want to refine this premise?", cancellationToken: ct);

        if (ok)
        {
            var systemPrompt =
                "You are an assistant helping to refine a story premise. Focus on improving it based on user feedback. Be concise and helpful.";

            final = await refinementService.RefineAsync(initial, systemPrompt, sid, ct);
        }

        await UpdateWorkspaceAfterPremiseFinalizedAsync(final, ct);
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
        await consoleChatRenderer.StreamWithSpinnerAsync(premiseStream.CollectWhileStreaming(premiseBuilder, ct), ct);

        var generatedPremise = premiseBuilder.ToString().Trim();

        console.MarkupLine("[cyan]Initial Premise Generated.[/]");
        console.WriteLine();

        return generatedPremise;
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
}