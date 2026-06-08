namespace RepoIntel.Application.Abstractions;

public interface IEmbeddingProvider
{
    int Dimensions { get; }
    Task<float[][]> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default);
}
