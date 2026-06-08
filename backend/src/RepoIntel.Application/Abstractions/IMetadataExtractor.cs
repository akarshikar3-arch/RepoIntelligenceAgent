using RepoIntel.Domain.Code;

namespace RepoIntel.Application.Abstractions;

public enum DetectedFramework { Unknown, Angular, DotNet, Mixed }

public sealed record RepoMetadata(
    string SessionId,
    DetectedFramework Framework,
    string? PrimaryLanguage,
    AngularInfo? Angular,
    DotNetInfo? DotNet,
    IReadOnlyDictionary<string, int> LanguageFileCounts);

public sealed record AngularInfo(
    string? AngularJsonPath,
    string? AngularVersion,
    IReadOnlyList<string> Projects);

public sealed record DotNetInfo(
    IReadOnlyList<string> SolutionFiles,
    IReadOnlyList<string> ProjectFiles,
    IReadOnlyList<string> TargetFrameworks);

public interface IMetadataExtractor
{
    Task<RepoMetadata> ExtractAsync(
        string sessionId,
        string rootPath,
        IReadOnlyList<CodeFile> files,
        CancellationToken ct = default);
}
