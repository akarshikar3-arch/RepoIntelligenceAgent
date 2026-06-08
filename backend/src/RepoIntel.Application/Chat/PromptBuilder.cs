using System.Text;
using RepoIntel.Application.Abstractions;

namespace RepoIntel.Application.Chat;

internal static class PromptBuilder
{
    // Keep context compact to reduce LLM token pressure and 429 rate limits.
    private const int MaxContextChars = 6_000;

    public const string SystemPrompt =
        "You are RepoIntel, an assistant that answers strictly from the provided repository excerpts. " +
        "Rules:\n" +
        "1. Use ONLY the CONTEXT below. Do not rely on outside knowledge of specific projects.\n" +
        "2. Cite sources inline using square brackets like [1], [2] matching the numbered excerpts.\n" +
        "3. If the context does not contain enough information, reply: " +
        "\"I can't find that in the indexed repository.\" and stop.\n" +
        "3a. For questions about how to run/use/operate/install a project, answer only if context includes operational docs (README/usage/install/quickstart/docs). Otherwise use rule 3.\n" +
        "4. Prefer concise, technical answers. Quote short code spans only when essential.\n" +
        "5. Never fabricate file paths, function names, or line numbers.";

    public static string BuildContextBlock(IReadOnlyList<ScoredChunk> chunks)
    {
        if (chunks.Count == 0) return "(no context retrieved)";

        var sb = new StringBuilder();
        var budget = MaxContextChars;
        for (var i = 0; i < chunks.Count; i++)
        {
            var c = chunks[i].Chunk;
            var header = $"[{i + 1}] {c.FilePath}:{c.StartLine}-{c.EndLine}\n";
            var body = c.Content;
            if (body.Length > budget - header.Length - 32)
                body = body[..Math.Max(0, budget - header.Length - 32)] + "\n…[truncated]";
            sb.Append(header).Append("```\n").Append(body).Append("\n```\n\n");
            budget -= header.Length + body.Length + 10;
            if (budget <= 0) break;
        }
        return sb.ToString();
    }

    public static string BuildUserPrompt(string question, string contextBlock) =>
        $"CONTEXT:\n{contextBlock}\nQUESTION: {question}\n\nAnswer using only the CONTEXT above. Cite with [n].";
}
