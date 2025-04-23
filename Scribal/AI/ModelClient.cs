using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

namespace Scribal.Cli;

public interface IModelClient
{
    IAsyncEnumerable<ChatResponseUpdate> GetResponse(string input);
    void UpdateConversationHistory(ChatResponse response);
}

public class ModelClient(IChatClient client, DiffService diffService, IConversationStore conversationStore)
    : IModelClient
{
    public IAsyncEnumerable<ChatResponseUpdate> GetResponse(string input)
    {
        var chatOptions = new ChatOptions
        {
            Tools =
            [
                AIFunctionFactory.Create(diffService.ApplyUnifiedDiffAsync)
            ]
        };

        conversationStore.AddUserMessage(input);

        return client.GetStreamingResponseAsync(conversationStore.GetConversation(), chatOptions);
    }

    public void UpdateConversationHistory(ChatResponse response)
    {
        var newest = response.Messages.Last();
        conversationStore.AddAssistantMessage(newest);
    }
}