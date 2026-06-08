using System.Collections.Concurrent;
using RepoIntel.Application.Abstractions;
using RepoIntel.Contracts;

namespace RepoIntel.Infrastructure.Chat;

public sealed class InMemoryChatHistoryStore : IChatHistoryStore
{
    private readonly ConcurrentDictionary<string, List<ChatTurnDto>> _store = new();

    public Task<IReadOnlyList<ChatTurnDto>> GetAsync(string sessionId, CancellationToken ct = default)
    {
        if (_store.TryGetValue(sessionId, out var list))
        {
            lock (list)
            {
                IReadOnlyList<ChatTurnDto> snapshot = list.ToArray();
                return Task.FromResult(snapshot);
            }
        }
        return Task.FromResult<IReadOnlyList<ChatTurnDto>>(Array.Empty<ChatTurnDto>());
    }

    public Task AppendAsync(string sessionId, ChatTurnDto turn, CancellationToken ct = default)
    {
        var list = _store.GetOrAdd(sessionId, _ => new List<ChatTurnDto>());
        lock (list) { list.Add(turn); }
        return Task.CompletedTask;
    }

    public Task ClearAsync(string sessionId, CancellationToken ct = default)
    {
        _store.TryRemove(sessionId, out _);
        return Task.CompletedTask;
    }
}
