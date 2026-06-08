using RepoIntel.Domain.Code;

namespace RepoIntel.Application.Abstractions;

public sealed record ScanResult(
    string SessionId,
    string RootPath,
    IReadOnlyList<CodeFile> Files,
    int SkippedCount,
    long TotalBytes);

public interface IFileScanner
{
    Task<ScanResult> ScanAsync(string sessionId, string rootPath, CancellationToken ct = default);
}
