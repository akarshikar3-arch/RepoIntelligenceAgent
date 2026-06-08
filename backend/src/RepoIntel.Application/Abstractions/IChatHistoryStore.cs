using RepoIntel.Contracts;

namespace RepoIntel.Application.Abstractions;

public interface IChatHistoryStore
{
    Task<IReadOnlyList<ChatTurnDto>> GetAsync(string sessionId, CancellationToken ct = default);
    Task AppendAsync(string sessionId, ChatTurnDto turn, CancellationToken ct = default);
    Task ClearAsync(string sessionId, CancellationToken ct = default);
}
