using RepoIntel.Domain.Sessions;

namespace RepoIntel.Application.Abstractions;

public interface ISessionStore
{
    Task<RepoSession?> GetAsync(string sessionId, CancellationToken ct = default);
    Task SetAsync(RepoSession session, CancellationToken ct = default);
    Task RemoveAsync(string sessionId, CancellationToken ct = default);
}
