using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RepoIntel.Application.Abstractions;
using RepoIntel.Domain.Code;
using RepoIntel.Infrastructure.Configuration;

namespace RepoIntel.Infrastructure.Scanning;

/// <summary>
/// Walks a working directory, applies ignore rules and size/count caps,
/// and projects each kept file into a <see cref="CodeFile"/>.
/// </summary>
public sealed class FileScanner : IFileScanner
{
    private readonly IngestionOptions _options;
    private readonly ILogger<FileScanner> _logger;

    public FileScanner(IOptions<IngestionOptions> options, ILogger<FileScanner> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public Task<ScanResult> ScanAsync(string sessionId, string rootPath, CancellationToken ct = default)
    {
        if (!Directory.Exists(rootPath))
            throw new DirectoryNotFoundException($"Scan root not found: {rootPath}");

        var ignored = new HashSet<string>(_options.IgnoredFolders, StringComparer.OrdinalIgnoreCase);
        var files = new List<CodeFile>(capacity: 256);
        long totalBytes = 0;
        var skipped = 0;

        var stack = new Stack<string>();
        stack.Push(rootPath);

        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var current = stack.Pop();

            IEnumerable<string> subDirs;
            try { subDirs = Directory.EnumerateDirectories(current); }
            catch (UnauthorizedAccessException) { continue; }
            catch (DirectoryNotFoundException) { continue; }

            foreach (var dir in subDirs)
            {
                var name = Path.GetFileName(dir);
                if (string.IsNullOrEmpty(name)) continue;
                if (name.StartsWith('.') && !string.Equals(name, ".github", StringComparison.OrdinalIgnoreCase))
                {
                    // ignore .git, .vs, .idea, .next, etc.
                    continue;
                }
                if (ignored.Contains(name)) continue;
                stack.Push(dir);
            }

            IEnumerable<string> filePaths;
            try { filePaths = Directory.EnumerateFiles(current); }
            catch (UnauthorizedAccessException) { continue; }
            catch (DirectoryNotFoundException) { continue; }

            foreach (var path in filePaths)
            {
                ct.ThrowIfCancellationRequested();

                FileInfo fi;
                try { fi = new FileInfo(path); }
                catch { skipped++; continue; }

                if (!fi.Exists) { skipped++; continue; }

                if (fi.Length > _options.MaxFileSizeBytes)
                {
                    skipped++;
                    continue;
                }

                if (files.Count >= _options.MaxFiles)
                {
                    _logger.LogWarning(
                        "Session {SessionId}: hit MaxFiles cap ({MaxFiles}); stopping scan.",
                        sessionId, _options.MaxFiles);
                    return Task.FromResult(new ScanResult(sessionId, rootPath, files, skipped, totalBytes));
                }

                var rel = Path.GetRelativePath(rootPath, path).Replace('\\', '/');
                var ext = fi.Extension;
                var language = LanguageFromExtension(ext);
                var category = CategoryFor(rel, ext);

                int lineCount = 0;
                if (IsTextLike(ext))
                {
                    try { lineCount = CountLines(path, ct); }
                    catch { /* unreadable -> 0 lines */ }
                }

                files.Add(new CodeFile
                {
                    RelativePath = rel,
                    Language = language,
                    SizeBytes = fi.Length,
                    LineCount = lineCount,
                    Category = category,
                });

                totalBytes += fi.Length;

                if (totalBytes > _options.MaxRepoSizeBytes)
                {
                    _logger.LogWarning(
                        "Session {SessionId}: hit MaxRepoSizeBytes cap ({Cap}); stopping scan.",
                        sessionId, _options.MaxRepoSizeBytes);
                    return Task.FromResult(new ScanResult(sessionId, rootPath, files, skipped, totalBytes));
                }
            }
        }

        _logger.LogInformation(
            "Session {SessionId}: scanned {Count} files ({Bytes} bytes, {Skipped} skipped) under {Root}",
            sessionId, files.Count, totalBytes, skipped, rootPath);

        return Task.FromResult(new ScanResult(sessionId, rootPath, files, skipped, totalBytes));
    }

    private static int CountLines(string path, CancellationToken ct)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new StreamReader(stream);
        var count = 0;
        while (reader.ReadLine() is not null)
        {
            count++;
            if ((count & 0x3FFF) == 0) ct.ThrowIfCancellationRequested();
        }
        return count;
    }

    private static string LanguageFromExtension(string ext) => ext.ToLowerInvariant() switch
    {
        ".cs" => "csharp",
        ".ts" => "typescript",
        ".tsx" => "tsx",
        ".js" => "javascript",
        ".jsx" => "jsx",
        ".html" or ".htm" => "html",
        ".css" => "css",
        ".scss" => "scss",
        ".sass" => "sass",
        ".less" => "less",
        ".json" => "json",
        ".yml" or ".yaml" => "yaml",
        ".xml" => "xml",
        ".md" or ".markdown" => "markdown",
        ".py" => "python",
        ".java" => "java",
        ".kt" or ".kts" => "kotlin",
        ".go" => "go",
        ".rs" => "rust",
        ".rb" => "ruby",
        ".php" => "php",
        ".sql" => "sql",
        ".sh" or ".bash" => "shell",
        ".ps1" => "powershell",
        ".csproj" or ".sln" or ".props" or ".targets" => "msbuild",
        ".dockerfile" => "dockerfile",
        ".toml" => "toml",
        ".ini" => "ini",
        _ => "other",
    };

    private static bool IsTextLike(string ext)
    {
        // Skip line-counting on obvious binaries.
        return ext.ToLowerInvariant() is not (
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".ico" or ".webp" or ".svg" or
            ".pdf" or ".zip" or ".tar" or ".gz" or ".7z" or ".rar" or
            ".dll" or ".exe" or ".so" or ".dylib" or ".pdb" or ".class" or ".jar" or
            ".woff" or ".woff2" or ".ttf" or ".eot" or
            ".mp3" or ".mp4" or ".mov" or ".wav" or ".ogg");
    }

    private static string CategoryFor(string relPath, string ext)
    {
        var lower = relPath.ToLowerInvariant();

        if (lower.Contains("/test", StringComparison.Ordinal) ||
            lower.Contains(".test.", StringComparison.Ordinal) ||
            lower.Contains(".spec.", StringComparison.Ordinal) ||
            lower.EndsWith(".tests.cs", StringComparison.Ordinal))
        {
            return "test";
        }

        if (lower.StartsWith("docs/", StringComparison.Ordinal) ||
            ext.Equals(".md", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".markdown", StringComparison.OrdinalIgnoreCase))
        {
            return "docs";
        }

        if (ext is ".json" or ".yml" or ".yaml" or ".xml" or ".toml" or ".ini" or ".props" or ".targets" ||
            lower.EndsWith(".csproj", StringComparison.Ordinal) ||
            lower.EndsWith(".sln", StringComparison.Ordinal) ||
            lower.EndsWith("dockerfile", StringComparison.Ordinal) ||
            lower.EndsWith(".editorconfig", StringComparison.Ordinal))
        {
            return "config";
        }

        return "src";
    }
}
