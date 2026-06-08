namespace RepoIntel.Application.Abstractions;

public sealed record ChatMessage(string Role, string Content);

public sealed record LlmOptions(string? Model = null, double Temperature = 0.2, int MaxTokens = 1024);

public sealed record TokenChunk(string Delta, bool IsFinal = false);

public interface ILlmProvider
{
    IAsyncEnumerable<TokenChunk> ChatAsync(
        IReadOnlyList<ChatMessage> messages,
        LlmOptions options,
        CancellationToken ct = default);
}
