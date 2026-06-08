using System.Collections.Concurrent;
using RepoIntel.Application.Abstractions;
using RepoIntel.Domain.Sessions;

namespace RepoIntel.Infrastructure.Sessions;

public sealed class InMemorySessionStore : ISessionStore
{
    private readonly ConcurrentDictionary<string, RepoSession> _sessions = new(StringComparer.Ordinal);

    public Task<RepoSession?> GetAsync(string sessionId, CancellationToken ct = default)
        => Task.FromResult(_sessions.TryGetValue(sessionId, out var s) ? s : null);

    public Task SetAsync(RepoSession session, CancellationToken ct = default)
    {
        _sessions[session.Id] = session;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string sessionId, CancellationToken ct = default)
    {
        _sessions.TryRemove(sessionId, out _);
        return Task.CompletedTask;
    }
}
