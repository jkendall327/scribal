using System.IO.Abstractions;
using System.Runtime.CompilerServices;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.Google;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Scribal.Context;
using ChatMessageContent = Microsoft.SemanticKernel.ChatMessageContent;

#pragma warning disable SKEXP0001

#pragma warning disable SKEXP0070

namespace Scribal.AI;

/// <summary>
///     Discriminated union between a chunk of the model's streamed output and a metadata record produced when it's done.
/// </summary>
public record ChatStreamItem
{
    private ChatStreamItem()
    {
    }

    public sealed record TokenChunk(string Content) : ChatStreamItem;

    public sealed record Metadata(TimeSpan Elapsed, int PromptTokens, int CompletionTokens) : ChatStreamItem;
}

public sealed class AiChatService(
    Kernel kernel,
    IChatSessionStore store,
    PromptBuilder prompts,
    IFileSystem fileSystem,
    TimeProvider time,
    MetadataCollector metadataCollector) : IAiChatService // Added MetadataCollector
{
    public async IAsyncEnumerable<ChatStreamItem> StreamAsync(string cid,
        string user,
        string sid,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var start = time.GetTimestamp();

        var chat = kernel.GetRequiredService<IChatCompletionService>(sid);
        var history = await PrepareHistoryAsync(cid, user, ct); // PrepareHistoryAsync adds the user message

        var settings = GetSettings(sid);

        // Stream response back for the UI.
        var stream = chat.GetStreamingChatMessageContentsAsync(history, settings, kernel, ct);

        await foreach (var chunk in stream)
        {
            if (chunk.Content is {Length: > 0} text)
            {
                yield return new ChatStreamItem.TokenChunk(text);
            }
        }

        // Collect the full assistant message from streamed chunks.
        var final = await chat.GetChatMessageContentAsync(history, settings, kernel, ct);

        await UpdateChatHistoryWithAssistantMessage(cid, final, history, ct);

        // Use the new MetadataCollector
        var metadata = metadataCollector.CollectMetadata(sid, start, final);

        yield return metadata;
    }

    public async IAsyncEnumerable<ChatStreamItem> StreamWithExplicitHistoryAsync(string conversationId,
        ChatHistory history,
        string userMessage,
        string sid,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var start = time.GetTimestamp();
        var chat = kernel.GetRequiredService<IChatCompletionService>(sid);

        // Add the current user message to the provided history
        history.AddUserMessage(userMessage);

        var settings = GetSettings(sid);

        // Stream response back for the UI.
        var stream = chat.GetStreamingChatMessageContentsAsync(history, settings, kernel, ct);

        await foreach (var chunk in stream)
        {
            if (chunk.Content is {Length: > 0} text)
            {
                yield return new ChatStreamItem.TokenChunk(text);
            }
        }

        // Collect the full assistant message from streamed chunks.
        var finalAssistantMessageContent = await chat.GetChatMessageContentAsync(history, settings, kernel, ct);

        // Update the passed-in ChatHistory object with the assistant's full response
        var assistantMessageText = string.Concat(finalAssistantMessageContent).Trim();
        history.AddAssistantMessage(assistantMessageText); // This modifies the 'history' instance

        // Save the updated history
        await store.SaveAsync(conversationId, history, ct);

        // Collect and yield metadata using the new MetadataCollector
        var metadata = metadataCollector.CollectMetadata(sid, start, finalAssistantMessageContent);

        yield return metadata;
    }

    public async Task<(string AssistantResponse, ChatStreamItem.Metadata Metadata)>
        GetFullResponseWithExplicitHistoryAsync(string conversationId,
            ChatHistory history,
            string userMessage,
            string sid,
            CancellationToken ct = default)
    {
        var start = time.GetTimestamp();
        var chat = kernel.GetRequiredService<IChatCompletionService>(sid);

        // Add the current user message to the provided history
        history.AddUserMessage(userMessage);

        var settings = GetSettings(sid);

        // Get the full response from the AI
        var finalAssistantMessageContent = await chat.GetChatMessageContentAsync(history, settings, kernel, ct);

        // Extract the assistant's full response text
        var assistantResponseText = string.Concat(finalAssistantMessageContent).Trim();

        // Update the passed-in ChatHistory object with the assistant's full response
        history.AddAssistantMessage(assistantResponseText); // This modifies the 'history' instance

        // Save the updated history
        await store.SaveAsync(conversationId, history, ct);

        // Collect metadata using the MetadataCollector
        var metadata = metadataCollector.CollectMetadata(sid, start, finalAssistantMessageContent);

        return (assistantResponseText, metadata);
    }

    private PromptExecutionSettings GetSettings(string sid)
    {
        // Gemini requires special treatment to actually use tools.
        return sid.ToLowerInvariant() switch // Ensure case-insensitivity for service ID
        {
            "gemini" => new GeminiPromptExecutionSettings
            {
                ToolCallBehavior = GeminiToolCallBehavior.AutoInvokeKernelFunctions
            },
            var _ => new OpenAIPromptExecutionSettings // Default to OpenAI settings
            {
                ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions
            }
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

    // Removed CollectMetadata method from here

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
        else // If not the first user message, just add the plain user message
        {
            history.AddUserMessage(user);
        }

        return history;
    }
}