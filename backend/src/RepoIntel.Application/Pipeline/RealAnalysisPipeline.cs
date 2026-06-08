using Microsoft.Extensions.Logging;
using RepoIntel.Application.Abstractions;
using RepoIntel.Contracts;
using RepoIntel.Domain.Sessions;

namespace RepoIntel.Application.Pipeline;

/// <summary>
/// Concrete pipeline that fetches/materializes the working directory, scans files,
/// extracts metadata and builds the architecture graph. Results are persisted on
/// the session so dashboard endpoints can render them.
/// </summary>
public sealed class RealAnalysisPipeline : IAnalysisPipeline
{
    private readonly ISessionStore _sessions;
    private readonly IRepoFetcher _fetcher;
    private readonly IFileScanner _scanner;
    private readonly IMetadataExtractor _metadata;
    private readonly IArchitectureGraphBuilder _graph;
    private readonly IChunker _chunker;
    private readonly IEmbeddingProvider _embeddings;
    private readonly IVectorStore _vectorStore;
    private readonly ILogger<RealAnalysisPipeline> _logger;

    public RealAnalysisPipeline(
        ISessionStore sessions,
        IRepoFetcher fetcher,
        IFileScanner scanner,
        IMetadataExtractor metadata,
        IArchitectureGraphBuilder graph,
        IChunker chunker,
        IEmbeddingProvider embeddings,
        IVectorStore vectorStore,
        ILogger<RealAnalysisPipeline> logger)
    {
        _sessions = sessions;
        _fetcher = fetcher;
        _scanner = scanner;
        _metadata = metadata;
        _graph = graph;
        _chunker = chunker;
        _embeddings = embeddings;
        _vectorStore = vectorStore;
        _logger = logger;
    }

