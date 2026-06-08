using System.Diagnostics;
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
}
