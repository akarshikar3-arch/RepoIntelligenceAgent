using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using RepoIntel.Application.Abstractions;
using RepoIntel.Domain.Architecture;
using RepoIntel.Domain.Code;

namespace RepoIntel.Infrastructure.Architecture;

/// <summary>
/// Heuristic architecture graph builder. Classifies scanned files into nodes
/// (Angular components/services/modules/pipes/guards/interceptors and
/// .NET controllers/services/repositories/entities) and infers folder-level
/// "depends-on" edges from naming + import scanning of small text files.
/// Designed to run safely on partial inputs and without project-system parsing.
/// </summary>
public sealed class HeuristicGraphBuilder : IArchitectureGraphBuilder
{
    private static readonly Regex AngularImport =
        new(@"from\s+['""]([^'""]+)['""]", RegexOptions.Compiled);

    private static readonly Regex CsharpUsing =
        new(@"^\s*using\s+([A-Za-z0-9_.]+)\s*;", RegexOptions.Compiled | RegexOptions.Multiline);

    private const long MaxImportScanBytes = 64 * 1024;

    private readonly ILogger<HeuristicGraphBuilder> _logger;

    public HeuristicGraphBuilder(ILogger<HeuristicGraphBuilder> logger)
    {
        _logger = logger;
    }

    public async Task<ArchitectureGraph> BuildAsync(
        string sessionId,
        string rootPath,
        IReadOnlyList<CodeFile> files,
        CancellationToken ct = default)
    {
        var nodes = new Dictionary<string, GraphNode>(StringComparer.Ordinal);
        var edges = new HashSet<(string From, string To, string Kind)>();

        // Folder modules — one node per top-level / second-level folder.
        var moduleByPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            var moduleId = ModuleIdFor(file.RelativePath);
            moduleByPath[file.RelativePath] = moduleId;
            if (!nodes.ContainsKey(moduleId))
            {
                nodes[moduleId] = new GraphNode
                {
                    Id = moduleId,
                    Label = moduleId,
                    Kind = NodeKind.Module,
                };
            }
        }

        // Classify each file → typed node, link to its module.
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            var kind = ClassifyFile(file);
            if (kind is null) continue;

            var nodeId = "file:" + file.RelativePath;
            if (!nodes.ContainsKey(nodeId))
            {
                nodes[nodeId] = new GraphNode
                {
                    Id = nodeId,
                    Label = LabelFor(file.RelativePath),
                    Kind = kind.Value,
                    Metrics = new Dictionary<string, double>
                    {
                        ["lines"] = file.LineCount,
                        ["bytes"] = file.SizeBytes,
                    },
                };
            }

