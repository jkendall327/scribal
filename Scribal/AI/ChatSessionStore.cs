using System.Collections.Concurrent;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Scribal.Cli;

public interface IChatSessionStore
{
    Task<ChatHistory> LoadAsync(string conversationId, CancellationToken ct = default);
    Task SaveAsync(string conversationId, ChatHistory history, CancellationToken ct = default);
}

public sealed class InMemoryChatSessionStore : IChatSessionStore
{
    private readonly ConcurrentDictionary<string, ChatHistory> _store = new();

    public Task<ChatHistory> LoadAsync(string id, CancellationToken _)
    {
        var history = _store.GetOrAdd(id, _ => []);
        return Task.FromResult(history);
    }

    public Task SaveAsync(string id, ChatHistory h, CancellationToken _)
    {
        _store[id] = h;
        return Task.CompletedTask;
    }
}