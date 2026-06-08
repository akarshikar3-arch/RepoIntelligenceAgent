using System.Text;
using RepoIntel.Application.Abstractions;

namespace RepoIntel.Infrastructure.Indexing;

/// <summary>
/// Deterministic, dependency-free embedding fallback. Uses a hashed bag-of-tokens
/// projection into a fixed-dimension vector with sublinear term weighting and L2
/// normalization. Good enough for ranking similarity over a single repo when no
/// external embedding service is configured. Replaceable via DI.
/// </summary>
public sealed class HashingEmbeddingProvider : IEmbeddingProvider
{
    public int Dimensions => 384;

    public Task<float[][]> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        var result = new float[texts.Count][];
        for (var i = 0; i < texts.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            result[i] = Embed(texts[i]);
        }
        return Task.FromResult(result);
    }

    private float[] Embed(string text)
    {
        var vec = new float[Dimensions];
        if (string.IsNullOrWhiteSpace(text)) return vec;

        foreach (var token in Tokenize(text))
        {
            var h = unchecked((int)(Hash(token) & 0x7fffffff));
            var bucket = h % Dimensions;
            var sign = (h & 1) == 0 ? 1f : -1f;
            vec[bucket] += sign;
        }

        // sublinear scaling + L2 normalize
        double norm = 0;
        for (var i = 0; i < vec.Length; i++)
        {
            vec[i] = vec[i] >= 0
                ? MathF.Log(1 + vec[i])
                : -MathF.Log(1 + -vec[i]);
            norm += vec[i] * vec[i];
        }
        if (norm > 0)
        {
            var inv = 1f / MathF.Sqrt((float)norm);
            for (var i = 0; i < vec.Length; i++) vec[i] *= inv;
        }
        return vec;
    }

    private static IEnumerable<string> Tokenize(string text)
    {
        var sb = new StringBuilder(32);
        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch) || ch == '_')
            {
                sb.Append(char.ToLowerInvariant(ch));
            }
            else if (sb.Length > 0)
            {
                if (sb.Length >= 2) yield return sb.ToString();
                sb.Clear();
            }
        }
        if (sb.Length >= 2) yield return sb.ToString();
    }

    // FNV-1a 64-bit
    private static ulong Hash(string s)
    {
        unchecked
        {
            const ulong offset = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            ulong h = offset;
            foreach (var c in s) { h ^= c; h *= prime; }
            return h;
        }
    }
}
