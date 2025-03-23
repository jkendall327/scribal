using System.ComponentModel;
using Microsoft.Extensions.AI;

namespace Scribal.Cli;

public interface IModelClient
{
    IAsyncEnumerable<ChatResponseUpdate> GetResponse(string input);
    void UpdateConversationHistory(ChatResponse response);
}

public class ModelClient(IChatClient client) : IModelClient
{
    [Description("Gets the weather")]
    private string GetWeather() => Random.Shared.NextDouble() > 0.5 ? "It's sunny" : "It's raining";

    private readonly List<ChatMessage> _conversation =
    [
        new(ChatRole.System, "You are a helpful AI assistant"),
    ];
    
    public IAsyncEnumerable<ChatResponseUpdate> GetResponse(string input)
    {
        var chatOptions = new ChatOptions
        {
            Tools = [AIFunctionFactory.Create(GetWeather)]
        };
        
        _conversation.Add(new(ChatRole.User, input));

        return client.GetStreamingResponseAsync(_conversation, chatOptions);
    }

    public void UpdateConversationHistory(ChatResponse response)
    {
        var newest = response.Messages.Last();
        _conversation.Add(newest);
    }
}