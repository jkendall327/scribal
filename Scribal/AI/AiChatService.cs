using System.IO.Abstractions;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.Google;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.Data;
using Microsoft.SemanticKernel.Memory;
using OpenAI.Chat;
using Scribal.Context;
using ChatMessageContent = Microsoft.SemanticKernel.ChatMessageContent;
#pragma warning disable SKEXP0001

#pragma warning disable SKEXP0070

namespace Scribal.AI;

public interface IAiChatService
{
    IAsyncEnumerable<ChatStreamItem> StreamAsync(string conversationId,
        string userMessage,
        string sid,
        CancellationToken ct = default);
}

/// <summary>
/// Discriminated union between a chunk of the model's streamed output and a metadata record produced when it's done.
/// </summary>
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
    public async IAsyncEnumerable<ChatStreamItem> StreamAsync(string cid,
        string user,
        string sid,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var start = time.GetTimestamp();

        var chat = kernel.GetRequiredService<IChatCompletionService>(sid);
        var history = await PrepareHistoryAsync(cid, user, ct);

        var settings = GetSettings(sid);
        
        // Stream response back for the UI.
        var stream = chat
            .GetStreamingChatMessageContentsAsync(history, settings, kernel, ct);

        await foreach (var chunk in stream)
        {
            if (chunk.Content is {Length: > 0} text)
            {
                yield return new ChatStreamItem.TokenChunk(text);
            }
        }

        // Collect the full assistant message from streamed chunks.
        var final = await chat
            .GetChatMessageContentAsync(history, settings, kernel, ct);
        
        await UpdateChatHistoryWithAssistantMessage(cid, final, history, ct);

        var metadata = CollectMetadata(sid, start, final);

        yield return metadata;
    }
    
    private PromptExecutionSettings GetSettings(string sid)
    {
        // Gemini requires special treatment to actually use tools.
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
    
    private async Task UpdateChatHistoryWithAssistantMessage(string cid,
        ChatMessageContent final,
        ChatHistory history,
        CancellationToken ct)
    {
        var assistant = string.Concat(final).Trim();
        history.AddAssistantMessage(assistant);
        await store.SaveAsync(cid, history, ct);
    }

    private ChatStreamItem.Metadata CollectMetadata(string? sid, long start, ChatMessageContent final)
    {
        var elapsed = time.GetElapsedTime(start);

        int promptTokens = 0, completionTokens = 0;

        if (final.Metadata is GeminiMetadata geminiMetadata)
        {
            promptTokens = geminiMetadata.PromptTokenCount;
            completionTokens = geminiMetadata.CandidatesTokenCount;
        }
        // OpenAI format
        else if (final.Metadata?.TryGetValue("Usage", out var u) is true && u is ChatTokenUsage usage)
        {
            promptTokens = usage.InputTokenCount;
            completionTokens = usage.OutputTokenCount;
        }

        var metadata = new ChatStreamItem.Metadata(Elapsed: elapsed,
            ServiceId: sid ?? final.ModelId ?? "[unknown]",
            PromptTokens: promptTokens,
            CompletionTokens: completionTokens);
        
        return metadata;
    }

    private async Task<ChatHistory> PrepareHistoryAsync(string cid, string user, CancellationToken ct)
    {
        var history = await store.LoadAsync(cid, ct);

        // New conversation, ensure system prompt is there.
        if (history.All(m => m.Role != AuthorRole.System))
        {
            var system = await prompts.BuildSystemPrompt(kernel);
            history.AddSystemMessage(system);
        }

        // Transform user's first message to include necessary context.
        // TODO: reseed this context when necessary...
        if (history.All(m => m.Role != AuthorRole.User))
        {
            var cwd = fileSystem.Directory.GetCurrentDirectory();
            var info = fileSystem.DirectoryInfo.New(cwd);

            var cooked = await prompts.BuildContextPrimerAsync(kernel, info, user);

            history.AddUserMessage(cooked);
        }

        history.AddUserMessage(user);
        return history;
    }
}