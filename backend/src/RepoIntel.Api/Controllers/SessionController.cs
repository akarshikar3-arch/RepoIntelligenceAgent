using Microsoft.AspNetCore.Mvc;
using RepoIntel.Application.Abstractions;
using RepoIntel.Contracts;
using RepoIntel.Domain.Sessions;

namespace RepoIntel.Api.Controllers;

[ApiController]
[Route("api/sessions/{sessionId}")]
[Produces("application/json")]
public sealed class SessionController : ControllerBase
{
    private readonly ISessionStore _sessions;

    public SessionController(ISessionStore sessions)
    {
        _sessions = sessions;
    }

    [HttpGet]
    [ProducesResponseType(typeof(SessionSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SessionSummaryDto>> Get(string sessionId, CancellationToken ct)
    {
        var s = await _sessions.GetAsync(sessionId, ct).ConfigureAwait(false);
        if (s is null) return NotFound();
        return Ok(ToSummary(s));
    }

    [HttpGet("metrics")]
    [ProducesResponseType(typeof(DashboardMetricsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DashboardMetricsDto>> GetMetrics(string sessionId, CancellationToken ct)
    {
        var s = await _sessions.GetAsync(sessionId, ct).ConfigureAwait(false);
        if (s is null) return NotFound();
        var findings = FindingHeuristics.BuildFindings(s);
        return Ok(BuildMetrics(s, findings));
    }

    [HttpGet("overview")]
    [ProducesResponseType(typeof(DashboardOverviewDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DashboardOverviewDto>> GetOverview(string sessionId, CancellationToken ct)
    {
        var s = await _sessions.GetAsync(sessionId, ct).ConfigureAwait(false);
        if (s is null) return NotFound();

        var summary = ToSummary(s);
        var findings = FindingHeuristics.BuildFindings(s);
        var metrics = BuildMetrics(s, findings);

        return Ok(new DashboardOverviewDto(summary, metrics, findings));
    }

    [HttpGet("tree")]
    [ProducesResponseType(typeof(FileTreeNodeDto), StatusCodes.Status200OK)]
    public Task<ActionResult<FileTreeNodeDto>> GetTree(string sessionId, CancellationToken ct)
        => Task.FromResult<ActionResult<FileTreeNodeDto>>(NotFound());

    [HttpGet("files/{*path}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public Task<ActionResult<string>> GetFile(string sessionId, string path, CancellationToken ct)
        => Task.FromResult<ActionResult<string>>(NotFound());

    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public Task<IActionResult> Delete(string sessionId, CancellationToken ct)
        => Task.FromResult<IActionResult>(NoContent());

    private static SessionSummaryDto ToSummary(RepoSession s) =>
        new(
            s.Id,
            s.Kind == SessionKind.Repo ? SessionKindDto.Repo : SessionKindDto.Snippet,
            s.Title,
            s.Framework,
            s.PrimaryLanguage,
            s.HealthScore,
            s.ConfidenceScore,
            s.CreatedAt,
            s.Status switch
            {
                SessionStatus.Pending => SessionStatusDto.Pending,
                SessionStatus.Running => SessionStatusDto.Running,
                SessionStatus.Ready => SessionStatusDto.Ready,
                SessionStatus.Error => SessionStatusDto.Error,
                _ => SessionStatusDto.Pending,
            },
            s.Error);

    private static DashboardMetricsDto BuildMetrics(
        RepoSession s,
        IReadOnlyList<FindingDto> findings)
    {
        var findingsBySeverity = FindingHeuristics.SummarizeBySeverity(findings);

        var snapshot = s.Analysis;
        if (snapshot is null)
        {
            return new DashboardMetricsDto(
                TotalFiles: 0,
                TotalFolders: 0,
                FileTypes: Array.Empty<FileTypeBucket>(),
                Languages: Array.Empty<LanguageBucket>(),
                Categories: new CategoryBreakdown(0, 0, 0, 0),
                Angular: null,
                DotNet: null,
                FindingsBySeverity: findingsBySeverity,
                LargestFiles: Array.Empty<LargestFile>());
        }

        var files = snapshot.Files;

        var fileTypes = files
            .GroupBy(f => GetExt(f.RelativePath))
            .Select(g => new FileTypeBucket(g.Key, g.Count(), g.Sum(x => x.SizeBytes)))
            .OrderByDescending(b => b.Count)
            .Take(20)
            .ToList();

        var languages = files
            .GroupBy(f => f.Language)
            .Select(g => new LanguageBucket(g.Key, g.Count(), g.Sum(x => x.SizeBytes)))
            .OrderByDescending(b => b.Files)
            .ToList();

        var categories = new CategoryBreakdown(
            Source: files.Count(f => f.Category == "src"),
            Test: files.Count(f => f.Category == "test"),
            Config: files.Count(f => f.Category == "config"),
            Docs: files.Count(f => f.Category == "docs"));

        var folderCount = files
            .Select(f =>
            {
                var idx = f.RelativePath.LastIndexOf('/');
                return idx > 0 ? f.RelativePath[..idx] : string.Empty;
            })
            .Where(p => p.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        AngularBreakdown? angular = null;
        if (snapshot.Angular is not null)
        {
            angular = new AngularBreakdown(
                Components: files.Count(f => f.RelativePath.EndsWith(".component.ts", StringComparison.OrdinalIgnoreCase)),
                Services: files.Count(f => f.RelativePath.EndsWith(".service.ts", StringComparison.OrdinalIgnoreCase)),
                Modules: files.Count(f => f.RelativePath.EndsWith(".module.ts", StringComparison.OrdinalIgnoreCase)),
                Guards: files.Count(f => f.RelativePath.EndsWith(".guard.ts", StringComparison.OrdinalIgnoreCase)),
                Interceptors: files.Count(f => f.RelativePath.EndsWith(".interceptor.ts", StringComparison.OrdinalIgnoreCase)),
                Pipes: files.Count(f => f.RelativePath.EndsWith(".pipe.ts", StringComparison.OrdinalIgnoreCase)));
        }

        DotNetBreakdown? dotnet = null;
        if (snapshot.DotNet is not null)
        {
            dotnet = new DotNetBreakdown(
                Controllers: files.Count(f => f.RelativePath.EndsWith("Controller.cs", StringComparison.OrdinalIgnoreCase)),
                Services: files.Count(f => f.RelativePath.EndsWith("Service.cs", StringComparison.OrdinalIgnoreCase)),
                Entities: files.Count(f => f.RelativePath.EndsWith("Entity.cs", StringComparison.OrdinalIgnoreCase)),
                Repositories: files.Count(f => f.RelativePath.EndsWith("Repository.cs", StringComparison.OrdinalIgnoreCase)));
        }

        var largest = files
            .OrderByDescending(f => f.SizeBytes)
            .Take(10)
            .Select(f => new LargestFile(f.RelativePath, f.SizeBytes, f.LineCount))
            .ToList();

        return new DashboardMetricsDto(
            TotalFiles: files.Count,
            TotalFolders: folderCount,
            FileTypes: fileTypes,
            Languages: languages,
            Categories: categories,
            Angular: angular,
            DotNet: dotnet,
            FindingsBySeverity: findingsBySeverity,
            LargestFiles: largest);
    }

    private static string GetExt(string path)
    {
        var idx = path.LastIndexOf('.');
        if (idx < 0 || idx == path.Length - 1) return "(none)";
        var slash = path.LastIndexOf('/');
        if (slash > idx) return "(none)";
        return path[idx..].ToLowerInvariant();
    }
}
