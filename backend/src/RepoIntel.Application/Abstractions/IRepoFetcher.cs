namespace RepoIntel.Application.Abstractions;

public sealed record LocalRepoHandle(string SessionId, string WorkingDirectory, string? Branch, string? CommitSha) : IDisposable
{
    public void Dispose()
    {
        try { if (Directory.Exists(WorkingDirectory)) Directory.Delete(WorkingDirectory, recursive: true); }
        catch { /* best-effort cleanup */ }
    }
}

public interface IRepoFetcher
{
    Task<LocalRepoHandle> FetchAsync(string sessionId, string url, string? branch, CancellationToken ct = default);
}
