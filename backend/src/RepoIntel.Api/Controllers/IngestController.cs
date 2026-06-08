using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using RepoIntel.Application.Abstractions;
using RepoIntel.Contracts;
using RepoIntel.Domain.Sessions;
using RepoIntel.Infrastructure.Configuration;

namespace RepoIntel.Api.Controllers;

[ApiController]
[Route("api/ingest")]
[Produces("application/json")]
public sealed class IngestController : ControllerBase
{
    private readonly ISessionStore _sessions;
    private readonly IAnalysisPipeline _pipeline;
    private readonly IngestionOptions _ingestion;
    private readonly ILogger<IngestController> _logger;

    public IngestController(
        ISessionStore sessions,
        IAnalysisPipeline pipeline,
        IOptions<IngestionOptions> ingestion,
        ILogger<IngestController> logger)
    {
        _sessions = sessions;
        _pipeline = pipeline;
        _ingestion = ingestion.Value;
        _logger = logger;
    }

    /// <summary>Accept a GitHub repository URL and start the analysis pipeline.</summary>
    [HttpPost("repo")]
    [ProducesResponseType(typeof(IngestResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IngestResponse>> IngestRepo(
        [FromBody] RepoIngestRequest request,
        CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Url))
            return InvalidIngest("A repository URL is required.");

        if (!Uri.TryCreate(request.Url.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return InvalidIngest("Repository URL must be an absolute http(s) URL.");
        }

        if (_ingestion.AllowedHosts.Length > 0 &&
            !_ingestion.AllowedHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
        {
            return InvalidIngest(
                $"Host '{uri.Host}' is not allowed. Allowed: {string.Join(", ", _ingestion.AllowedHosts)}.");
        }

        var session = new RepoSession
        {
            Id = NewSessionId(),
            Kind = SessionKind.Repo,
            Title = DeriveRepoTitle(uri),
            Status = SessionStatus.Pending,
        };
        await _sessions.SetAsync(session, ct).ConfigureAwait(false);

        _logger.LogInformation("Created repo session {SessionId} for {Url}", session.Id, uri);

        _ = Task.Run(
            () => _pipeline.RunRepoAsync(session.Id, request, CancellationToken.None),
            CancellationToken.None);

        return Accepted(new IngestResponse(session.Id));
    }

    /// <summary>Accept pasted code snippets and start the analysis pipeline.</summary>
    [HttpPost("snippet")]
    [ProducesResponseType(typeof(IngestResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IngestResponse>> IngestSnippet(
        [FromBody] SnippetIngestRequest request,
        CancellationToken ct)
    {
        if (request is null || request.Files is null || request.Files.Count == 0)
            return InvalidIngest("At least one snippet file is required.");

        if (request.Files.Count > _ingestion.MaxFiles)
            return InvalidIngest($"Snippet exceeds maximum file count ({_ingestion.MaxFiles}).");

        long total = 0;
        foreach (var file in request.Files)
        {
            if (string.IsNullOrWhiteSpace(file.Name))
                return InvalidIngest("Every snippet file must have a name.");
            if (file.Content is null)
                return InvalidIngest($"File '{file.Name}' has null content.");

            var size = System.Text.Encoding.UTF8.GetByteCount(file.Content);
            if (size > _ingestion.MaxFileSizeBytes)
                return InvalidIngest(
                    $"File '{file.Name}' exceeds max file size ({_ingestion.MaxFileSizeBytes} bytes).");
            total += size;
        }

        if (total > _ingestion.MaxRepoSizeBytes)
            return InvalidIngest($"Snippet payload exceeds max total size ({_ingestion.MaxRepoSizeBytes} bytes).");

        var session = new RepoSession
        {
            Id = NewSessionId(),
            Kind = SessionKind.Snippet,
            Title = request.Files.Count == 1
                ? request.Files[0].Name
                : $"Snippet ({request.Files.Count} files)",
            Status = SessionStatus.Pending,
        };
        await _sessions.SetAsync(session, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Created snippet session {SessionId} ({FileCount} files)",
            session.Id, request.Files.Count);

        _ = Task.Run(
            () => _pipeline.RunSnippetAsync(session.Id, request, CancellationToken.None),
            CancellationToken.None);

        return Accepted(new IngestResponse(session.Id));
    }

    private static string NewSessionId() => Guid.NewGuid().ToString("N");

    private static string DeriveRepoTitle(Uri uri)
    {
        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length >= 2)
        {
            var repo = segments[1];
            if (repo.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
                repo = repo[..^4];
            return $"{segments[0]}/{repo}";
        }
        return uri.Host + uri.AbsolutePath;
    }

    private ActionResult<IngestResponse> InvalidIngest(string detail) =>
        BadRequest(new ProblemDetails
        {
            Title = "Invalid ingest request",
            Detail = detail,
            Status = StatusCodes.Status400BadRequest,
        });
}
