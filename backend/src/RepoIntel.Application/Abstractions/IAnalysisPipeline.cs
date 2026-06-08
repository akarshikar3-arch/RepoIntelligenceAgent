using RepoIntel.Contracts;

namespace RepoIntel.Application.Abstractions;

/// <summary>Orchestrates the full analysis pipeline for a session.</summary>
public interface IAnalysisPipeline
{
    Task RunRepoAsync(string sessionId, RepoIngestRequest request, CancellationToken ct = default);
    Task RunSnippetAsync(string sessionId, SnippetIngestRequest request, CancellationToken ct = default);
}
