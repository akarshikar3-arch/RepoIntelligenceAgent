namespace RepoIntel.Application.Abstractions;

public interface IRetriever
{
    Task<IReadOnlyList<ScoredChunk>> RetrieveAsync(
        string sessionId,
        string query,
        int topK = 6,
        CancellationToken ct = default);
}
