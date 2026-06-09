using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RepoIntel.Application.Abstractions;
using RepoIntel.Infrastructure.Configuration;

namespace RepoIntel.Infrastructure.Ingestion;

/// <summary>
/// Clones a remote git repository (shallow, single-branch) into a session-scoped
/// temp directory. Requires the `git` executable on PATH.
/// </summary>
public sealed class GitRepoFetcher : IRepoFetcher
{
    private readonly IngestionOptions _options;
    private readonly ILogger<GitRepoFetcher> _logger;

    public GitRepoFetcher(IOptions<IngestionOptions> options, ILogger<GitRepoFetcher> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<LocalRepoHandle> FetchAsync(
        string sessionId,
        string url,
        string? branch,
        CancellationToken ct = default)
    {
        var tempRoot = ResolveTempRoot();
        Directory.CreateDirectory(tempRoot);
        var workDir = Path.Combine(tempRoot, sessionId);
        if (Directory.Exists(workDir))
        {
            try { Directory.Delete(workDir, recursive: true); } catch { /* best-effort */ }
        }
        Directory.CreateDirectory(workDir);

        var args = new List<string>
        {
            "-c", "core.longpaths=true",
            "clone",
            "--depth", "1",
            "--single-branch",
            "--no-tags",
        };
        if (!string.IsNullOrWhiteSpace(branch))
        {
            args.Add("--branch");
            args.Add(branch);
        }
        args.Add(url);
        args.Add(workDir);

        var psi = new ProcessStartInfo("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        _logger.LogInformation("Session {SessionId}: cloning {Url} into {WorkDir}", sessionId, url, workDir);

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        try
        {
            proc.Start();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Session {SessionId}: git executable unavailable; attempting GitHub archive fallback",
                sessionId);

            if (TryParseGithubRepo(url, out var owner, out var repo))
            {
                await FetchViaGithubArchiveAsync(sessionId, owner, repo, branch, workDir, ct).ConfigureAwait(false);
                return new LocalRepoHandle(sessionId, workDir, branch, null);
            }

            throw new InvalidOperationException(
                "Failed to start 'git'. Ensure Git is installed and on PATH.", ex);
        }

        var stderrTask = proc.StandardError.ReadToEndAsync(ct);
        var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct).ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        _ = await stdoutTask.ConfigureAwait(false);

        if (proc.ExitCode != 0)
        {
            try { Directory.Delete(workDir, recursive: true); } catch { }

            var details = Truncate(stderr, 500);
            if (IsLikelyWindowsPathLengthFailure(stderr))
            {
                details += " (Likely Windows path-length limitation while cloning deep repository paths.)";
            }

            throw new InvalidOperationException(
                $"git clone failed (exit {proc.ExitCode}): {details}");
        }

        string? commit = null;
        try { commit = await ReadHeadShaAsync(workDir, ct).ConfigureAwait(false); } catch { }

        return new LocalRepoHandle(sessionId, workDir, branch, commit);
    }

    private static async Task<string?> ReadHeadShaAsync(string workDir, CancellationToken ct)
    {
        var headPath = Path.Combine(workDir, ".git", "HEAD");
        if (!File.Exists(headPath)) return null;
        var head = (await File.ReadAllTextAsync(headPath, ct).ConfigureAwait(false)).Trim();
        if (head.StartsWith("ref:", StringComparison.Ordinal))
        {
            var refPath = Path.Combine(workDir, ".git", head[4..].Trim());
            if (File.Exists(refPath))
                return (await File.ReadAllTextAsync(refPath, ct).ConfigureAwait(false)).Trim();
            return null;
        }
        return head;
    }

    private string ResolveTempRoot()
    {
        if (!OperatingSystem.IsWindows())
            return _options.TempRoot;

        // Keep clone paths short on Windows to avoid deep-path checkout failures.
        const string shortRoot = @"C:\ri";
        return _options.TempRoot.Length > shortRoot.Length
            ? shortRoot
            : _options.TempRoot;
    }