            edges.Add((moduleByPath[file.RelativePath], nodeId, "contains"));
        }

        // Folder-to-folder edges via import scanning of TS/JS/C# files.
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            if (file.SizeBytes > MaxImportScanBytes) continue;
            if (!IsImportScannable(file.Language)) continue;

            string text;
            try
            {
                text = await File.ReadAllTextAsync(Path.Combine(rootPath, file.RelativePath), ct)
                    .ConfigureAwait(false);
            }
            catch
            {
                continue;
            }

            var fromModule = moduleByPath[file.RelativePath];
            var imports = ExtractImports(file.Language, text);
            foreach (var import in imports)
            {
                var targetModule = ResolveImportTarget(import, fromModule, moduleByPath, file.RelativePath);
                if (targetModule is null || string.Equals(targetModule, fromModule, StringComparison.Ordinal))
                    continue;

                if (!nodes.ContainsKey(targetModule))
                {
                    nodes[targetModule] = new GraphNode
                    {
                        Id = targetModule,
                        Label = targetModule,
                        Kind = targetModule.StartsWith("ext:", StringComparison.Ordinal)
                            ? NodeKind.External
                            : NodeKind.Module,
                    };
                }

                edges.Add((fromModule, targetModule, "depends-on"));
            }
        }

        var graph = new ArchitectureGraph
        {
            Nodes = nodes.Values.ToList(),
            Edges = edges
                .Select(e => new GraphEdge { From = e.From, To = e.To, Kind = e.Kind })
                .ToList(),
        };

        _logger.LogInformation(
            "Session {SessionId}: built architecture graph ({Nodes} nodes, {Edges} edges)",
            sessionId, graph.Nodes.Count, graph.Edges.Count);

        return graph;
    }

    private static string ModuleIdFor(string relativePath)
    {
        var segs = relativePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segs.Length switch
        {
            0 => "(root)",
            1 => "(root)",
            2 => segs[0],
            _ => $"{segs[0]}/{segs[1]}",
        };
    }

    private static string LabelFor(string relativePath)
        => Path.GetFileName(relativePath);

    private static NodeKind? ClassifyFile(CodeFile file)
    {
        var path = file.RelativePath;
        var name = Path.GetFileName(path).ToLowerInvariant();
        var lang = file.Language;

        // Angular conventions
        if (lang is "typescript" or "tsx" or "javascript" or "jsx")
        {
            if (name.EndsWith(".component.ts") || name.EndsWith(".component.tsx") ||
                name.EndsWith(".component.html"))
                return NodeKind.Component;
            if (name.EndsWith(".service.ts")) return NodeKind.Service;
            if (name.EndsWith(".module.ts")) return NodeKind.Module;
            if (name.EndsWith(".guard.ts")) return NodeKind.Guard;
            if (name.EndsWith(".interceptor.ts")) return NodeKind.Interceptor;
            if (name.EndsWith(".pipe.ts")) return NodeKind.Pipe;
        }

        // .NET conventions
        if (lang == "csharp")
        {
            if (name.EndsWith("controller.cs")) return NodeKind.Controller;
            if (name.EndsWith("repository.cs")) return NodeKind.Repository;
            if (name.EndsWith("service.cs")) return NodeKind.Service;
            if (path.Contains("/Entities/", StringComparison.OrdinalIgnoreCase) ||
                path.Contains("/Domain/", StringComparison.OrdinalIgnoreCase))
                return NodeKind.Entity;
            return NodeKind.File;
        }

        return null;
    }

    private static bool IsImportScannable(string language) => language switch
    {
        "typescript" or "tsx" or "javascript" or "jsx" or "csharp" => true,
        _ => false,
    };

    private static IEnumerable<string> ExtractImports(string language, string text)
    {
        if (language == "csharp")
        {
            foreach (Match m in CsharpUsing.Matches(text))
                yield return m.Groups[1].Value;
        }
        else
        {
            foreach (Match m in AngularImport.Matches(text))
                yield return m.Groups[1].Value;
        }
    }

    private static string? ResolveImportTarget(
        string import,
        string fromModule,
        Dictionary<string, string> moduleByPath,
        string sourceRelative)
    {
        if (string.IsNullOrWhiteSpace(import)) return null;

        // Relative path import (TS/JS) → resolve to a sibling module.
        if (import.StartsWith('.'))
        {
            var sourceDir = Path.GetDirectoryName(sourceRelative)?.Replace('\\', '/') ?? "";
            var combined = NormalizeRelative(sourceDir, import);
            if (combined is null) return null;

            // Map to the closest known file path (ignore extension).
            var match = moduleByPath.Keys.FirstOrDefault(k =>
                k.Equals(combined, StringComparison.OrdinalIgnoreCase) ||
                k.StartsWith(combined + ".", StringComparison.OrdinalIgnoreCase) ||
                k.StartsWith(combined + "/", StringComparison.OrdinalIgnoreCase));

            return match is null ? null : moduleByPath[match];
        }

        // C# namespace — collapse to first two segments as a synthetic module key.
        if (import.Contains('.'))
        {
            var segs = import.Split('.', StringSplitOptions.RemoveEmptyEntries);
            return segs.Length >= 2 ? $"ns:{segs[0]}.{segs[1]}" : $"ns:{import}";
        }

        // Bare package import → external dependency.
        return $"ext:{import.Split('/')[0]}";
    }

    private static string? NormalizeRelative(string baseDir, string relative)
    {
        var parts = (baseDir + "/" + relative).Replace('\\', '/').Split('/');
        var stack = new Stack<string>();
        foreach (var part in parts)
        {
            if (string.IsNullOrEmpty(part) || part == ".") continue;
            if (part == "..")
            {
                if (stack.Count == 0) return null;
                stack.Pop();
                continue;
            }
            stack.Push(part);
        }
        return string.Join('/', stack.Reverse());
    }
}
