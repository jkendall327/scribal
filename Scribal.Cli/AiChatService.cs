using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.Google;
using Microsoft.SemanticKernel.Connectors.OpenAI;
#pragma warning disable SKEXP0070

namespace Scribal.Cli;

public interface IAiChatService
{
    Task<string> AskAsync(string user, string? provider = null, CancellationToken ct = default);
}

public sealed class AiChatService : IAiChatService
{
    private readonly Kernel _kernel;

    public AiChatService(Kernel kernel) => _kernel = kernel;

    public async Task<string> AskAsync(string user, string? provider = null, CancellationToken ct = default)
    {
        // pick the requested model, or let SK choose the default one
        var chat = _kernel.GetRequiredService<IChatCompletionService>(provider);

        // execution settings: enable auto tool invocation
        PromptExecutionSettings settings = new OpenAIPromptExecutionSettings
        {
            ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions
        };

        if (provider is "gemini")
        {
            settings = new GeminiPromptExecutionSettings
            {
                ToolCallBehavior = GeminiToolCallBehavior.AutoInvokeKernelFunctions
            };
        }
        
        var history = new ChatHistory();
        history.AddUserMessage(user);

        // one-liner chat call that can transparently loop through tool calls
        var reply = await chat.GetChatMessageContentAsync(history, settings, _kernel, ct);

        return reply.Content;
    }
}
