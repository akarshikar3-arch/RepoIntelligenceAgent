namespace RepoIntel.Domain.Code;

public sealed class CodeFile
{
    public required string RelativePath { get; init; }
    public required string Language { get; init; }
    public required long SizeBytes { get; init; }
    public int LineCount { get; init; }
    public string? Category { get; init; } // src | test | config | docs
}

public sealed class CodeChunk
{
    public required string Id { get; init; }
    public required string SessionId { get; init; }
    public required string FilePath { get; init; }
    public required int StartLine { get; init; }
    public required int EndLine { get; init; }
    public required string Content { get; init; }
    public float[]? Embedding { get; set; }
}
