using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using RepoIntel.Application.Abstractions;
using RepoIntel.Contracts;

namespace RepoIntel.Application.Chat;

/// <summary>
/// Retrieval-first chat flow. Embeds the question, retrieves top-K chunks from the
/// per-session vector store, builds a grounded prompt and forwards it to the LLM.
/// All answers are grounded; if no context is found we short-circuit instead of
/// asking the LLM at all.
/// </summary>
public sealed class ChatOrchestrator : IChatOrchestrator
{
    private const int MaxLlmAttempts = 3;
    private const int TopK = 8;
    private const int UsageTopK = 12;
    private const double MinChunkScore = 0.05;
    private const double UsageMinChunkScore = 0.02;
    private static readonly Regex FileCountQuestion = new(
        @"\b(how many|hw many|number of|count|counts)\b.*\bfiles?\b|\bfiles?\b.*\b(how many|hw many|number of|count|counts|r there)\b|\bfile\s*breakdown\b|\bbreakdown\b.*\bfile\b|\bfile\s*counts?\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex MostChangesQuestion = new(
        @"\bmost\b.*\b(change|changes|changed)\b|\b(changed|change|changes)\b.*\bmost\b|\bchanges?\b.*\bmost\b|\bmost\b.*\bwork\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex LargestFileQuestion = new(
        @"\b(largest|biggest|longest)\b.*\b(files?|source file|code file)\b|\b(files?|source file|code file)\b.*\b(largest|biggest|longest)\b|\btop\s*\d*\s*(largest|biggest)\s*files?\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CriticalityQuestion = new(
        @"\b(criticality|criticalities|criticals?|critical issues?|critical findings?|most critical|highest critical|highest risk|riskiest|high risk)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CriticalFindingsQuestion = new(
        @"\bshow\b.*\bcritical(s| issues?| findings?)\b|\blist\b.*\bcritical(s| issues?| findings?)\b|\bcritical(s| issues?| findings?)\b.*\b(show|list|only|any)\b|\bany\b.*\bcritical(s| issues?| findings?)\b|\bcritical(s| issues?| findings?)\b\s*\?*$|\bcriticals\s+(plz|pls|please)\b|\bshow\s+critical\s+only\b|\bonly\s+critical(s| issues?| findings?)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex WarningsQuestion = new(
        @"\b(warnings?|warning issues?|show .*warnings?|only warnings?|warn me|warning only|show me only warnings?)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RiskyFilesQuestion = new(
        @"\b(top\s*\d*\s*)?risky files?\b|\bhighest[- ]risk files?\b|\bwhich files?\b.*\brisky\b|\brisky\b.*\bfiles?\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SecurityFindingsQuestion = new(
        @"\b(security findings|security issues|security only|only security|show .*security|sec findings?|sec issues?|secret leaks? detected|leaked secrets?|creds|credentials|tokens?)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TsAnyFilesQuestion = new(
        @"\b(which files?|files?)\b.*\b(any type|ts-any|: any|as any)\b|\b(any type|ts-any)\b.*\bmost\b|\bts-any\s*hotspots?\b|\bhotspots?\b.*\bts-any\b|\bwhere\b.*\busing\b.*\bany\b.*\bmost\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex OffTopicQuestion = new(
        @"\b(world cup|who invented|what is\s*\d+\s*[+\-*/]\s*\d+|quantum physics|tell me a joke|joke)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RepoOverviewQuestion = new(
        @"\b(what'?s in this repo|what is in this repo|repo overview|repo summary|summarize this repo)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex DependencyQuestion = new(
        @"\b(show|list|what are)?\s*dependencies\b|\bdependency\s*(files|manifests?)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex GreetingQuestion = new(
        @"^\s*(hi|hello|hey)\s*[!.?]*\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HelpQuestion = new(
        @"^\s*(help|what can you do\??|how can you help\??)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TechStackQuestion = new(
        @"\b(tech stack|stack|technologies|what tech|what technologies|used here|used\?)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex FrontendBackendQuestion = new(
        @"\b(frontend|backend)\b.*\b(or|and|both)\b|\bis this frontend or backend\b|\bfrontend or backend\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex FrameworkQuestion = new(
        @"\bwhich framework|what framework|framework used|angular|react|vue|\.net|asp\.net\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ApiListQuestion = new(
        @"\b(list|show|what are|which are)\b.*\bapis?\b|\ball\s+apis\b|\bapi\s+surface\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex NewDeveloperQuestion = new(
        @"\b(explain this repo to a new developer|new developer|repo to a new developer|repo overview for a new developer)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ProductionReadinessQuestion = new(
        @"\b(before production|improve before production|production readiness|production ready|ship to production)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AuthQuestion = new(
        @"\b(login|logn|authentication|auth|signin|sign in|signup|sign up)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex MainFileQuestion = new(
        @"\b(main file|entry ?point|startup file|starting file|where .* start|which file .* start)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex UsageQuestion = new(
        @"\b(how to|how do i|operate|use|run|start|install|setup|quickstart|getting started|command|troubleshoot|debug)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly ISessionStore _sessions;
    private readonly IRetriever _retriever;
    private readonly ILlmProvider _llm;
    private readonly IChatHistoryStore _history;
    private readonly ILogger<ChatOrchestrator> _logger;

    public ChatOrchestrator(
        ISessionStore sessions,
        IRetriever retriever,
        ILlmProvider llm,
        IChatHistoryStore history,
        ILogger<ChatOrchestrator> logger)
    {
        _sessions = sessions;
        _retriever = retriever;
        _llm = llm;
        _history = history;
        _logger = logger;
    }

    public async Task<ChatResponse> AskAsync(string sessionId, ChatRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
            throw new ArgumentException("Question is required.", nameof(request));

        var normalizedQuestion = NormalizeQuestion(request.Question);

        if (OffTopicQuestion.IsMatch(normalizedQuestion))
        {
            const string msg = "I can't find that in the indexed repository.";
            await _history.AppendAsync(
                sessionId,
                new ChatTurnDto("assistant", msg, null),
                ct).ConfigureAwait(false);
            return new ChatResponse(msg, Array.Empty<ChatCitation>(), Grounded: false);
        }

        var session = await _sessions.GetAsync(sessionId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Session '{sessionId}' not found.");

        await _history.AppendAsync(
            sessionId,
            new ChatTurnDto("user", request.Question, null),
            ct).ConfigureAwait(false);

        var metadataAnswer = TryAnswerFromMetadata(normalizedQuestion, session);
        if (metadataAnswer is not null)
        {
            await _history.AppendAsync(
                sessionId,
                new ChatTurnDto("assistant", metadataAnswer, null),
                ct).ConfigureAwait(false);

            return new ChatResponse(metadataAnswer, Array.Empty<ChatCitation>(), Grounded: true);
        }

        var isUsageQuestion = UsageQuestion.IsMatch(normalizedQuestion);
        var hits = await RetrieveGroundedHitsAsync(sessionId, normalizedQuestion, isUsageQuestion, ct).ConfigureAwait(false);
        var minScore = isUsageQuestion ? UsageMinChunkScore : MinChunkScore;
        var grounded = hits.Where(h => h.Score >= minScore).ToList();

        var nonFixtureGrounded = grounded
            .Where(h => !IsFixturePath(h.Chunk.FilePath))
            .ToList();
        if (nonFixtureGrounded.Count > 0)
            grounded = nonFixtureGrounded;

        if (isUsageQuestion)
        {
            var opDocs = grounded
                .Where(h => IsOperationalUsageDocPath(h.Chunk.FilePath))
                .ToList();
            grounded = opDocs;
        }

        if (grounded.Count == 0)
        {
            const string msg = "I can't find that in the indexed repository.";
            var fallback = new ChatResponse(msg, Array.Empty<ChatCitation>(), Grounded: false);
            await _history.AppendAsync(
                sessionId,
                new ChatTurnDto("assistant", msg, null),
                ct).ConfigureAwait(false);
            return fallback;
        }

        var contextBlock = PromptBuilder.BuildContextBlock(grounded);
        var userPrompt = PromptBuilder.BuildUserPrompt(normalizedQuestion, contextBlock);

        var messages = new List<ChatMessage>
        {
            new("system", PromptBuilder.SystemPrompt),
        };

        if (request.History is { Count: > 0 })
        {
            foreach (var m in request.History.TakeLast(8))
            {
                if (m.Role is "user" or "assistant" && !string.IsNullOrWhiteSpace(m.Content))
                    messages.Add(new ChatMessage(m.Role, m.Content));
            }
        }

        messages.Add(new ChatMessage("user", userPrompt));

        try
        {
            var content = await GenerateLlmResponseAsync(messages, sessionId, ct).ConfigureAwait(false);
            if (string.IsNullOrEmpty(content))
                content = "(empty response from model)";

            if (string.Equals(content, "I can't find that in the indexed repository.", StringComparison.Ordinal))
            {
                await _history.AppendAsync(
                    sessionId,
                    new ChatTurnDto("assistant", content, null),
                    ct).ConfigureAwait(false);

                return new ChatResponse(content, Array.Empty<ChatCitation>(), Grounded: false);
            }

            var citations = grounded
                .Select(h => new ChatCitation(h.Chunk.FilePath, h.Chunk.StartLine, h.Chunk.EndLine, h.Score))
                .ToList();

            await _history.AppendAsync(
                sessionId,
                new ChatTurnDto("assistant", content, citations),
                ct).ConfigureAwait(false);

            return new ChatResponse(content, citations, Grounded: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LLM call failed for session {SessionId}", sessionId);
            var fallback = BuildUnavailableFallback(normalizedQuestion, session, grounded, IsRateLimitException(ex));
            await _history.AppendAsync(
                sessionId,
                new ChatTurnDto("assistant", fallback.Content, fallback.Citations.Count > 0 ? fallback.Citations : null),
                ct).ConfigureAwait(false);
            return fallback;
        }
    }

    private async Task<string> GenerateLlmResponseAsync(
        IReadOnlyList<ChatMessage> messages,
        string sessionId,
        CancellationToken ct)
    {
        for (var attempt = 1; attempt <= MaxLlmAttempts; attempt++)
        {
            var sb = new StringBuilder();
            try
            {
                await foreach (var token in _llm.ChatAsync(messages, new LlmOptions(Temperature: 0.1, MaxTokens: 512), ct).ConfigureAwait(false))
                {
                    if (!string.IsNullOrEmpty(token.Delta))
                        sb.Append(token.Delta);
                }

                return sb.ToString().Trim();
            }
            catch (Exception ex) when (IsRateLimitException(ex) && attempt < MaxLlmAttempts)
            {
                var delay = TimeSpan.FromMilliseconds(400 * attempt * attempt);
                _logger.LogWarning(ex, "LLM rate-limited for session {SessionId}; retrying in {DelayMs}ms (attempt {Attempt}/{MaxAttempts})", sessionId, (int)delay.TotalMilliseconds, attempt + 1, MaxLlmAttempts);
                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
        }

        throw new InvalidOperationException("The language model did not return a response.");
    }

    private async Task<List<ScoredChunk>> RetrieveGroundedHitsAsync(
        string sessionId,
        string question,
        bool isUsageQuestion,
        CancellationToken ct)
    {
        var primary = await _retriever.RetrieveAsync(sessionId, question, TopK, ct).ConfigureAwait(false);
        if (!isUsageQuestion)
            return primary.ToList();

        var docsQuery = question + " README readme docs documentation usage install setup run start quickstart command cli";
        var readmeQuery = "README.md readme getting started quickstart install usage run";
        var secondary = await _retriever.RetrieveAsync(sessionId, docsQuery, UsageTopK, ct).ConfigureAwait(false);
        var tertiary = await _retriever.RetrieveAsync(sessionId, readmeQuery, UsageTopK, ct).ConfigureAwait(false);

        var merged = new Dictionary<string, (ScoredChunk chunk, double adjusted)>(StringComparer.OrdinalIgnoreCase);
        foreach (var hit in primary.Concat(secondary).Concat(tertiary))
        {
            var key = $"{hit.Chunk.FilePath}|{hit.Chunk.StartLine}|{hit.Chunk.EndLine}";
            var adjusted = hit.Score + OperationalDocBoost(hit.Chunk.FilePath);
            if (!merged.TryGetValue(key, out var existing) || adjusted > existing.adjusted)
                merged[key] = (hit, adjusted);
        }

        return merged.Values
            .OrderByDescending(x => x.adjusted)
            .ThenByDescending(x => x.chunk.Score)
            .Take(UsageTopK)
            .Select(x => x.chunk)
            .ToList();
    }

    private static bool IsDocLikePath(string path)
    {
        var p = path.Replace('\\', '/');
        return p.EndsWith("README.md", StringComparison.OrdinalIgnoreCase)
            || p.EndsWith("readme.md", StringComparison.OrdinalIgnoreCase)
            || p.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
            || p.Contains("/docs/", StringComparison.OrdinalIgnoreCase)
            || p.Contains("/doc/", StringComparison.OrdinalIgnoreCase)
            || p.Contains("/examples/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsOperationalUsageDocPath(string path)
    {
        var p = path.Replace('\\', '/');
        var isDoc = IsDocLikePath(p);
        if (!isDoc)
            return false;

        var isNonOperational = p.EndsWith("SECURITY.md", StringComparison.OrdinalIgnoreCase)
            || p.EndsWith("USERS.md", StringComparison.OrdinalIgnoreCase)
            || p.EndsWith("LICENSE", StringComparison.OrdinalIgnoreCase)
            || p.EndsWith("CONTRIBUTING.md", StringComparison.OrdinalIgnoreCase)
            || p.EndsWith("CHANGELOG.md", StringComparison.OrdinalIgnoreCase);

        if (isNonOperational)
            return false;

        return p.EndsWith("README.md", StringComparison.OrdinalIgnoreCase)
            || p.Contains("quickstart", StringComparison.OrdinalIgnoreCase)
            || p.Contains("getting-started", StringComparison.OrdinalIgnoreCase)
            || p.Contains("install", StringComparison.OrdinalIgnoreCase)
            || p.Contains("usage", StringComparison.OrdinalIgnoreCase)
            || p.Contains("/docs/", StringComparison.OrdinalIgnoreCase)
            || p.Contains("/doc/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFixturePath(string path)
    {
        var p = path.Replace('\\', '/');
        return p.Contains("/testdata/", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith("testdata/", StringComparison.OrdinalIgnoreCase)
            || p.Contains("/fixtures/", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith("fixtures/", StringComparison.OrdinalIgnoreCase)
            || p.Contains("/samples/", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith("samples/", StringComparison.OrdinalIgnoreCase);
    }

    private static double OperationalDocBoost(string path)
    {
        var p = path.Replace('\\', '/');
        if (p.EndsWith("README.md", StringComparison.OrdinalIgnoreCase)
            || p.EndsWith("readme.md", StringComparison.OrdinalIgnoreCase))
            return 0.45;

        if (p.Contains("quickstart", StringComparison.OrdinalIgnoreCase)
            || p.Contains("getting-started", StringComparison.OrdinalIgnoreCase)
            || p.Contains("install", StringComparison.OrdinalIgnoreCase)
            || p.Contains("usage", StringComparison.OrdinalIgnoreCase))
            return 0.30;

        if (p.Contains("/docs/", StringComparison.OrdinalIgnoreCase)
            || p.Contains("/doc/", StringComparison.OrdinalIgnoreCase)
            || p.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            return 0.12;

        if (p.EndsWith("SECURITY.md", StringComparison.OrdinalIgnoreCase)
            || p.EndsWith("USERS.md", StringComparison.OrdinalIgnoreCase)
            || p.EndsWith("CONTRIBUTING.md", StringComparison.OrdinalIgnoreCase)
            || p.EndsWith("LICENSE", StringComparison.OrdinalIgnoreCase))
            return -0.12;

        return 0.0;
    }

    private static string? TryAnswerFromMetadata(string question, Domain.Sessions.RepoSession session)
    {
        if (GreetingQuestion.IsMatch(question))
            return "Hi. Ask me about repository facts like file counts, likely entrypoint, criticality limits, security findings, or TS-ANY hotspots.";

        if (HelpQuestion.IsMatch(question))
        {
            return "I can answer grounded repository questions about tech stack, frameworks, main entrypoints, APIs, risky files, security findings, dependency manifests, and relevant code locations. Try asking: 'What tech stack is used?', 'List all APIs', 'Where is this API implemented?', or 'What should I improve before production?'";
        }

        var snapshot = session.Analysis;
        if (snapshot is null) return null;

        if (TechStackQuestion.IsMatch(question))
        {
            var parts = GetStackParts(snapshot);
            var languages = snapshot.Files
                .Select(f => f.Language)
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(l => l, StringComparer.OrdinalIgnoreCase)
                .Take(6)
                .ToList();

            var stack = parts.Count > 0 ? string.Join(", ", parts) : "no clear framework-specific stack detected";
            var langs = languages.Count > 0 ? $" Indexed languages: {string.Join(", ", languages)}." : string.Empty;
            return $"Detected tech stack: {stack}. Primary language: {snapshot.PrimaryLanguage ?? session.PrimaryLanguage ?? "unknown"}.{langs}";
        }

        if (FrontendBackendQuestion.IsMatch(question))
        {
            var hasAngular = HasAngular(snapshot);
            var hasDotNet = HasDotNet(snapshot);

            return (hasAngular, hasDotNet) switch
            {
                (true, true) => "This indexed repository contains both frontend and backend code: an Angular frontend plus a .NET backend.",
                (true, false) => "This indexed repository looks frontend-focused, primarily Angular-based.",
                (false, true) => "This indexed repository looks backend-focused, primarily .NET-based.",
                _ => "I can't clearly classify this indexed repository as frontend-only or backend-only from the current snapshot."
            };
        }

        if (FrameworkQuestion.IsMatch(question))
        {
            var frameworks = GetStackParts(snapshot);
            if (frameworks.Count == 0)
                return $"I can't determine a clear framework from the indexed snapshot. Stored framework metadata is '{snapshot.Framework ?? session.Framework ?? "Unknown"}'.";

            return $"Detected frameworks and major libraries: {string.Join(", ", frameworks)}.";
        }

        if (ApiListQuestion.IsMatch(question))
        {
            var paths = snapshot.Files
                .Select(f => f.RelativePath.Replace('\\', '/'))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var controllers = paths
                .Where(p => p.EndsWith("Controller.cs", StringComparison.OrdinalIgnoreCase))
                .Select(Path.GetFileNameWithoutExtension)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var backendRouteFiles = paths
                .Where(IsLikelyBackendRoutePath)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .Take(8)
                .ToList();

            var apiClients = paths
                .Where(IsLikelyApiClientPath)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .Take(10)
                .ToList();

            if (controllers.Count == 0 && backendRouteFiles.Count == 0 && apiClients.Count == 0)
                return "I can't find clear API controller or client files in the indexed repository snapshot.";

            var segments = new List<string>();
            if (controllers.Count > 0)
                segments.Add($"backend controllers: {string.Join(", ", controllers)}");
            if (backendRouteFiles.Count > 0)
                segments.Add($"backend route files: {string.Join(", ", backendRouteFiles)}");
            if (apiClients.Count > 0)
                segments.Add($"frontend API clients: {string.Join(", ", apiClients)}");
            return $"Detected API surface from indexed files: {string.Join("; ", segments)}.";
        }

        if (NewDeveloperQuestion.IsMatch(question))
        {
            var parts = GetStackParts(snapshot);
            var mainEntry = TryAnswerFromMetadata("main entrypoint", session);
            var totalFiles = snapshot.Files.Count;
            return $"This repository is a repo-intelligence application. It includes {totalFiles} indexed files and appears to use {string.Join(", ", parts.DefaultIfEmpty("mixed technologies"))}. {mainEntry} Start with the API controllers, the chat orchestrator, and the frontend dashboard/chat components to understand the end-to-end flow.";
        }

        if (ProductionReadinessQuestion.IsMatch(question))
        {
            var findings = GetEffectiveFindings(session)
                .OrderBy(f => SeverityRank(f.Severity))
                .ThenBy(f => f.File, StringComparer.OrdinalIgnoreCase)
                .ThenBy(f => f.Line)
                .Take(3)
                .ToList();

            var suggestions = new List<string>();
            if (findings.Count > 0)
                suggestions.Add($"address top findings first: {string.Join("; ", findings.Select(f => $"{f.Severity} {f.RuleId} at {f.File}:{f.Line}"))}");
            if (!HasDependencyLockfile(snapshot.Files))
                suggestions.Add("commit a dependency lockfile for better supply-chain determinism");
            if (HasAngular(snapshot) && HasDotNet(snapshot))
                suggestions.Add("add end-to-end and integration coverage for frontend-to-backend flows");
            suggestions.Add("add graceful fallback behavior for transient AI provider failures");

            return $"Before production, prioritize: {string.Join("; ", suggestions)}.";
        }

        if (AuthQuestion.IsMatch(question))
        {
            var authFiles = snapshot.Files
                .Select(f => f.RelativePath.Replace('\\', '/'))
                .Where(p => p.Contains("auth", StringComparison.OrdinalIgnoreCase)
                            || p.Contains("login", StringComparison.OrdinalIgnoreCase)
                            || p.Contains("signin", StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .Take(8)
                .ToList();

            if (authFiles.Count == 0)
                return "I can't find login or authentication-specific files in the indexed repository.";

            return $"Authentication-related files detected: {string.Join(", ", authFiles)}.";
        }

        if (RepoOverviewQuestion.IsMatch(question))
        {
            var totalFiles = snapshot.Files.Count;
            var sourceFiles = snapshot.Files.Count(f => string.Equals(f.Category, "src", StringComparison.OrdinalIgnoreCase));
            var testFiles = snapshot.Files.Count(f => string.Equals(f.Category, "test", StringComparison.OrdinalIgnoreCase));
            var configFiles = snapshot.Files.Count(f => string.Equals(f.Category, "config", StringComparison.OrdinalIgnoreCase));
            var docFiles = snapshot.Files.Count(f => string.Equals(f.Category, "docs", StringComparison.OrdinalIgnoreCase));
            return $"Indexed repo summary: {totalFiles} files total (source: {sourceFiles}, test: {testFiles}, config: {configFiles}, docs: {docFiles}). Ask for criticals, warnings, security findings, entrypoint, or risky files for more detail.";
        }

        if (DependencyQuestion.IsMatch(question))
        {
            var manifests = snapshot.Files
                .Select(f => f.RelativePath.Replace('\\', '/'))
                .Where(IsDependencyManifestPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .Take(15)
                .ToList();

            if (manifests.Count == 0)
                return "I can't find dependency manifest files in the indexed repository snapshot.";

            return $"Dependency manifest files ({manifests.Count} shown): {string.Join(", ", manifests)}.";
        }

        if (SecurityFindingsQuestion.IsMatch(question))
        {
            var findings = GetEffectiveFindings(session);
            var security = findings
                .Where(f => f.Category == Domain.Insights.FindingCategory.Security)
                .OrderBy(f => SeverityRank(f.Severity))
                .ThenBy(f => f.File, StringComparer.OrdinalIgnoreCase)
                .ThenBy(f => f.Line)
                .Take(10)
                .ToList();

            if (security.Count == 0)
                return "No security findings are present in the indexed session findings.";

            var list = string.Join("; ", security.Select(f => $"{f.Severity} {f.RuleId} at {f.File}:{f.Line}"));
            return $"Security findings ({security.Count} shown): {list}.";
        }

        if (TsAnyFilesQuestion.IsMatch(question))
        {
            var findings = session.Findings ?? Array.Empty<Domain.Insights.Finding>();
            var ranked = findings
                .Where(f => string.Equals(f.RuleId, "TS-ANY", StringComparison.OrdinalIgnoreCase))
                .GroupBy(f => f.File, StringComparer.OrdinalIgnoreCase)
                .Select(g => new
                {
                    File = g.Key,
                    Count = g.Sum(ParseTsAnyCountFromMessage),
                    Hits = g.Count(),
                })
                .OrderByDescending(x => x.Count)
                .ThenByDescending(x => x.Hits)
                .ThenBy(x => x.File, StringComparer.OrdinalIgnoreCase)
                .Take(5)
                .ToList();

            if (ranked.Count == 0)
                return "No TS-ANY findings are present in the indexed session findings.";

            var list = string.Join(", ", ranked.Select(x => $"{x.File} ({x.Count} any usages)"));
            return $"Top files by TS-ANY usage: {list}.";
        }

        if (LargestFileQuestion.IsMatch(question))
        {
            var ranked = snapshot.Files
                .Where(f => string.Equals(f.Category, "src", StringComparison.OrdinalIgnoreCase))
                .Where(f => !IsFixturePath(f.RelativePath))
                .OrderByDescending(f => f.LineCount)
                .ThenByDescending(f => f.SizeBytes)
                .Take(5)
                .ToList();

            if (ranked.Count == 0)
            {
                ranked = snapshot.Files
                    .Where(f => !IsFixturePath(f.RelativePath))
                    .OrderByDescending(f => f.LineCount)
                    .ThenByDescending(f => f.SizeBytes)
                    .Take(5)
                    .ToList();
            }

            if (ranked.Count == 0)
                return "I can't determine the largest file from the indexed repository snapshot.";

            var top = ranked[0];
            var list = string.Join(", ", ranked.Select(f => $"{f.RelativePath} ({f.LineCount} lines, {f.SizeBytes} bytes)"));
            return $"Largest indexed file by size/complexity is {top.RelativePath} ({top.LineCount} lines, {top.SizeBytes} bytes). Top largest files: {list}.";
        }

        if (MostChangesQuestion.IsMatch(question))
        {
            // We only index a repository snapshot, not git commit history.
            var ranked = snapshot.Files
                .Where(f => string.Equals(f.Category, "src", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => f.LineCount)
                .ThenByDescending(f => f.SizeBytes)
                .Take(3)
                .ToList();

            if (ranked.Count == 0)
            {
                ranked = snapshot.Files
                    .OrderByDescending(f => f.LineCount)
                    .ThenByDescending(f => f.SizeBytes)
                    .Take(3)
                    .ToList();
            }

            if (ranked.Count == 0)
                return "I can't determine that from the indexed repository snapshot.";

            var top = ranked[0];
            var topList = string.Join(", ", ranked.Select(f => $"{f.RelativePath} ({f.LineCount} lines)"));

            return $"I cannot infer actual git change history from the indexed snapshot. By size/complexity proxy, the largest source file is {top.RelativePath} ({top.LineCount} lines). Top candidates by size are: {topList}.";
        }

        if (MainFileQuestion.IsMatch(question))
        {
            var files = snapshot.Files
                .Select(f => f.RelativePath.Replace('\\', '/'))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var manifestHints = InferEntrypointHintsFromLayout(files);
            if (manifestHints.Count > 0)
                return $"Entrypoint hints from indexed repository layout: {string.Join(" ", manifestHints)}";

            var strongCandidates = files
                .Where(p => p.EndsWith("/main.go", StringComparison.OrdinalIgnoreCase)
                            || p.Equals("main.go", StringComparison.OrdinalIgnoreCase)
                            || p.EndsWith("/Program.cs", StringComparison.OrdinalIgnoreCase)
                            || p.Equals("Program.cs", StringComparison.OrdinalIgnoreCase)
                            || p.EndsWith("/src/main.ts", StringComparison.OrdinalIgnoreCase)
                            || p.EndsWith("/src/main.js", StringComparison.OrdinalIgnoreCase)
                            || p.EndsWith("/src/main.jsx", StringComparison.OrdinalIgnoreCase)
                            || p.EndsWith("/src/main.tsx", StringComparison.OrdinalIgnoreCase)
                            || p.EndsWith("/app/main.py", StringComparison.OrdinalIgnoreCase)
                            || p.Equals("main.py", StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var nonFixtureCandidates = strongCandidates
                .Where(p => !p.Contains("/testdata/", StringComparison.OrdinalIgnoreCase)
                            && !p.StartsWith("testdata/", StringComparison.OrdinalIgnoreCase)
                            && !p.Contains("/fixtures/", StringComparison.OrdinalIgnoreCase)
                            && !p.StartsWith("fixtures/", StringComparison.OrdinalIgnoreCase)
                            && !p.Contains("/samples/", StringComparison.OrdinalIgnoreCase)
                            && !p.StartsWith("samples/", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (nonFixtureCandidates.Count == 1)
                return $"The likely main entrypoint is {nonFixtureCandidates[0]} based on indexed file paths.";

            if (nonFixtureCandidates.Count > 1)
                return $"I found multiple possible entrypoints in the indexed repository: {string.Join(", ", nonFixtureCandidates.Take(5))}.";

            if (strongCandidates.Count == 1)
                return $"The likely main entrypoint is {strongCandidates[0]} based on indexed file paths.";

            if (strongCandidates.Count > 1)
                return $"I found multiple possible entrypoints in the indexed repository: {string.Join(", ", strongCandidates.Take(5))}.";

            return "I can't determine a single main entrypoint from the indexed repository snapshot.";
        }

        if (CriticalityQuestion.IsMatch(question))
        {
            var findings = GetEffectiveFindings(session);
            if (findings.Count > 0)
            {
                if (CriticalFindingsQuestion.IsMatch(question))
                {
                    var criticalOnly = findings
                        .Where(f => f.Severity == Domain.Insights.Severity.Critical)
                        .OrderBy(f => f.File, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(f => f.Line)
                        .Take(10)
                        .ToList();

                    if (criticalOnly.Count == 0)
                        return "No critical findings are present in the indexed session findings.";

                    var criticalList = string.Join("; ", criticalOnly.Select(f => $"{f.RuleId} at {f.File}:{f.Line}"));
                    return $"Critical findings ({criticalOnly.Count} shown): {criticalList}.";
                }

                var critical = findings.Count(f => f.Severity == Domain.Insights.Severity.Critical);
                var high = findings.Count(f => f.Severity == Domain.Insights.Severity.High);
                var medium = findings.Count(f => f.Severity == Domain.Insights.Severity.Medium);
                var low = findings.Count(f => f.Severity == Domain.Insights.Severity.Low);
                var info = findings.Count(f => f.Severity == Domain.Insights.Severity.Info);

                var top = findings
                    .OrderBy(f => SeverityRank(f.Severity))
                    .ThenBy(f => f.File, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(f => f.Line)
                    .Take(5)
                    .ToList();

                var topList = string.Join("; ", top.Select(f => $"{f.Severity} {f.RuleId} at {f.File}:{f.Line}"));
                return $"From indexed findings, severity breakdown is Critical: {critical}, High: {high}, Medium: {medium}, Low: {low}, Info: {info}. Top criticality findings: {topList}.";
            }

            // If findings are not available, use deterministic size/complexity proxy.
            var ranked = snapshot.Files
                .Where(f => string.Equals(f.Category, "src", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => f.LineCount)
                .ThenByDescending(f => f.SizeBytes)
                .Take(5)
                .ToList();

            if (ranked.Count == 0)
            {
                ranked = snapshot.Files
                    .OrderByDescending(f => f.LineCount)
                    .ThenByDescending(f => f.SizeBytes)
                    .Take(5)
                    .ToList();
            }

            if (ranked.Count == 0)
                return "I can't determine criticality from the indexed repository snapshot.";

            var list = string.Join(", ", ranked.Select(f => $"{f.RelativePath} ({f.LineCount} lines)"));
            return $"The indexed snapshot does not include true static-analysis criticality per file. As a size/complexity proxy, highest-risk files are: {list}.";
        }

        if (WarningsQuestion.IsMatch(question))
        {
            var findings = GetEffectiveFindings(session);
            if (findings.Count == 0)
                return "No warnings are present in the indexed session findings.";

            var warningLike = findings
                .Where(f => f.Severity != Domain.Insights.Severity.Critical)
                .OrderBy(f => SeverityRank(f.Severity))
                .ThenBy(f => f.File, StringComparer.OrdinalIgnoreCase)
                .ThenBy(f => f.Line)
                .Take(10)
                .ToList();

            if (warningLike.Count == 0)
                return "No non-critical warnings are present in the indexed session findings.";

            var high = warningLike.Count(f => f.Severity == Domain.Insights.Severity.High);
            var medium = warningLike.Count(f => f.Severity == Domain.Insights.Severity.Medium);
            var low = warningLike.Count(f => f.Severity == Domain.Insights.Severity.Low);
            var info = warningLike.Count(f => f.Severity == Domain.Insights.Severity.Info);
            var list = string.Join("; ", warningLike.Select(f => $"{f.Severity} {f.RuleId} at {f.File}:{f.Line}"));

            return $"Warnings from indexed findings (shown {warningLike.Count}): High: {high}, Medium: {medium}, Low: {low}, Info: {info}. {list}.";
        }

        if (RiskyFilesQuestion.IsMatch(question))
        {
            var ranked = snapshot.Files
                .Where(f => string.Equals(f.Category, "src", StringComparison.OrdinalIgnoreCase))
                .Where(f => !IsFixturePath(f.RelativePath))
                .OrderByDescending(f => f.LineCount)
                .ThenByDescending(f => f.SizeBytes)
                .Take(5)
                .ToList();

            if (ranked.Count == 0)
            {
                ranked = snapshot.Files
                    .Where(f => !IsFixturePath(f.RelativePath))
                    .OrderByDescending(f => f.LineCount)
                    .ThenByDescending(f => f.SizeBytes)
                    .Take(5)
                    .ToList();
            }

            if (ranked.Count == 0)
                return "I can't determine risky files from the indexed repository snapshot.";

            var list = string.Join(", ", ranked.Select(f => $"{f.RelativePath} ({f.LineCount} lines)"));
            return $"By size/complexity proxy, top risky files are: {list}.";
        }

        if (!FileCountQuestion.IsMatch(question)) return null;

        var total = snapshot.Files.Count;
        var source = snapshot.Files.Count(f => string.Equals(f.Category, "src", StringComparison.OrdinalIgnoreCase));
        var test = snapshot.Files.Count(f => string.Equals(f.Category, "test", StringComparison.OrdinalIgnoreCase));
        var config = snapshot.Files.Count(f => string.Equals(f.Category, "config", StringComparison.OrdinalIgnoreCase));
        var docs = snapshot.Files.Count(f => string.Equals(f.Category, "docs", StringComparison.OrdinalIgnoreCase));

        return $"The indexed repository contains {total} files (source: {source}, test: {test}, config: {config}, docs: {docs}).";
    }

    private static string NormalizeQuestion(string question)
    {
        var normalized = question.Trim();
        normalized = Regex.Replace(normalized, @"\blogn\b", "login", RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"\bmodul\b", "module", RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"\bfrmework\b", "framework", RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"\s+", " ");
        return normalized;
    }

    private static bool HasAngular(Domain.Sessions.RepoAnalysisSnapshot snapshot)
        => snapshot.Angular is not null
            || snapshot.Files.Any(f => f.RelativePath.EndsWith(".component.ts", StringComparison.OrdinalIgnoreCase)
                                       || f.RelativePath.EndsWith("angular.json", StringComparison.OrdinalIgnoreCase));

    private static bool HasDotNet(Domain.Sessions.RepoAnalysisSnapshot snapshot)
        => snapshot.DotNet is not null
            || snapshot.Files.Any(f => f.RelativePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                                       || f.RelativePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                                       || f.RelativePath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase));

    private static List<string> GetStackParts(Domain.Sessions.RepoAnalysisSnapshot snapshot)
    {
        var parts = new List<string>();
        if (HasAngular(snapshot))
            parts.Add("Angular frontend");
        if (HasDotNet(snapshot))
            parts.Add(".NET backend");
        if (snapshot.Files.Any(f => f.RelativePath.Contains("cytoscape", StringComparison.OrdinalIgnoreCase)))
            parts.Add("Cytoscape graph visualization");
        return parts;
    }

    private static bool IsRateLimitException(Exception ex)
        => ex.Message.Contains("429", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("too many requests", StringComparison.OrdinalIgnoreCase);

    private static ChatResponse BuildUnavailableFallback(
        string question,
        Domain.Sessions.RepoSession session,
        IReadOnlyList<ScoredChunk> grounded,
        bool wasRateLimited)
    {
        var metadata = TryAnswerFromMetadata(question, session);
        if (metadata is not null)
            return new ChatResponse(metadata, Array.Empty<ChatCitation>(), Grounded: true);

        var citations = grounded
            .Select(h => new ChatCitation(h.Chunk.FilePath, h.Chunk.StartLine, h.Chunk.EndLine, h.Score))
            .ToList();

        if (citations.Count == 0)
        {
            var message = wasRateLimited
                ? "Live AI is busy right now. I can still answer indexed repository facts like tech stack, main entrypoint, APIs, risky files, security findings, dependency manifests, and file counts."
                : "Live AI is temporarily unavailable. I can still answer indexed repository facts like tech stack, main entrypoint, APIs, risky files, security findings, dependency manifests, and file counts.";
            return new ChatResponse(message, Array.Empty<ChatCitation>(), Grounded: false);
        }

        var prefix = wasRateLimited
            ? "Live AI is busy right now. Based on indexed repository matches, the most relevant code is in: "
            : "Live AI is temporarily unavailable. Based on indexed repository matches, the most relevant code is in: ";
        var locations = string.Join("; ", citations.Take(4).Select(c => $"{c.File} L{c.StartLine}-{c.EndLine}"));
        var content = prefix + locations + ". Retry in a moment for a fuller natural-language explanation.";
        return new ChatResponse(content, citations, Grounded: true);
    }

    private static int ParseTsAnyCountFromMessage(Domain.Insights.Finding finding)
    {
        var message = finding.Message ?? string.Empty;
        var m = Regex.Match(message, @"any type used\s+(\d+)\s+time\(s\)", RegexOptions.IgnoreCase);
        if (m.Success && int.TryParse(m.Groups[1].Value, out var n))
            return n;
        return 1;
    }

    private static IReadOnlyList<Domain.Insights.Finding> GetEffectiveFindings(Domain.Sessions.RepoSession session)
    {
        if (session.Findings is { Count: > 0 })
            return session.Findings;

        var snapshot = session.Analysis;
        if (snapshot is null)
            return Array.Empty<Domain.Insights.Finding>();

        var findings = new List<Domain.Insights.Finding>();

        foreach (var file in snapshot.Files.Where(f => LooksLikeSecretFile(f.RelativePath)).Take(5))
        {
            findings.Add(new Domain.Insights.Finding
            {
                Id = string.Empty,
                RuleId = "S-POTENTIAL-SECRET",
                Severity = Domain.Insights.Severity.Critical,
                Category = Domain.Insights.FindingCategory.Security,
                File = file.RelativePath,
                Line = 1,
                Column = 1,
                Message = "Potential secret-bearing file detected.",
                FixHint = "Remove from source control and rotate credentials.",
            });
        }

        if (!HasDependencyLockfile(snapshot.Files))
        {
            findings.Add(new Domain.Insights.Finding
            {
                Id = string.Empty,
                RuleId = "S-NO-LOCKFILE",
                Severity = Domain.Insights.Severity.Low,
                Category = Domain.Insights.FindingCategory.Security,
                File = "(repo)",
                Line = 1,
                Column = 1,
                Message = "No dependency lockfile detected.",
                FixHint = "Commit a lockfile to improve supply-chain determinism.",
            });
        }

        return findings;
    }

    private static bool LooksLikeSecretFile(string path)
    {
        var p = path.Replace('\\', '/').ToLowerInvariant();
        var name = Path.GetFileName(p);
        return name == ".env"
            || name.EndsWith(".pem", StringComparison.Ordinal)
            || name.EndsWith(".pfx", StringComparison.Ordinal)
            || name.EndsWith(".key", StringComparison.Ordinal)
            || p.Contains("secret", StringComparison.Ordinal)
            || p.Contains("apikey", StringComparison.Ordinal)
            || p.Contains("token", StringComparison.Ordinal);
    }

    private static bool HasDependencyLockfile(IReadOnlyList<Domain.Code.CodeFile> files)
        => files.Any(f =>
            string.Equals(Path.GetFileName(f.RelativePath), "package-lock.json", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetFileName(f.RelativePath), "yarn.lock", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetFileName(f.RelativePath), "pnpm-lock.yaml", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetFileName(f.RelativePath), "packages.lock.json", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetFileName(f.RelativePath), "Pipfile.lock", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetFileName(f.RelativePath), "poetry.lock", StringComparison.OrdinalIgnoreCase));

    private static bool IsDependencyManifestPath(string path)
    {
        var name = Path.GetFileName(path);
        return string.Equals(name, "go.mod", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "go.sum", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "package.json", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "package-lock.json", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "yarn.lock", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "pnpm-lock.yaml", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "Pipfile", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "Pipfile.lock", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "requirements.txt", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "poetry.lock", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "pyproject.toml", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "pom.xml", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "build.gradle", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "build.gradle.kts", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "Cargo.toml", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "Cargo.lock", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "composer.json", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "composer.lock", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "Gemfile", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "Gemfile.lock", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "deps.edn", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "mix.exs", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "mix.lock", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "Directory.Packages.props", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "packages.lock.json", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "global.json", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLikelyApiClientPath(string path)
    {
        var p = path.Replace('\\', '/');
        var name = Path.GetFileName(p).ToLowerInvariant();

        if (name.EndsWith(".api.ts", StringComparison.OrdinalIgnoreCase))
            return true;

        if (name.EndsWith(".service.ts", StringComparison.OrdinalIgnoreCase)
            && (name.Contains("api", StringComparison.Ordinal)
                || name.Contains("http", StringComparison.Ordinal)
                || name.Contains("client", StringComparison.Ordinal)
                || name.Contains("data", StringComparison.Ordinal)
                || name.Contains("interceptor", StringComparison.Ordinal)
                || name.Contains("resource", StringComparison.Ordinal)
                || name.Contains("proxy", StringComparison.Ordinal)))
            return true;

        return false;
    }

    private static bool IsLikelyBackendRoutePath(string path)
    {
        var p = path.Replace('\\', '/').ToLowerInvariant();
        var name = Path.GetFileName(p);

        if (name.EndsWith("controller.cs", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("controller.ts", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("controller.js", StringComparison.OrdinalIgnoreCase))
            return true;

        if (p.Contains("/controllers/", StringComparison.Ordinal)
            || p.Contains("/routes/", StringComparison.Ordinal)
            || p.Contains("/router/", StringComparison.Ordinal)
            || p.EndsWith("/program.cs", StringComparison.Ordinal)
            || p.EndsWith("/main.go", StringComparison.Ordinal)
            || p.EndsWith("/main.py", StringComparison.Ordinal))
            return true;

        return false;
    }

    private static List<string> InferEntrypointHintsFromLayout(IReadOnlyList<string> files)
    {
        var hints = new List<string>();

        bool HasPath(string target)
            => files.Any(p => p.Equals(target, StringComparison.OrdinalIgnoreCase)
                              || p.EndsWith("/" + target, StringComparison.OrdinalIgnoreCase));

        bool hasPackageJson = files.Any(p =>
            Path.GetFileName(p).Equals("package.json", StringComparison.OrdinalIgnoreCase));

        if (hasPackageJson)
        {
            if (HasPath("bin/parse-server"))
                hints.Add("Runtime startup likely uses bin/parse-server (package CLI binary).");
            else if (files.Any(p => p.StartsWith("bin/", StringComparison.OrdinalIgnoreCase)
                                    || p.Contains("/bin/", StringComparison.OrdinalIgnoreCase)))
                hints.Add("Runtime startup likely uses a package CLI under bin/.");

            if (HasPath("lib/index.js"))
                hints.Add("Package module entrypoint likely maps to lib/index.js.");
            else if (HasPath("src/index.ts") || HasPath("src/index.js"))
                hints.Add("Package module entrypoint likely maps to src/index.*.");
        }

        if (HasPath("Program.cs") || HasPath("src/Program.cs"))
            hints.Add(".NET app startup likely begins at Program.cs.");

        if (HasPath("main.py") || HasPath("app/main.py"))
            hints.Add("Python app startup likely begins at main.py.");

        if (HasPath("main.go") || files.Any(p => p.Contains("/cmd/", StringComparison.OrdinalIgnoreCase) && p.EndsWith("/main.go", StringComparison.OrdinalIgnoreCase)))
            hints.Add("Go app startup likely begins at main.go.");

        return hints;
    }

    private static int SeverityRank(Domain.Insights.Severity severity)
    {
        return severity switch
        {
            Domain.Insights.Severity.Critical => 0,
            Domain.Insights.Severity.High => 1,
            Domain.Insights.Severity.Medium => 2,
            Domain.Insights.Severity.Low => 3,
            Domain.Insights.Severity.Info => 4,
            _ => 9,
        };
    }
}
