namespace RepoIntel.Domain.Insights;

public enum Severity { Info, Low, Medium, High, Critical }

public enum FindingCategory
{
    Quality,
    Security,
    Performance,
    Testing,
    Documentation,
    Dependencies,
    Architecture,
    Refactor
}

public sealed class Finding
{
    public required string Id { get; init; }
    public required string RuleId { get; init; }
    public required Severity Severity { get; init; }
    public required FindingCategory Category { get; init; }
    public required string File { get; init; }
    public int Line { get; init; }
    public int Column { get; init; }
    public required string Message { get; init; }
    public string? Snippet { get; init; }
    public string? FixHint { get; init; }
}
