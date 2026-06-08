using Microsoft.AspNetCore.Mvc;
using RepoIntel.Contracts;

namespace RepoIntel.Api.Controllers;

[ApiController]
[Route("api/sessions/{sessionId}")]
public sealed class ProgressController : ControllerBase
{
    /// <summary>SSE stream of pipeline progress events for a session.</summary>
    [HttpGet("progress")]
    public async Task Stream(string sessionId, CancellationToken ct)
    {
        Response.Headers.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";

        // Real subscription via IProgressBroadcaster lands in P3.
        await Response.WriteAsync($": connected {sessionId}\n\n", ct);
        await Response.Body.FlushAsync(ct);

        try { await Task.Delay(Timeout.Infinite, ct); }
        catch (OperationCanceledException) { /* client disconnected */ }
    }
}
