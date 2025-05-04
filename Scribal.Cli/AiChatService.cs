using System.Runtime.CompilerServices;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

#pragma warning disable SKEXP0070

namespace Scribal.Cli;

public interface IAiChatService
{
    /// Single-shot (non-streaming) convenience.
    Task<string> AskAsync(string conversationId,
        string userMessage,
        string? serviceId = null,
        CancellationToken ct = default);

    /// Low-latency streaming: yields partial tokens as soon as they arrive.
    IAsyncEnumerable<string> StreamAsync(string conversationId,
        string userMessage,
        string? serviceId = null,
        CancellationToken ct = default);
}

public sealed class AiChatService : IAiChatService
{
    private readonly Kernel _kernel;
    private readonly IChatSessionStore _store;
    private readonly PromptBuilder _prompts;

    public AiChatService(Kernel kernel, IChatSessionStore store, PromptBuilder prompts)
    {
        _kernel = kernel;
        _store = store;
        _prompts = prompts;
    }

    private static OpenAIPromptExecutionSettings Settings =>
        new()
        {
            ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions
        };

    public async Task<string> AskAsync(string cid, string user, string? sid, CancellationToken ct)
    {
        var chat = _kernel.GetRequiredService<IChatCompletionService>(sid);
        var hist = await PrepareHistoryAsync(cid, user, ct);

        var reply = await chat.GetChatMessageContentAsync(hist, Settings, _kernel, ct);
        hist.AddAssistantMessage(reply.Content);

        await _store.SaveAsync(cid, hist, ct);
        return reply.Content;
    }

    public async IAsyncEnumerable<string> StreamAsync(string cid,
        string user,
        string? sid,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var chat = _kernel.GetRequiredService<IChatCompletionService>(sid);
        var hist = await PrepareHistoryAsync(cid, user, ct);

        await foreach (var chunk in chat.GetStreamingChatMessageContentsAsync(hist, Settings, _kernel, ct))
        {
            if (chunk.Content is {Length: > 0} text)
            {
                // push token/partial phrase upstream
                yield return text; 
            }
        }

        // collect the full assistant message from streamed chunks
        var assistant = string.Concat(await chat.GetChatMessageContentAsync(hist, Settings, _kernel, ct)).Trim();

        hist.AddAssistantMessage(assistant);
        await _store.SaveAsync(cid, hist, ct);
    }

    private async Task<ChatHistory> PrepareHistoryAsync(string cid, string user, CancellationToken ct)
    {
        var hist = await _store.LoadAsync(cid, ct);

        if (hist.All(m => m.Role != AuthorRole.System))
        {
            hist.AddSystemMessage(PromptBuilder.SystemPrompt);
        }

        hist.AddUserMessage(user);
        return hist;
    }
}