using System.Collections.Concurrent;
using RepoIntel.Application.Abstractions;
using RepoIntel.Domain.Code;

namespace RepoIntel.Infrastructure.Indexing;

public sealed class InMemoryVectorStore : IVectorStore
{
    private readonly ConcurrentDictionary<string, List<CodeChunk>> _bySession = new();

    public Task UpsertAsync(string sessionId, IReadOnlyList<CodeChunk> chunks, CancellationToken ct = default)
    {
        _bySession[sessionId] = chunks.Where(c => c.Embedding is { Length: > 0 }).ToList();
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ScoredChunk>> SearchAsync(
        string sessionId,
        float[] queryEmbedding,
        int topK,
        CancellationToken ct = default)
    {
        if (!_bySession.TryGetValue(sessionId, out var chunks) || chunks.Count == 0)
            return Task.FromResult<IReadOnlyList<ScoredChunk>>(Array.Empty<ScoredChunk>());

        var scored = new List<ScoredChunk>(chunks.Count);
        foreach (var chunk in chunks)
        {
            var score = Cosine(queryEmbedding, chunk.Embedding!);
            scored.Add(new ScoredChunk(chunk, score));
        }

        IReadOnlyList<ScoredChunk> top = scored
            .OrderByDescending(s => s.Score)
            .Take(Math.Max(1, topK))
            .ToList();
        return Task.FromResult(top);
    }

    public Task RemoveAsync(string sessionId, CancellationToken ct = default)
    {
        _bySession.TryRemove(sessionId, out _);
        return Task.CompletedTask;
    }

    private static double Cosine(float[] a, float[] b)
    {
        if (a.Length != b.Length) return 0;
        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            na += a[i] * a[i];
            nb += b[i] * b[i];
        }
        if (na == 0 || nb == 0) return 0;
        return dot / (Math.Sqrt(na) * Math.Sqrt(nb));
    }
}
