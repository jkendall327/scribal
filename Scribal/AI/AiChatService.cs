using System.IO.Abstractions;
using System.Runtime.CompilerServices;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Scribal.Cli;

#pragma warning disable SKEXP0070

namespace Scribal.AI;

public interface IAiChatService
{
    Task<string> AskAsync(string conversationId,
        string userMessage,
        string? serviceId = null,
        CancellationToken ct = default);

    IAsyncEnumerable<string> StreamAsync(string conversationId,
        string userMessage,
        string? serviceId = null,
        CancellationToken ct = default);
}

public sealed class AiChatService(Kernel kernel, IChatSessionStore store, PromptBuilder prompts, IFileSystem fileSystem)
    : IAiChatService
{

    private static OpenAIPromptExecutionSettings Settings =>
        new()
        {
            ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions
        };

    public async Task<string> AskAsync(string cid, string user, string? sid, CancellationToken ct)
    {
        var chat = kernel.GetRequiredService<IChatCompletionService>(sid);
        var hist = await PrepareHistoryAsync(cid, user, ct);

        var reply = await chat.GetChatMessageContentAsync(hist, Settings, kernel, ct);
        hist.AddAssistantMessage(reply.Content);

        await store.SaveAsync(cid, hist, ct);
        return reply.Content;
    }

    public async IAsyncEnumerable<string> StreamAsync(string cid,
        string user,
        string? sid,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var chat = kernel.GetRequiredService<IChatCompletionService>(sid);
        var hist = await PrepareHistoryAsync(cid, user, ct);

        await foreach (var chunk in chat.GetStreamingChatMessageContentsAsync(hist, Settings, kernel, ct))
        {
            if (chunk.Content is {Length: > 0} text)
            {
                // push token/partial phrase upstream
                yield return text; 
            }
        }

        // collect the full assistant message from streamed chunks
        var assistant = string.Concat(await chat.GetChatMessageContentAsync(hist, Settings, kernel, ct)).Trim();

        hist.AddAssistantMessage(assistant);
        await store.SaveAsync(cid, hist, ct);
    }

    private async Task<ChatHistory> PrepareHistoryAsync(string cid, string user, CancellationToken ct)
    {
        var hist = await store.LoadAsync(cid, ct);

        if (hist.All(m => m.Role != AuthorRole.System))
        {
            hist.AddSystemMessage(PromptBuilder.SystemPrompt);
        }
        
        if (hist.All(m => m.Role != AuthorRole.User))
        {
            var cwd = fileSystem.Directory.GetCurrentDirectory();
            var info = fileSystem.DirectoryInfo.New(cwd);
            
            var cooked = await prompts.BuildPromptAsync(info, user);

            hist.AddUserMessage(cooked);
        }


        hist.AddUserMessage(user);
        return hist;
    }
}