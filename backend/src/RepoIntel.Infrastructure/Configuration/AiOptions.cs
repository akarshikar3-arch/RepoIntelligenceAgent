namespace RepoIntel.Infrastructure.Configuration;

public sealed class AiOptions
{
    public const string SectionName = "AI";

    public LlmOptionsSection Llm { get; set; } = new();
    public EmbeddingsOptionsSection Embeddings { get; set; } = new();
    public VectorStoreOptionsSection VectorStore { get; set; } = new();

    public sealed class LlmOptionsSection
    {
        public string Provider { get; set; } = "Groq";
        public string Model { get; set; } = "llama-3.1-8b-instant";
        public string? ApiKey { get; set; }
        public string? BaseUrl { get; set; }
    }

    public sealed class EmbeddingsOptionsSection
    {
        public string Provider { get; set; } = "Local";
        public string Model { get; set; } = "all-MiniLM-L6-v2";
        public string? ApiKey { get; set; }
    }

    public sealed class VectorStoreOptionsSection
    {
        public string Provider { get; set; } = "InMemory";
        public string? ConnectionString { get; set; }
    }
}

public sealed class IngestionOptions
{
    public const string SectionName = "Ingestion";

    public long MaxRepoSizeBytes { get; set; } = 200 * 1024 * 1024; // 200 MB
    public long MaxFileSizeBytes { get; set; } = 1 * 1024 * 1024;   // 1 MB
    public int MaxFiles { get; set; } = 5000;
    public string TempRoot { get; set; } = Path.Combine(Path.GetTempPath(), "repointel");
    public string[] AllowedHosts { get; set; } = ["github.com"];
    public string[] IgnoredFolders { get; set; } =
        ["node_modules", "dist", "build", ".git", "coverage", "bin", "obj", ".vs", ".idea", ".next", "out"];
}
