using RepoIntel.Domain.Code;

namespace RepoIntel.Domain.Sessions;

public sealed record RepoAnalysisSnapshot(
    IReadOnlyList<CodeFile> Files,
    string? Framework,
    string? PrimaryLanguage,
    AngularSnapshot? Angular,
    DotNetSnapshot? DotNet,
    int SkippedCount,
    long TotalBytes);

public sealed record AngularSnapshot(
    string? AngularJsonPath,
    string? AngularVersion,
    IReadOnlyList<string> Projects);

public sealed record DotNetSnapshot(
    IReadOnlyList<string> SolutionFiles,
    IReadOnlyList<string> ProjectFiles,
    IReadOnlyList<string> TargetFrameworks);
