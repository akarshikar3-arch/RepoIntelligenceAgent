using RepoIntel.Domain.Architecture;
using RepoIntel.Domain.Code;

namespace RepoIntel.Application.Abstractions;

public interface IArchitectureGraphBuilder
{
    Task<ArchitectureGraph> BuildAsync(
        string sessionId,
        string rootPath,
        IReadOnlyList<CodeFile> files,
        CancellationToken ct = default);
}
