using RepoIntel.Contracts;

namespace RepoIntel.Application.Abstractions;

public interface IChatOrchestrator
{
    Task<ChatResponse> AskAsync(string sessionId, ChatRequest request, CancellationToken ct = default);
}
