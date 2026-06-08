using RepoIntel.Application.Abstractions;

namespace RepoIntel.Infrastructure.Indexing;

public sealed class Retriever : IRetriever
{
    private readonly IEmbeddingProvider _embeddings;
    private readonly IVectorStore _store;

    public Retriever(IEmbeddingProvider embeddings, IVectorStore store)
    {
        _embeddings = embeddings;
        _store = store;
    }

    public async Task<IReadOnlyList<ScoredChunk>> RetrieveAsync(
        string sessionId,
        string query,
        int topK = 6,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Array.Empty<ScoredChunk>();

        var emb = await _embeddings.EmbedBatchAsync(new[] { query }, ct).ConfigureAwait(false);
        if (emb.Length == 0) return Array.Empty<ScoredChunk>();

        return await _store.SearchAsync(sessionId, emb[0], topK, ct).ConfigureAwait(false);
    }
}