    private static bool IsLikelyWindowsPathLengthFailure(string stderr)
    {
        if (string.IsNullOrWhiteSpace(stderr))
            return false;

        return stderr.Contains("unable to create file", StringComparison.OrdinalIgnoreCase)
            || stderr.Contains("cannot create directory", StringComparison.OrdinalIgnoreCase)
            || stderr.Contains("Filename too long", StringComparison.OrdinalIgnoreCase)
            || stderr.Contains("File name too long", StringComparison.OrdinalIgnoreCase);
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) ? s : s.Length <= max ? s : s[..max] + "…";

    private static bool TryParseGithubRepo(string url, out string owner, out string repo)
    {
        owner = string.Empty;
        repo = string.Empty;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        if (!string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
            return false;

        var segs = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segs.Length < 2)
            return false;

        owner = segs[0];
        repo = segs[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase)
            ? segs[1][..^4]
            : segs[1];

        return owner.Length > 0 && repo.Length > 0;
    }

    private async Task FetchViaGithubArchiveAsync(
        string sessionId,
        string owner,
        string repo,
        string? branch,
        string workDir,
        CancellationToken ct)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("RepoIntel", "1.0"));

        var branchCandidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(branch))
            branchCandidates.Add(branch.Trim());

        var defaultBranch = await TryGetGithubDefaultBranchAsync(http, owner, repo, ct).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(defaultBranch))
            branchCandidates.Add(defaultBranch);

        branchCandidates.Add("main");
        branchCandidates.Add("master");

        branchCandidates = branchCandidates
            .Where(b => !string.IsNullOrWhiteSpace(b))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var tempRoot = Path.Combine(Path.GetTempPath(), "repointel-archive", sessionId);
        var zipPath = Path.Combine(tempRoot, "repo.zip");
        var unzipPath = Path.Combine(tempRoot, "unzipped");

        if (Directory.Exists(tempRoot))
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
        Directory.CreateDirectory(tempRoot);

        HttpStatusCode? lastStatus = null;
        string? lastArchiveUrl = null;

        foreach (var candidate in branchCandidates)
        {
            var archiveUrl = $"https://codeload.github.com/{owner}/{repo}/zip/refs/heads/{candidate}";
            lastArchiveUrl = archiveUrl;

            _logger.LogInformation(
                "Session {SessionId}: downloading GitHub archive {ArchiveUrl}",
                sessionId,
                archiveUrl);

            using var resp = await http.GetAsync(
                archiveUrl,
                HttpCompletionOption.ResponseHeadersRead,
                ct).ConfigureAwait(false);

            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                lastStatus = resp.StatusCode;
                continue;
            }

            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                throw new InvalidOperationException(
                    $"GitHub archive download failed ({(int)resp.StatusCode}) for '{candidate}': {Truncate(body, 250)}");
            }

            await using (var fs = File.Create(zipPath))
            await using (var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            {
                await stream.CopyToAsync(fs, ct).ConfigureAwait(false);
            }

            Directory.CreateDirectory(unzipPath);
            ZipFile.ExtractToDirectory(zipPath, unzipPath, overwriteFiles: true);

            var extractedRoot = Directory
                .EnumerateDirectories(unzipPath)
                .FirstOrDefault() ?? unzipPath;

            CopyDirectoryContents(extractedRoot, workDir);
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
            return;
        }

        throw new InvalidOperationException(
            $"GitHub archive download failed ({(lastStatus.HasValue ? (int)lastStatus.Value : 0)}) for all branch candidates ({string.Join(", ", branchCandidates)}). Last URL: {lastArchiveUrl}");
    }

    private static async Task<string?> TryGetGithubDefaultBranchAsync(
        HttpClient http,
        string owner,
        string repo,
        CancellationToken ct)
    {
        var url = $"https://api.github.com/repos/{owner}/{repo}";

        using var resp = await http.GetAsync(url, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            return null;

        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
        if (doc.RootElement.TryGetProperty("default_branch", out var prop) && prop.ValueKind == JsonValueKind.String)
            return prop.GetString();

        return null;
    }

    private static void CopyDirectoryContents(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);

        foreach (var dir in Directory.EnumerateDirectories(sourceDir, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(sourceDir, dir);
            Directory.CreateDirectory(Path.Combine(targetDir, rel));
        }

        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(sourceDir, file);
            var dest = Path.Combine(targetDir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, overwrite: true);
        }
    }
}
