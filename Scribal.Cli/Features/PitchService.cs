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
    IOptions<AiSettings> options,
    WorkspaceManager workspaceManager,
    IRefinementService refinementService,
    IUserInteraction userInteraction)
{
    private readonly IUserInteraction _userInteraction = userInteraction;

    public async Task CreatePremiseFromPitch(string pitch, CancellationToken ct = default)
    {
        if (options.Value.Primary is null)
        {
            await _userInteraction.NotifyAsync("No primary model is configured. Cannot generate premise.", new(MessageType.Error), ct);

            return;
        }

        var sid = options.Value.Primary.Provider;

        var initial = await GenerateInitialPremise(pitch, sid, ct);
        var final = initial;

        var ok = await _userInteraction.ConfirmAsync("Do you want to refine this premise?", ct);

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

        await _userInteraction.NotifyAsync("Generating initial premise...", new(MessageType.Warning), ct);

        var chatRequest = new ChatRequest(initialPitch, cid, sid);
        var premiseStream = chat.StreamAsync(chatRequest, history, ct);

        // Stream the initial premise generation using IUserInteraction
        var generatedPremise = await _userInteraction.DisplayAssistantResponseAsync(premiseStream, ct);
        
        if (string.IsNullOrWhiteSpace(generatedPremise))
        {
            await _userInteraction.NotifyAsync("AI failed to generate a premise.", new(MessageType.Error), ct);
            // Return empty or handle as an error case, depending on desired behavior
            return string.Empty; 
        }

        await _userInteraction.NotifyAsync("Initial Premise Generated.", new(MessageType.Informational), ct); // Cyan -> Informational
        await _userInteraction.NotifyAsync("", ct); // For spacing

        return generatedPremise.Trim(); // Ensure it's trimmed, though DisplayAssistantResponseAsync might handle this.
    }

    private async Task UpdateWorkspaceAfterPremiseFinalizedAsync(string premise, CancellationToken ct)
    {
        if (!workspaceManager.InWorkspace)
        {
            await _userInteraction.NotifyAsync("Not in a Scribal workspace. Premise not saved to workspace state.", new(MessageType.Warning), ct);
            return;
        }

        var state = await workspaceManager.LoadWorkspaceStateAsync(cancellationToken: ct);

        if (state is null)
        {
            await _userInteraction.NotifyAsync("Could not load workspace state. Premise not saved.", new(MessageType.Error), ct);
            return;
        }

        state.Premise = premise;
        state.PipelineStage = PipelineStageType.AwaitingOutline;
        await workspaceManager.SaveWorkspaceStateAsync(state, cancellationToken: ct);
        await _userInteraction.NotifyAsync("Premise saved and pipeline stage updated to AwaitingOutline.", new(MessageType.Informational), ct);
    }
}