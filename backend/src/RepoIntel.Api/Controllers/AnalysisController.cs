using Microsoft.AspNetCore.Mvc;
using RepoIntel.Application.Abstractions;
using RepoIntel.Contracts;
using RepoIntel.Domain.Architecture;

namespace RepoIntel.Api.Controllers;

[ApiController]
[Route("api/sessions/{sessionId}")]
[Produces("application/json")]
public sealed class AnalysisController : ControllerBase
{
    private readonly ISessionStore _sessions;

    public AnalysisController(ISessionStore sessions)
    {
        _sessions = sessions;
    }

    [HttpGet("architecture")]
    [ProducesResponseType(typeof(ArchitectureGraphDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ArchitectureGraphDto>> GetArchitecture(
        string sessionId,
        CancellationToken ct)
    {
        var session = await _sessions.GetAsync(sessionId, ct).ConfigureAwait(false);
        if (session is null) return NotFound();

        var graph = session.Architecture;
        if (graph is null)
        {
            // No graph yet — return an empty payload so the UI can render gracefully.
            return Ok(new ArchitectureGraphDto(Array.Empty<GraphNodeDto>(), Array.Empty<GraphEdgeDto>()));
        }

        return Ok(ToDto(graph));
    }

    [HttpGet("findings")]
    [ProducesResponseType(typeof(IReadOnlyList<FindingDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<FindingDto>>> GetFindings(
        string sessionId,
        [FromQuery] string? severity,
        [FromQuery] string? category,
        CancellationToken ct)
    {
        var session = await _sessions.GetAsync(sessionId, ct).ConfigureAwait(false);
        if (session is null) return NotFound();

        IEnumerable<FindingDto> findings = FindingHeuristics.BuildFindings(session);

        if (!string.IsNullOrWhiteSpace(category))
        {
            findings = findings.Where(f => string.Equals(f.Category, category, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(severity)
            && Enum.TryParse<SeverityDto>(severity, ignoreCase: true, out var sev))
        {
            findings = findings.Where(f => f.Severity == sev);
        }

        return Ok(findings.ToList());
    }

    [HttpGet("insights/{tab}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public Task<ActionResult<object>> GetInsights(string sessionId, string tab, CancellationToken ct)
        => Task.FromResult<ActionResult<object>>(NotFound());

    private static ArchitectureGraphDto ToDto(ArchitectureGraph g) =>
        new(
            g.Nodes
                .Select(n => new GraphNodeDto(n.Id, n.Label, n.Kind.ToString(), n.Metrics))
                .ToList(),
            g.Edges
                .Select(e => new GraphEdgeDto(e.From, e.To, e.Kind))
                .ToList());
}
