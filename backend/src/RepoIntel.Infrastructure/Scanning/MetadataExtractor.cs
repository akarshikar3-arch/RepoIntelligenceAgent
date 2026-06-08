using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using RepoIntel.Application.Abstractions;
using RepoIntel.Domain.Code;

namespace RepoIntel.Infrastructure.Scanning;

/// <summary>
/// Inspects scanned files to detect frameworks (Angular, .NET) and aggregate
/// per-language counts. Reads only the small marker files it needs.
/// </summary>
public sealed class MetadataExtractor : IMetadataExtractor
{
    private static readonly Regex TargetFrameworkRegex =
        new(@"<TargetFrameworks?>([^<]+)</TargetFrameworks?>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly ILogger<MetadataExtractor> _logger;

    public MetadataExtractor(ILogger<MetadataExtractor> logger)
    {
        _logger = logger;
    }

    public async Task<RepoMetadata> ExtractAsync(
        string sessionId,
        string rootPath,
        IReadOnlyList<CodeFile> files,
        CancellationToken ct = default)
    {
        var languageCounts = files
            .GroupBy(f => f.Language, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        var primaryLanguage = languageCounts
            .Where(kv => kv.Key is not ("other" or "json" or "yaml" or "xml" or "markdown" or "msbuild"))
            .OrderByDescending(kv => kv.Value)
            .Select(kv => kv.Key)
            .FirstOrDefault();

        var angular = await DetectAngularAsync(rootPath, files, ct).ConfigureAwait(false);
        var dotnet = await DetectDotNetAsync(rootPath, files, ct).ConfigureAwait(false);

        var framework = (angular, dotnet) switch
        {
            (not null, not null) => DetectedFramework.Mixed,
            (not null, null) => DetectedFramework.Angular,
            (null, not null) => DetectedFramework.DotNet,
            _ => DetectedFramework.Unknown,
        };

        _logger.LogInformation(
            "Session {SessionId}: framework={Framework} primaryLang={Lang}",
            sessionId, framework, primaryLanguage);

        return new RepoMetadata(sessionId, framework, primaryLanguage, angular, dotnet, languageCounts);
    }

    private static async Task<AngularInfo?> DetectAngularAsync(
        string rootPath,
        IReadOnlyList<CodeFile> files,
        CancellationToken ct)
    {
        var angularJson = files.FirstOrDefault(f =>
            f.RelativePath.Equals("angular.json", StringComparison.OrdinalIgnoreCase));

        var packageJson = files.FirstOrDefault(f =>
            f.RelativePath.Equals("package.json", StringComparison.OrdinalIgnoreCase));

        string? version = null;
        var hasAngularDep = false;

        if (packageJson is not null)
        {
            var pkgPath = Path.Combine(rootPath, packageJson.RelativePath);
            try
            {
                using var stream = File.OpenRead(pkgPath);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
                version = ReadAngularVersion(doc);
                hasAngularDep = version is not null;
            }
            catch { /* malformed package.json — ignore */ }
        }

        if (angularJson is null && !hasAngularDep) return null;

        var projects = new List<string>();
        if (angularJson is not null)
        {
            var ngPath = Path.Combine(rootPath, angularJson.RelativePath);
            try
            {
                using var stream = File.OpenRead(ngPath);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
                if (doc.RootElement.TryGetProperty("projects", out var projectsEl) &&
                    projectsEl.ValueKind == JsonValueKind.Object)
                {
                    foreach (var p in projectsEl.EnumerateObject()) projects.Add(p.Name);
                }
            }
            catch { /* ignore parse errors */ }
        }

        return new AngularInfo(angularJson?.RelativePath, version, projects);
    }

    private static string? ReadAngularVersion(JsonDocument doc)
    {
        foreach (var section in new[] { "dependencies", "devDependencies", "peerDependencies" })
        {
            if (!doc.RootElement.TryGetProperty(section, out var deps) ||
                deps.ValueKind != JsonValueKind.Object) continue;

            if (deps.TryGetProperty("@angular/core", out var v) && v.ValueKind == JsonValueKind.String)
                return v.GetString();
        }
        return null;
    }

    private static async Task<DotNetInfo?> DetectDotNetAsync(
        string rootPath,
        IReadOnlyList<CodeFile> files,
        CancellationToken ct)
    {
        var slns = files
            .Where(f => f.RelativePath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
            .Select(f => f.RelativePath)
            .ToList();

        var csprojs = files
            .Where(f => f.RelativePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .Select(f => f.RelativePath)
            .ToList();

        if (slns.Count == 0 && csprojs.Count == 0) return null;

        var tfms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rel in csprojs)
        {
            ct.ThrowIfCancellationRequested();
            var path = Path.Combine(rootPath, rel);
            try
            {
                var text = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
                foreach (Match m in TargetFrameworkRegex.Matches(text))
                {
                    foreach (var t in m.Groups[1].Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        tfms.Add(t);
                }
            }
            catch { /* unreadable csproj — skip */ }
        }

        return new DotNetInfo(slns, csprojs, tfms.ToList());
    }
}
