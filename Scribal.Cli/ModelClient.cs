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

public class ModelClient(IChatClient client, IConversationStore conversationStore) : IModelClient
{
    [Description("Gets the weather")]
    private string GetWeather() => Random.Shared.NextDouble() > 0.5 ? "It's sunny" : "It's raining";

    [Description("Applies a complete edit to a file, overwriting its current content. Provide the relative file path and the full new content.")]
    private async Task<string> ApplyFileEditAsync(
        [Description("The relative path to the file to be edited.")] string filePath,
        [Description("The full new content for the file.")] string newContent)
    {
        try
        {
            // Basic security check: prevent navigating up the directory tree too easily.
            // This is NOT foolproof security, but a basic safeguard.
            if (filePath.Contains(".."))
            {
                return $"Error: File path cannot contain '..'. Path provided: {filePath}";
            }

            // Consider making the path relative to a specific root directory if needed.
            // For now, assuming relative to the application's working directory.
            await File.WriteAllTextAsync(filePath, newContent);
            return $"Successfully applied edit to file: {filePath}";
        }
        catch (Exception ex)
        {
            // Log the exception details if logging is set up
            Console.Error.WriteLine($"Error applying file edit to {filePath}: {ex.Message}");
            return $"Error applying edit to file {filePath}: {ex.Message}";
        }
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetResponse(string input)
    {
        var chatOptions = new ChatOptions
        {
            Tools =
            [
                AIFunctionFactory.Create(GetWeather),
                AIFunctionFactory.Create(ApplyFileEditAsync) // Register the new tool
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
