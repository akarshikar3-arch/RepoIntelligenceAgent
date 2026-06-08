using RepoIntel.Contracts;
using RepoIntel.Domain.Code;
using RepoIntel.Domain.Insights;
using RepoIntel.Domain.Sessions;

namespace RepoIntel.Api.Controllers;

internal static class FindingHeuristics
{
    private static readonly SeverityDto[] SeverityOrder =
    {
        SeverityDto.Critical,
        SeverityDto.High,
        SeverityDto.Medium,
        SeverityDto.Low,
        SeverityDto.Info,
    };

    public static IReadOnlyList<FindingDto> BuildFindings(RepoSession session)
    {
        if (session.Findings is { Count: > 0 })
        {
            return session.Findings
                .OrderBy(f => SeverityRank(ToDtoSeverity(f.Severity)))
                .ThenBy(f => f.File, StringComparer.OrdinalIgnoreCase)
                .ThenBy(f => f.Line)
                .Select((f, i) => new FindingDto(
                    Id: string.IsNullOrWhiteSpace(f.Id) ? $"F{i + 1:0000}" : f.Id,
                    RuleId: f.RuleId,
                    Severity: ToDtoSeverity(f.Severity),
                    Category: ToDtoCategory(f.Category),
                    File: f.File,
                    Line: f.Line,
                    Column: f.Column,
                    Message: f.Message,
                    Snippet: f.Snippet,
                    FixHint: f.FixHint))
                .ToList();
        }

        var snapshot = session.Analysis;
        if (snapshot is null)
            return Array.Empty<FindingDto>();

        var findings = new List<FindingDto>();
        var files = snapshot.Files;

        // Quality heuristics.
        var sourceCount = files.Count(f => IsCategory(f, "src"));
        var testCount = files.Count(f => IsCategory(f, "test"));

        if (sourceCount > 0 && testCount == 0)
        {
            findings.Add(NewFinding("Q-MISSING-TESTS", SeverityDto.High, "quality", "(repo)",
                "No test files were detected for source code.",
                "Add unit/integration tests for critical paths before merge."));
        }

        foreach (var file in files.Where(f => f.LineCount >= 900).Take(5))
        {
            findings.Add(NewFinding("Q-LARGE-FILE", SeverityDto.Medium, "quality", file.RelativePath,
                $"Large file detected ({file.LineCount} lines).",
                "Split into smaller modules/components to improve maintainability."));
        }

        if (!files.Any(f => IsCategory(f, "docs")))
        {
            findings.Add(NewFinding("Q-MISSING-DOCS", SeverityDto.Low, "quality", "(repo)",
                "No documentation files were detected.",
                "Add README and usage/contribution docs."));
        }

        // Security heuristics.
        foreach (var file in files.Where(f => LooksLikeSecretFile(f.RelativePath)).Take(5))
        {
            findings.Add(NewFinding("S-POTENTIAL-SECRET", SeverityDto.Critical, "security", file.RelativePath,
                "Potential secret-bearing file detected.",
                "Remove from source control and rotate credentials."));
        }

        if (!HasDependencyLockfile(files))
        {
            findings.Add(NewFinding("S-NO-LOCKFILE", SeverityDto.Low, "security", "(repo)",
                "No dependency lockfile detected.",
                "Commit a lockfile to improve supply-chain determinism."));
        }

        return findings
            .OrderBy(f => SeverityRank(f.Severity))
            .ThenBy(f => f.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f.File, StringComparer.OrdinalIgnoreCase)
            .Select((f, i) => f with { Id = $"F{i + 1:0000}" })
            .ToList();
    }

    public static IReadOnlyList<SeverityCount> SummarizeBySeverity(IReadOnlyList<FindingDto> findings)
    {
        var grouped = findings
            .GroupBy(f => f.Severity)
            .ToDictionary(g => g.Key, g => g.Count());

        return SeverityOrder
            .Select(s => new SeverityCount(s, grouped.TryGetValue(s, out var c) ? c : 0))
            .ToList();
    }

    private static FindingDto NewFinding(
        string ruleId,
        SeverityDto severity,
        string category,
        string file,
        string message,
        string? fixHint)
        => new(
            Id: string.Empty,
            RuleId: ruleId,
            Severity: severity,
            Category: category,
            File: file,
            Line: 1,
            Column: 1,
            Message: message,
            Snippet: null,
            FixHint: fixHint);

    private static int SeverityRank(SeverityDto s) => Array.IndexOf(SeverityOrder, s);

    private static bool IsCategory(CodeFile f, string category) =>
        string.Equals(f.Category, category, StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeSecretFile(string path)
    {
        var p = path.Replace('\\', '/').ToLowerInvariant();
        var name = Path.GetFileName(p);
        return name == ".env"
            || name.EndsWith(".pem", StringComparison.Ordinal)
            || name.EndsWith(".pfx", StringComparison.Ordinal)
            || name.EndsWith(".key", StringComparison.Ordinal)
            || p.Contains("secret")
            || p.Contains("apikey")
            || p.Contains("token");
    }

    private static bool HasDependencyLockfile(IReadOnlyList<CodeFile> files)
        => files.Any(f =>
            string.Equals(Path.GetFileName(f.RelativePath), "package-lock.json", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetFileName(f.RelativePath), "yarn.lock", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetFileName(f.RelativePath), "pnpm-lock.yaml", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetFileName(f.RelativePath), "packages.lock.json", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetFileName(f.RelativePath), "Pipfile.lock", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetFileName(f.RelativePath), "poetry.lock", StringComparison.OrdinalIgnoreCase));

    private static SeverityDto ToDtoSeverity(Severity severity) => severity switch
    {
        Severity.Critical => SeverityDto.Critical,
        Severity.High => SeverityDto.High,
        Severity.Medium => SeverityDto.Medium,
        Severity.Low => SeverityDto.Low,
        _ => SeverityDto.Info,
    };

    private static string ToDtoCategory(FindingCategory category) => category switch
    {
        FindingCategory.Security => "security",
        FindingCategory.Testing => "quality",
        FindingCategory.Refactor => "quality",
        FindingCategory.Performance => "quality",
        FindingCategory.Documentation => "quality",
        FindingCategory.Dependencies => "quality",
        FindingCategory.Architecture => "quality",
        _ => "quality",
    };
}
