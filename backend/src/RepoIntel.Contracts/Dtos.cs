namespace RepoIntel.Contracts;

public enum SessionKindDto { Repo, Snippet }
public enum SessionStatusDto { Pending, Running, Ready, Error }
public enum SeverityDto { Info, Low, Medium, High, Critical }

public sealed record SnippetFileDto(string Name, string? Language, string Content);

public sealed record RepoIngestRequest(string Url, string? Branch, string? ReviewProfile = null);
public sealed record SnippetIngestRequest(IReadOnlyList<SnippetFileDto> Files, string? ReviewProfile = null);
public sealed record IngestResponse(string SessionId);

public sealed record SessionSummaryDto(
    string Id,
    SessionKindDto Kind,
    string Title,
    string? Framework,
    string? PrimaryLanguage,
    int? HealthScore,
    int? ConfidenceScore,
    DateTimeOffset CreatedAt,
    SessionStatusDto Status);

public sealed record FileTypeBucket(string Extension, int Count, long Bytes);
public sealed record LanguageBucket(string Language, int Files, long Bytes);
public sealed record CategoryBreakdown(int Source, int Test, int Config, int Docs);
public sealed record AngularBreakdown(int Components, int Services, int Modules, int Guards, int Interceptors, int Pipes);
public sealed record DotNetBreakdown(int Controllers, int Services, int Entities, int Repositories);
public sealed record SeverityCount(SeverityDto Severity, int Count);
public sealed record LargestFile(string Path, long Bytes, int Lines);

public sealed record DashboardMetricsDto(
    int TotalFiles,
    int TotalFolders,
    IReadOnlyList<FileTypeBucket> FileTypes,
    IReadOnlyList<LanguageBucket> Languages,
    CategoryBreakdown Categories,
    AngularBreakdown? Angular,
    DotNetBreakdown? DotNet,
    IReadOnlyList<SeverityCount> FindingsBySeverity,
    IReadOnlyList<LargestFile> LargestFiles);

public sealed record DashboardOverviewDto(
    SessionSummaryDto Session,
    DashboardMetricsDto Metrics,
    IReadOnlyList<FindingDto> Findings);

public sealed record FindingDto(
    string Id,
    string RuleId,
    SeverityDto Severity,
    string Category,
    string File,
    int Line,
    int Column,
    string Message,
    string? Snippet,
    string? FixHint);

public sealed record GraphNodeDto(string Id, string Label, string Kind, IReadOnlyDictionary<string, double>? Metrics);
public sealed record GraphEdgeDto(string From, string To, string Kind);
public sealed record ArchitectureGraphDto(IReadOnlyList<GraphNodeDto> Nodes, IReadOnlyList<GraphEdgeDto> Edges);

public enum PipelineStageDto
{
    Validate, Fetch, Scan, Metadata, Architecture, Analyze, Chunk, Embed, Index, Dashboard
}
public enum StageStatusDto { Pending, Running, Done, Error }

public sealed record PipelineProgressEvent(
    PipelineStageDto Stage,
    StageStatusDto Status,
    int Percent,
    string? Message);

public sealed record FileTreeNodeDto(string Name, string Path, bool IsDirectory, long? SizeBytes, IReadOnlyList<FileTreeNodeDto>? Children);

public sealed record ChatMessageDto(string Role, string Content);
public sealed record ChatRequest(string Question, IReadOnlyList<ChatMessageDto>? History = null);
public sealed record ChatCitation(string File, int StartLine, int EndLine, double Score);
public sealed record ChatTurnDto(string Role, string Content, IReadOnlyList<ChatCitation>? Citations);
public sealed record ChatResponse(string Content, IReadOnlyList<ChatCitation> Citations, bool Grounded);