    public async Task RunRepoAsync(string sessionId, RepoIngestRequest request, CancellationToken ct = default)
    {
        var session = await _sessions.GetAsync(sessionId, ct).ConfigureAwait(false);
        if (session is null)
        {
            _logger.LogWarning("Pipeline started for unknown session {SessionId}", sessionId);
            return;
        }

        await SetStatus(session, SessionStatus.Running, ct).ConfigureAwait(false);

        LocalRepoHandle? handle = null;
        try
        {
            handle = await _fetcher.FetchAsync(sessionId, request.Url, request.Branch, ct).ConfigureAwait(false);
            session.WorkingDirectory = handle.WorkingDirectory;
            await AnalyzeAsync(session, handle.WorkingDirectory, request.ReviewProfile, ct).ConfigureAwait(false);
            await SetStatus(session, SessionStatus.Ready, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Repo pipeline failed for session {SessionId}", sessionId);
            session.Error = ex.Message;
            await SetStatus(session, SessionStatus.Error, ct).ConfigureAwait(false);
        }
    }

    public async Task RunSnippetAsync(string sessionId, SnippetIngestRequest request, CancellationToken ct = default)
    {
        var session = await _sessions.GetAsync(sessionId, ct).ConfigureAwait(false);
        if (session is null)
        {
            _logger.LogWarning("Pipeline started for unknown session {SessionId}", sessionId);
            return;
        }

        await SetStatus(session, SessionStatus.Running, ct).ConfigureAwait(false);

        var workDir = Path.Combine(Path.GetTempPath(), "repointel", sessionId);
        try
        {
            if (Directory.Exists(workDir))
                Directory.Delete(workDir, recursive: true);
            Directory.CreateDirectory(workDir);

            foreach (var file in request.Files)
            {
                ct.ThrowIfCancellationRequested();
                var safeName = SanitizeRelative(file.Name);
                var fullPath = Path.Combine(workDir, safeName);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                await File.WriteAllTextAsync(fullPath, file.Content, ct).ConfigureAwait(false);
            }

            session.WorkingDirectory = workDir;
            await AnalyzeAsync(session, workDir, request.ReviewProfile, ct).ConfigureAwait(false);
            await SetStatus(session, SessionStatus.Ready, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Snippet pipeline failed for session {SessionId}", sessionId);
            session.Error = ex.Message;
            await SetStatus(session, SessionStatus.Error, ct).ConfigureAwait(false);
        }
    }

    private async Task AnalyzeAsync(RepoSession session, string rootPath, string? reviewProfile, CancellationToken ct)
    {
        var scan = await _scanner.ScanAsync(session.Id, rootPath, ct).ConfigureAwait(false);
        var meta = await _metadata.ExtractAsync(session.Id, rootPath, scan.Files, ct).ConfigureAwait(false);
        var graph = await _graph.BuildAsync(session.Id, rootPath, scan.Files, ct).ConfigureAwait(false);
        var review = HeuristicCodeReviewEngine.Analyze(rootPath, scan.Files, reviewProfile);

        session.Framework = meta.Framework.ToString();
        session.PrimaryLanguage = meta.PrimaryLanguage;
        session.Architecture = graph;
        session.Findings = review.Findings;
        session.HealthScore = (int)Math.Round(review.Score * 10);
        session.ConfidenceScore = Math.Clamp(60 + (scan.Files.Count / 4), 0, 100);
        session.Analysis = new RepoAnalysisSnapshot(
            scan.Files,
            meta.Framework.ToString(),
            meta.PrimaryLanguage,
            meta.Angular is null
                ? null
                : new AngularSnapshot(meta.Angular.AngularJsonPath, meta.Angular.AngularVersion, meta.Angular.Projects),
            meta.DotNet is null
                ? null
                : new DotNetSnapshot(meta.DotNet.SolutionFiles, meta.DotNet.ProjectFiles, meta.DotNet.TargetFrameworks),
            scan.SkippedCount,
            scan.TotalBytes);

        await IndexAsync(session.Id, rootPath, scan.Files, ct).ConfigureAwait(false);
    }

    private async Task IndexAsync(string sessionId, string rootPath, IReadOnlyList<Domain.Code.CodeFile> files, CancellationToken ct)
    {
        try
        {
            var chunks = await _chunker.ChunkAsync(sessionId, rootPath, files, ct).ConfigureAwait(false);
            if (chunks.Count == 0)
            {
                _logger.LogInformation("Session {SessionId}: no chunks produced; skipping embedding", sessionId);
                await _vectorStore.UpsertAsync(sessionId, Array.Empty<Domain.Code.CodeChunk>(), ct).ConfigureAwait(false);
                return;
            }

            const int batchSize = 64;
            for (var i = 0; i < chunks.Count; i += batchSize)
            {
                ct.ThrowIfCancellationRequested();
                var batch = chunks.Skip(i).Take(batchSize).ToArray();
                var vectors = await _embeddings
                    .EmbedBatchAsync(batch.Select(c => c.Content).ToArray(), ct)
                    .ConfigureAwait(false);
                for (var j = 0; j < batch.Length && j < vectors.Length; j++)
                    batch[j].Embedding = vectors[j];
            }

            await _vectorStore.UpsertAsync(sessionId, chunks, ct).ConfigureAwait(false);
            _logger.LogInformation(
                "Session {SessionId}: indexed {ChunkCount} chunks across {FileCount} files (dim={Dim})",
                sessionId, chunks.Count, chunks.Select(c => c.FilePath).Distinct().Count(), _embeddings.Dimensions);
        }
        catch (Exception ex)
        {
            // Indexing failures should not fail the whole analysis; chat will degrade gracefully.
            _logger.LogWarning(ex, "Session {SessionId}: indexing failed", sessionId);
        }
    }

    private async Task SetStatus(RepoSession session, SessionStatus status, CancellationToken ct)
    {
        session.Status = status;
        await _sessions.SetAsync(session, ct).ConfigureAwait(false);
    }

    private static string SanitizeRelative(string name)
    {
        var normalized = name.Replace('\\', '/').Trim('/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var safe = segments
            .Where(s => s != "." && s != "..")
            .Select(s => string.Concat(s.Where(c => !Path.GetInvalidFileNameChars().Contains(c))))
            .Where(s => s.Length > 0)
            .ToArray();
        return safe.Length == 0 ? "snippet.txt" : Path.Combine(safe);
    }
}
