using RepoIntel.Contracts;

namespace RepoIntel.Application.Abstractions;

/// <summary>Multi-subscriber broadcaster for pipeline progress per session (SSE source).</summary>
public interface IProgressBroadcaster
{
    ValueTask PublishAsync(string sessionId, PipelineProgressEvent evt, CancellationToken ct = default);
    IAsyncEnumerable<PipelineProgressEvent> SubscribeAsync(string sessionId, CancellationToken ct = default);
}
