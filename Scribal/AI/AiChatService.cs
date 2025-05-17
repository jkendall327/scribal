using System.IO.Abstractions;
using System.Runtime.CompilerServices;
using System.Text;
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

public record ChatMessage(string Message, ChatStreamItem.Metadata Metadata);

public sealed class AiChatService(
    Kernel kernel,
    IChatSessionStore store,
    PromptBuilder prompts,
    IFileSystem fileSystem,
    TimeProvider time,
    MetadataCollector metadataCollector) : IAiChatService
{
    public async IAsyncEnumerable<ChatStreamItem> StreamAsync(string cid,
        string user,
        string sid,
        ChatHistory? history = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var start = time.GetTimestamp();

        var chat = kernel.GetRequiredService<IChatCompletionService>(sid);

        if (history == null)
        {
            // Start a conversation from scratch, preparing default system prompt etc.
            history = await PrepareHistoryAsync(cid, user, ct);
        }
        else
        {
            history.AddUserMessage(user);
        }

        var settings = GetSettings(sid);

        // Stream response back for the UI.
        var stream = chat.GetStreamingChatMessageContentsAsync(history, settings, kernel, ct);

        var sb = new StringBuilder();

        var hunks = new List<StreamingChatMessageContent>();

        await foreach (var fragment in stream)
        {
            hunks.Add(fragment);

            if (fragment.Content is not {Length: > 0} text)
            {
                continue;
            }

            var hunk = new ChatStreamItem.TokenChunk(text);

            sb.Append(hunk.Content);

            yield return hunk;
        }

        await UpdateChatHistoryWithAssistantMessage(cid, sb.ToString(), history, ct);

        // We simply assume the last returned hunk is the one to contain token usage metadata.
        var usage = hunks.Last().Metadata;
        var fakeMessage = new ChatMessageContent(AuthorRole.Assistant, sb.ToString(), metadata: usage);

        var metadata = metadataCollector.CollectMetadata(sid, start, fakeMessage);

        yield return metadata;
    }

    public async Task<ChatMessage> GetFullResponseWithExplicitHistoryAsync(string conversationId,
        ChatHistory history,
        string userMessage,
        string sid,
        CancellationToken ct = default)
    {
        var start = time.GetTimestamp();
        var chat = kernel.GetRequiredService<IChatCompletionService>(sid);

        history.AddUserMessage(userMessage);

        var settings = GetSettings(sid);

        var finalAssistantMessageContent = await chat.GetChatMessageContentAsync(history, settings, kernel, ct);

        var assistantResponseText = string.Concat(finalAssistantMessageContent).Trim();

        history.AddAssistantMessage(assistantResponseText);

        await store.SaveAsync(conversationId, history, ct);

        var metadata = metadataCollector.CollectMetadata(sid, start, finalAssistantMessageContent);

        return new(assistantResponseText, metadata);
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
        string final,
        ChatHistory history,
        CancellationToken ct)
    {
        var assistant = final.Trim();
        history.AddAssistantMessage(assistant);
        await store.SaveAsync(cid, history, ct);
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
        else // If not the first user message, just add the plain user message
        {
            history.AddUserMessage(user);
        }

        return history;
    }
}