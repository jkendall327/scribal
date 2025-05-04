using System.IO.Abstractions;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.Google;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using OpenAI.Chat;
using Scribal.Cli;

#pragma warning disable SKEXP0070

namespace Scribal.AI;

public interface IAiChatService
{
    Task<string> AskAsync(string conversationId,
        string userMessage,
        string? serviceId = null,
        CancellationToken ct = default);

    IAsyncEnumerable<ChatStreamItem> StreamAsync(string conversationId,
        string userMessage,
        string? serviceId = null,
        CancellationToken ct = default);
}

public record ChatStreamItem
{
    private ChatStreamItem()
    {
    }

    public sealed record TokenChunk(string Content) : ChatStreamItem;

    public sealed record Metadata(TimeSpan Elapsed, string ServiceId, int PromptTokens, int CompletionTokens)
        : ChatStreamItem;
}

public sealed class AiChatService(
    Kernel kernel,
    IChatSessionStore store,
    PromptBuilder prompts,
    IFileSystem fileSystem,
    TimeProvider time) : IAiChatService
{
    private PromptExecutionSettings GetSettings(string? sid)
    {
        return sid switch
        {
            "gemini" => new GeminiPromptExecutionSettings
            {
                ToolCallBehavior = GeminiToolCallBehavior.AutoInvokeKernelFunctions
            },
            var _ => new OpenAIPromptExecutionSettings
            {
                ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions
            },
        };
    }

    public async Task<string> AskAsync(string cid, string user, string? sid, CancellationToken ct)
    {
        var chat = kernel.GetRequiredService<IChatCompletionService>(sid);
        var hist = await PrepareHistoryAsync(cid, user, ct);

        var reply = await chat.GetChatMessageContentAsync(hist, GetSettings(sid), kernel, ct);
        hist.AddAssistantMessage(reply.Content);

        await store.SaveAsync(cid, hist, ct);
        return reply.Content;
    }

    public async IAsyncEnumerable<ChatStreamItem> StreamAsync(string cid,
        string user,
        string? sid,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var start = time.GetTimestamp();

        var chat = kernel.GetRequiredService<IChatCompletionService>(sid);
        var history = await PrepareHistoryAsync(cid, user, ct);

        var stream = chat.GetStreamingChatMessageContentsAsync(history, GetSettings(sid), kernel, ct);

        await foreach (var chunk in stream)
        {
            if (chunk.Content is {Length: > 0} text)
            {
                // push token/partial phrase upstream
                yield return new ChatStreamItem.TokenChunk(text);
            }
        }

        // collect the full assistant message from streamed chunks
        var final = await chat.GetChatMessageContentAsync(history, GetSettings(sid), kernel, ct);
        var assistant = string.Concat(final).Trim();

        history.AddAssistantMessage(assistant);
        await store.SaveAsync(cid, history, ct);

        var elapsed = time.GetElapsedTime(start);

        int promptTokens = 0, completionTokens = 0;

        if (final.Metadata is GeminiMetadata geminiMetadata)
        {
            promptTokens = geminiMetadata.PromptTokenCount;
            completionTokens = geminiMetadata.CandidatesTokenCount;
        }
        else if (final.Metadata?.TryGetValue("Usage", out var u) is true && u is ChatTokenUsage usage)
        {
            promptTokens = usage.InputTokenCount;
            completionTokens = usage.OutputTokenCount;
        }
        
        yield return new ChatStreamItem.Metadata(Elapsed: elapsed,
            ServiceId: sid ?? final.ModelId ?? "[unknown]",
            PromptTokens: promptTokens,
            CompletionTokens: completionTokens);
    }

    private async Task<ChatHistory> PrepareHistoryAsync(string cid, string user, CancellationToken ct)
    {
        var history = await store.LoadAsync(cid, ct);

        if (history.All(m => m.Role != AuthorRole.System))
        {
            var system = await prompts.BuildSystemPrompt();
            history.AddSystemMessage(system);
        }

        if (history.All(m => m.Role != AuthorRole.User))
        {
            var cwd = fileSystem.Directory.GetCurrentDirectory();
            var info = fileSystem.DirectoryInfo.New(cwd);

            var cooked = await prompts.BuildPromptAsync(info, user);

            history.AddUserMessage(cooked);
        }

        history.AddUserMessage(user);
        return history;
    }
}