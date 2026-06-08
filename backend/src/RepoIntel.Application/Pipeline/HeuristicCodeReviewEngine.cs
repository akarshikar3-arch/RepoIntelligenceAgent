using System.Text.RegularExpressions;
using RepoIntel.Domain.Code;
using RepoIntel.Domain.Insights;

namespace RepoIntel.Application.Pipeline;

internal static class HeuristicCodeReviewEngine
{
    private const double CriticalPenalty = 2.0;
    private const double WarningPenalty = 1.0;
    private const double InfoPenalty = 0.5;

    internal enum ReviewProfile
    {
        Strict,
        Balanced,
        Relaxed,
    }

    internal sealed record NoiseProfile(int MaxFindingsPerRule, bool SuppressLowNoiseInTests, double WarningWeight, double InfoWeight);

    internal sealed record ReviewResult(
        IReadOnlyList<Finding> Findings,
        double Score,
        int Critical,
        int Warning,
        int Info);

    public static ReviewResult Analyze(string rootPath, IReadOnlyList<CodeFile> files, string? reviewProfile)
    {
        var profile = ParseProfile(reviewProfile);
        var noise = GetNoiseProfile(profile);
        var findings = new List<Finding>();

        foreach (var file in files)
        {
            if (!LooksAnalyzable(file))
                continue;

            var fullPath = Path.Combine(rootPath, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath))
                continue;

            string src;
            try
            {
                src = File.ReadAllText(fullPath);
            }
            catch
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(src))
                continue;

