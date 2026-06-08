using Microsoft.Extensions.Logging;
using RepoIntel.Application.Abstractions;
using RepoIntel.Contracts;
using RepoIntel.Domain.Sessions;

namespace RepoIntel.Application.Pipeline;

/// <summary>
/// Placeholder pipeline used until concrete stages (fetch/scan/analyze/embed/index)
/// are implemented. Marks the session as Ready so downstream APIs can be exercised.
/// </summary>
public sealed class NoopAnalysisPipeline : IAnalysisPipeline
{
    private readonly ISessionStore _sessions;
    private readonly ILogger<NoopAnalysisPipeline> _logger;

    public NoopAnalysisPipeline(ISessionStore sessions, ILogger<NoopAnalysisPipeline> logger)
    {
        _sessions = sessions;
        _logger = logger;
    }

    public Task RunRepoAsync(string sessionId, RepoIngestRequest request, CancellationToken ct = default)
        => RunAsync(sessionId, $"repo {request.Url}", ct);

    public Task RunSnippetAsync(string sessionId, SnippetIngestRequest request, CancellationToken ct = default)
        => RunAsync(sessionId, $"snippet ({request.Files.Count} file(s))", ct);

    private async Task RunAsync(string sessionId, string label, CancellationToken ct)
    {
        var session = await _sessions.GetAsync(sessionId, ct).ConfigureAwait(false);
        if (session is null)
        {
            _logger.LogWarning("Pipeline started for unknown session {SessionId}", sessionId);
            return;
        }

        _logger.LogInformation("Noop pipeline running for session {SessionId} ({Label})", sessionId, label);
        session.Status = SessionStatus.Running;
        await _sessions.SetAsync(session, ct).ConfigureAwait(false);

        session.Status = SessionStatus.Ready;
        await _sessions.SetAsync(session, ct).ConfigureAwait(false);
    }
}
