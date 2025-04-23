using Microsoft.Extensions.AI;

namespace Scribal.Cli;

public interface IConversationStore
{
    List<ChatMessage> GetConversation();
    void AddUserMessage(string message);
    void AddAssistantMessage(ChatMessage message);
}

public class ConversationStore : IConversationStore
{
    private readonly List<ChatMessage> _conversation =
    [
        new(ChatRole.System, PromptBuilder.SystemPrompt),
    ];
    
    public List<ChatMessage> GetConversation()
    {
        return _conversation;
    }
    
    public void AddUserMessage(string message)
    {
        _conversation.Add(new(ChatRole.User, message));
    }
    
    public void AddAssistantMessage(ChatMessage message)
    {
        _conversation.Add(message);
    }

    public void ClearConversation()
    {
        _conversation.Clear();
        _conversation.Add(new(ChatRole.System, "You are a helpful AI assistant"));
    }
}