            CheckSecurity(file, src, findings);
            CheckTypeSafety(file, src, findings);
            CheckAngularSpecific(file, src, findings);
            CheckTestingQuality(file, src, findings);
            CheckBestPractices(file, src, findings);
            CheckCodeStructure(file, src, findings);
        }

        findings = ApplyNoiseControls(findings, noise);

        // Ensure deterministic IDs and sort by severity first.
        var ordered = findings
            .OrderBy(f => SeverityRank(f.Severity))
            .ThenBy(f => f.File, StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f.Line)
            .Select((f, i) => new Finding
            {
                Id = $"R{i + 1:0000}",
                RuleId = f.RuleId,
                Severity = f.Severity,
                Category = f.Category,
                File = f.File,
                Line = f.Line,
                Column = f.Column,
                Message = f.Message,
                Snippet = f.Snippet,
                FixHint = f.FixHint,
            })
            .ToList();

        var critical = ordered.Count(f => f.Severity == Severity.Critical);
        var warning = ordered.Count(f => f.Severity == Severity.High || f.Severity == Severity.Medium);
        var info = ordered.Count(f => f.Severity == Severity.Low || f.Severity == Severity.Info);

        var issuePoints = (critical * CriticalPenalty)
            + (warning * WarningPenalty * noise.WarningWeight)
            + (info * InfoPenalty * noise.InfoWeight);
        var normalization = Math.Max(8d, Math.Sqrt(Math.Max(1, files.Count)) * 4d);
        var raw = 10d * Math.Exp(-issuePoints / normalization);
        var score = Math.Clamp(Math.Round(raw, 1), 0d, 10d);

        return new ReviewResult(ordered, score, critical, warning, info);
    }

    private static bool LooksAnalyzable(CodeFile file)
    {
        return file.Language is "typescript" or "javascript" or "csharp" or "php"
            || file.RelativePath.EndsWith(".spec.ts", StringComparison.OrdinalIgnoreCase)
            || file.RelativePath.EndsWith(".component.ts", StringComparison.OrdinalIgnoreCase)
            || file.RelativePath.EndsWith(".module.ts", StringComparison.OrdinalIgnoreCase);
    }

    private static void CheckSecurity(CodeFile file, string src, List<Finding> findings)
    {
        if (src.Contains("[innerHTML]", StringComparison.Ordinal))
        {
            findings.Add(NewFinding("SEC-INNERHTML", Severity.Critical, FindingCategory.Security, file.RelativePath,
                "[innerHTML] binding found -- XSS risk",
                "Use Angular DomSanitizer or avoid innerHTML.",
                src, "[innerHTML]"));
        }

        if (Regex.IsMatch(src,
                @"(ghp_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}|AKIA[0-9A-Z]{16}|sk_live_[A-Za-z0-9]{10,}|sk_test_[A-Za-z0-9]{10,}|xox[baprs]-[A-Za-z0-9-]{10,}|apikey\s*=\s*[\""'][^\""']{10,}[\""'])",
                RegexOptions.IgnoreCase))
        {
            findings.Add(NewFinding("SEC-HARDCODED-SECRET", Severity.Critical, FindingCategory.Security, file.RelativePath,
                "Hardcoded API key or secret detected",
                "Move secrets to environment variables or a secure vault.",
                src, "apikey"));
        }

        if (src.Contains("http://", StringComparison.Ordinal)
            && !src.Contains("localhost", StringComparison.OrdinalIgnoreCase)
            && !src.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(NewFinding("SEC-HTTP-URL", Severity.Medium, FindingCategory.Security, file.RelativePath,
                "HTTP (non-secure) URL found in code",
                "Prefer HTTPS for external endpoints.",
                src, "http://"));
        }
    }

    private static void CheckTypeSafety(CodeFile file, string src, List<Finding> findings)
    {
        if (file.Language != "typescript")
            return;

        var normalizedPath = file.RelativePath.Replace('\\', '/');
        var isDeclarationFile = normalizedPath.EndsWith(".d.ts", StringComparison.OrdinalIgnoreCase);

        // Skip external dependency type definitions to avoid noisy/non-actionable findings.
        if (normalizedPath.Contains("/node_modules/", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.Contains("/@types/", StringComparison.OrdinalIgnoreCase))
            return;

        var isTestOrMock = IsTestOrMockFile(file.RelativePath);
        var anyCount = CountOccurrences(src, ": any") + CountOccurrences(src, "<any>") + CountOccurrences(src, "as any");
        if (anyCount > 0)
        {
            // `any` is type-system debt, not a production blocker.
            // Single-use `any` in normal source is usually minor debt; repeated usage is medium.
            var severity = (isTestOrMock || isDeclarationFile || anyCount <= 1) ? Severity.Low : Severity.Medium;

            var message = anyCount == 1
                ? "TypeScript 'any' used once -- reduces type safety at that location"
                : $"TypeScript 'any' used {anyCount} times -- reduces type safety and refactor confidence";

            var fixHint = anyCount == 1
                ? "Replace 'any' with a concrete interface/type (or 'unknown' at boundaries) for safer usage."
                : "Replace repeated 'any' with concrete interfaces/types or generics; use 'unknown' only at untrusted boundaries.";

            var needle = src.Contains(": any", StringComparison.Ordinal)
                ? ": any"
                : src.Contains(" as any", StringComparison.Ordinal)
                    ? " as any"
                    : "<any>";

            findings.Add(NewFinding("TS-ANY", severity, FindingCategory.Quality, file.RelativePath,
                message,
                fixHint,
                src, needle));
        }

        if (Regex.IsMatch(src, @"function\s+\w+\s*\([^)]*\)\s*\{")
            && !Regex.IsMatch(src, @"function\s+\w+\s*\([^)]*\)\s*:\s*"))
        {
            findings.Add(NewFinding("TS-MISSING-RETURN", Severity.Low, FindingCategory.Quality, file.RelativePath,
                "TypeScript function without explicit return type",
                "Add explicit return types for better readability and safety.",
                src, "function"));
        }
    }

    private static void CheckAngularSpecific(CodeFile file, string src, List<Finding> findings)
    {
        if (file.RelativePath.EndsWith(".component.ts", StringComparison.OrdinalIgnoreCase)
            && src.Contains("@Component", StringComparison.Ordinal)
            && !src.Contains("standalone: true", StringComparison.Ordinal)
            && !IsTestOrMockFile(file.RelativePath))
        {
            findings.Add(NewFinding("NG-STANDALONE", Severity.Low, FindingCategory.Quality, file.RelativePath,
                "Component not using standalone: true (Angular 17+ recommended)",
                "Add standalone: true in @Component decorator.",
                src, "@Component"));
        }

        var path = file.RelativePath.Replace('\\', '/');
        var isRoutingOrModuleFile = path.EndsWith("-routing.module.ts", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".module.ts", StringComparison.OrdinalIgnoreCase);

        if (isRoutingOrModuleFile
            && !IsTestOrMockFile(file.RelativePath)
            && Regex.IsMatch(src, @"import\s+\{?[^;\n]*Module[^;\n]*from", RegexOptions.IgnoreCase)
            && !src.Contains("loadChildren", StringComparison.Ordinal)
            && !src.Contains("loadComponent", StringComparison.Ordinal))
        {
            findings.Add(NewFinding("NG-EAGER-MODULE", Severity.Low, FindingCategory.Quality, file.RelativePath,
                "Eager module imports detected without lazy loading",
                "Prefer loadChildren/loadComponent where applicable.",
                src, "Module"));
        }
    }

    private static void CheckTestingQuality(CodeFile file, string src, List<Finding> findings)
    {
        if (!file.RelativePath.EndsWith(".spec.ts", StringComparison.OrdinalIgnoreCase))
            return;

        if ((src.Contains("describe(", StringComparison.Ordinal) || src.Contains("it(", StringComparison.Ordinal))
            && !src.Contains("throwError", StringComparison.Ordinal)
            && !src.Contains("toThrow", StringComparison.Ordinal)
            && !src.Contains(".error", StringComparison.Ordinal))
        {
            findings.Add(NewFinding("TEST-NO-ERROR-PATH", Severity.Low, FindingCategory.Testing, file.RelativePath,
                "No error scenario testing detected",
                "Add tests for failure/error paths.",
                src, "describe("));
        }
    }

    private static void CheckBestPractices(CodeFile file, string src, List<Finding> findings)
    {
        var lineCount = CountLines(src);
        var fnCount = CountOccurrences(src, "function ") + CountPattern(src, @"\w+\s*\([^)]*\)\s*\{");
        if (lineCount > 120 && fnCount <= 2 && !IsTestOrMockFile(file.RelativePath))
        {
            findings.Add(NewFinding("BP-LONG-FUNCTION", Severity.Low, FindingCategory.Quality, file.RelativePath,
                "Possible long function(s) -- few function definitions for the file size",
                "Break logic into smaller focused functions.",
                src, "function"));
        }
    }

    private static void CheckCodeStructure(CodeFile file, string src, List<Finding> findings)
    {
        if ((src.Contains("constructor(private", StringComparison.Ordinal)
             || src.Contains("constructor( private", StringComparison.Ordinal))
            && !src.Contains("private readonly", StringComparison.Ordinal))
        {
            findings.Add(NewFinding("STRUCT-READONLY", Severity.Low, FindingCategory.Refactor, file.RelativePath,
                "Injected services not marked as readonly",
                "Use private readonly for injected dependencies.",
                src, "constructor(private"));
        }

        if (Regex.IsMatch(src, @"[a-z]+_[a-z]+") && Regex.IsMatch(src, @"[a-z]+[A-Z]"))
        {
            findings.Add(NewFinding("STRUCT-NAMING", Severity.Low, FindingCategory.Refactor, file.RelativePath,
                "Mix of snake_case and camelCase naming detected",
                "Use consistent naming convention (camelCase in TypeScript).",
                src, "_"));
        }
    }

    private static Finding NewFinding(
        string ruleId,
        Severity severity,
        FindingCategory category,
        string file,
        string message,
        string fix,
        string source,
        string needle)
    {
        var line = GuessLineNumber(source, needle);
        return new Finding
        {
            Id = string.Empty,
            RuleId = ruleId,
            Severity = severity,
            Category = category,
            File = file,
            Line = line,
            Column = 1,
            Message = message,
            Snippet = null,
            FixHint = fix,
        };
    }

    private static int GuessLineNumber(string src, string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return 1;
        var idx = src.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return 1;
        var line = 1;
        for (var i = 0; i < idx; i++)
        {
            if (src[i] == '\n') line++;
        }
        return line;
    }

    private static int CountOccurrences(string text, string token)
    {
        var count = 0;
        var start = 0;
        while (true)
        {
            var idx = text.IndexOf(token, start, StringComparison.Ordinal);
            if (idx < 0) return count;
            count++;
            start = idx + token.Length;
        }
    }

    private static int CountPattern(string text, string pattern) => Regex.Matches(text, pattern).Count;

    private static ReviewProfile ParseProfile(string? reviewProfile)
    {
        if (string.IsNullOrWhiteSpace(reviewProfile))
            return ReviewProfile.Balanced;

        return reviewProfile.Trim().ToLowerInvariant() switch
        {
            "strict" => ReviewProfile.Strict,
            "relaxed" => ReviewProfile.Relaxed,
            _ => ReviewProfile.Balanced,
        };
    }

    private static NoiseProfile GetNoiseProfile(ReviewProfile profile) => profile switch
    {
        ReviewProfile.Strict => new NoiseProfile(MaxFindingsPerRule: 40, SuppressLowNoiseInTests: false, WarningWeight: 1.15, InfoWeight: 1.0),
        ReviewProfile.Relaxed => new NoiseProfile(MaxFindingsPerRule: 12, SuppressLowNoiseInTests: true, WarningWeight: 0.85, InfoWeight: 0.4),
        _ => new NoiseProfile(MaxFindingsPerRule: 20, SuppressLowNoiseInTests: false, WarningWeight: 1.0, InfoWeight: 1.0),
    };

    private static bool IsTestOrMockFile(string relativePath)
    {
        var p = relativePath.Replace('\\', '/');
        return p.EndsWith(".spec.ts", StringComparison.OrdinalIgnoreCase)
            || p.EndsWith(".test.ts", StringComparison.OrdinalIgnoreCase)
            || p.Contains("/mock-", StringComparison.OrdinalIgnoreCase)
            || p.Contains("/mocks/", StringComparison.OrdinalIgnoreCase)
            || p.Contains("/testing/", StringComparison.OrdinalIgnoreCase)
            || p.EndsWith("/test.ts", StringComparison.OrdinalIgnoreCase)
            || p.EndsWith("/polyfills.ts", StringComparison.OrdinalIgnoreCase);
    }

    private static List<Finding> ApplyNoiseControls(List<Finding> findings, NoiseProfile noise)
    {
        // Keep one finding per rule+file pair, then cap each rule to avoid floods.
        var deduped = findings
            .GroupBy(f => $"{f.RuleId}|{f.File}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderBy(f => f.Line).First())
            .ToList();

        if (noise.SuppressLowNoiseInTests)
        {
            deduped = deduped
                .Where(f => !(IsTestOrMockFile(f.File)
                              && f.Severity == Severity.Low
                              && (f.RuleId == "BP-LONG-FUNCTION"
                                  || f.RuleId == "NG-EAGER-MODULE"
                                  || f.RuleId == "TEST-NO-ERROR-PATH")))
                .ToList();
        }

        return deduped
            .GroupBy(f => f.RuleId, StringComparer.OrdinalIgnoreCase)
            .SelectMany(g => g
                .OrderBy(f => SeverityRank(f.Severity))
                .ThenBy(f => f.File, StringComparer.OrdinalIgnoreCase)
                .ThenBy(f => f.Line)
                .Take(noise.MaxFindingsPerRule))
            .ToList();
    }

    private static int CountLines(string src)
    {
        if (string.IsNullOrEmpty(src)) return 0;
        var lines = 1;
        foreach (var ch in src)
        {
            if (ch == '\n') lines++;
        }
        return lines;
    }

    private static int SeverityRank(Severity severity) => severity switch
    {
        Severity.Critical => 0,
        Severity.High => 1,
        Severity.Medium => 2,
        Severity.Low => 3,
        _ => 4,
    };
}
