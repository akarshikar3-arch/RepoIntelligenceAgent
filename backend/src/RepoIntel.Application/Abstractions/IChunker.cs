using RepoIntel.Domain.Code;

namespace RepoIntel.Application.Abstractions;

public interface IChunker
{
    Task<IReadOnlyList<CodeChunk>> ChunkAsync(
        string sessionId,
        string rootPath,
        IReadOnlyList<CodeFile> files,
        CancellationToken ct = default);
}
