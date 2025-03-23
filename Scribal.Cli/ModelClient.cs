using System.ComponentModel;
using Microsoft.Extensions.AI;

namespace Scribal.Cli;

public interface IModelClient
{
    IAsyncEnumerable<ChatResponseUpdate> GetResponse(string input);
}

public class ModelClient(IChatClient client) : IModelClient
{
    [Description("Gets the weather")]
    private string GetWeather() => Random.Shared.NextDouble() > 0.5 ? "It's sunny" : "It's raining";

    public IAsyncEnumerable<ChatResponseUpdate> GetResponse(string input)
    {
        var chatOptions = new ChatOptions
        {
            Tools = [AIFunctionFactory.Create(GetWeather)]
        };

        return client.GetStreamingResponseAsync("input", chatOptions);
    }
}