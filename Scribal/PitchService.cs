using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Scribal.AI;
using Scribal.Context;

namespace Scribal;

public class PitchService(
    IAiChatService chat,
    PromptRenderer renderer,
    Kernel kernel,
    IChatSessionStore store,
    IOptions<AiSettings> options)
{
    public async Task CreatePremiseFromPitch(string pitch, CancellationToken cancellationToken = default)
    {
        var request = new RenderRequest("Premise", "Premise", "Prompt for turning a story pitch into a premise", new());

        var prompt = await renderer.RenderPromptTemplateFromFileAsync(kernel, request);

        var conversation = await store.LoadAsync("", cancellationToken);

        conversation.AddSystemMessage(prompt);

        var sid = options.Value.Primary.Provider;
        
        var enumerable = chat.StreamAsync("", pitch, sid, cancellationToken);
    }
}