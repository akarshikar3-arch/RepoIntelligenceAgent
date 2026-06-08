namespace RepoIntel.Domain.Sessions;

public enum SessionKind { Repo, Snippet }

public enum SessionStatus { Pending, Running, Ready, Error }

public sealed class RepoSession
{
    public required string Id { get; init; }
    public required SessionKind Kind { get; init; }
    public required string Title { get; set; }
    public string? Framework { get; set; }
    public string? PrimaryLanguage { get; set; }
    public int? HealthScore { get; set; }
    public int? ConfidenceScore { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public SessionStatus Status { get; set; } = SessionStatus.Pending;
    public string? WorkingDirectory { get; set; }
    public string? Error { get; set; }

    /// <summary>Architecture graph produced by the analysis pipeline (set when ready).</summary>
    public Architecture.ArchitectureGraph? Architecture { get; set; }

    /// <summary>Code-review findings generated from source analysis.</summary>
    public IReadOnlyList<Insights.Finding> Findings { get; set; } = Array.Empty<Insights.Finding>();

    /// <summary>Snapshot of scanned files + framework metadata used to render dashboards.</summary>
    public RepoAnalysisSnapshot? Analysis { get; set; }
}
