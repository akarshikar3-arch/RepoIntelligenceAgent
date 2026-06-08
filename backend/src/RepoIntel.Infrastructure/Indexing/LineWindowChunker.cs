using Microsoft.Extensions.Logging;
using RepoIntel.Application.Abstractions;
using RepoIntel.Domain.Code;

namespace RepoIntel.Infrastructure.Indexing;

/// <summary>
/// Splits text source files into overlapping line windows. Suitable as a default
/// chunker before language-aware AST splitting is introduced.
/// </summary>
public sealed class LineWindowChunker : IChunker
{
    private const int WindowLines = 80;
    private const int OverlapLines = 10;
    private const int MaxCharsPerChunk = 4000;

    private static readonly HashSet<string> ChunkableLanguages = new(StringComparer.OrdinalIgnoreCase)
    {
        "TypeScript", "JavaScript", "C#", "HTML", "SCSS", "CSS", "Python", "Go",
        "Java", "Kotlin", "Ruby", "PHP", "Rust", "Markdown", "Json", "Yaml",
    };

    private readonly ILogger<LineWindowChunker> _logger;

    public LineWindowChunker(ILogger<LineWindowChunker> logger)
    {
        _logger = logger;
    }

    public async Task<IReadOnlyList<CodeChunk>> ChunkAsync(
        string sessionId,
        string rootPath,
        IReadOnlyList<CodeFile> files,
        CancellationToken ct = default)
    {
        var chunks = new List<CodeChunk>(capacity: files.Count * 2);

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();

            if (!ChunkableLanguages.Contains(file.Language)) continue;
            if (file.Category is not ("src" or "docs" or "config")) continue;

            var fullPath = Path.Combine(rootPath, file.RelativePath);
            if (!File.Exists(fullPath)) continue;

            string text;
            try
            {
                text = await File.ReadAllTextAsync(fullPath, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Skipping unreadable file {Path}", file.RelativePath);
                continue;
            }

            if (string.IsNullOrWhiteSpace(text)) continue;

            var lines = text.Split('\n');
            for (int start = 0; start < lines.Length; start += WindowLines - OverlapLines)
            {
                var end = Math.Min(start + WindowLines, lines.Length);
                var slice = string.Join('\n', lines, start, end - start);
                if (slice.Length > MaxCharsPerChunk)
                    slice = slice[..MaxCharsPerChunk];
                if (string.IsNullOrWhiteSpace(slice)) { if (end == lines.Length) break; else continue; }

                chunks.Add(new CodeChunk
                {
                    Id = $"{sessionId}:{file.RelativePath}:{start + 1}-{end}",
                    SessionId = sessionId,
                    FilePath = file.RelativePath,
                    StartLine = start + 1,
                    EndLine = end,
                    Content = slice,
                });

                if (end == lines.Length) break;
            }
        }

        return chunks;
    }
}
