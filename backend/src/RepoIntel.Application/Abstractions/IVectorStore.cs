using RepoIntel.Domain.Code;

namespace RepoIntel.Application.Abstractions;

public sealed record ScoredChunk(CodeChunk Chunk, double Score);

public interface IVectorStore
{
    Task UpsertAsync(string sessionId, IReadOnlyList<CodeChunk> chunks, CancellationToken ct = default);
    Task<IReadOnlyList<ScoredChunk>> SearchAsync(string sessionId, float[] queryEmbedding, int topK, CancellationToken ct = default);
    Task RemoveAsync(string sessionId, CancellationToken ct = default);
}
