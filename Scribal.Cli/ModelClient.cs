using System.ComponentModel;
using Microsoft.Extensions.AI;

namespace Scribal.Cli;

public interface IModelClient
{
    IAsyncEnumerable<ChatResponseUpdate> GetResponse(string input);
    void UpdateConversationHistory(ChatResponse response);
}

public class ModelClient(IChatClient client, IConversationStore conversationStore) : IModelClient
{
    [Description("Gets the weather")]
    private string GetWeather() => Random.Shared.NextDouble() > 0.5 ? "It's sunny" : "It's raining";
    
    public IAsyncEnumerable<ChatResponseUpdate> GetResponse(string input)
    {
        var chatOptions = new ChatOptions
        {
            Tools = [AIFunctionFactory.Create(GetWeather)]
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
