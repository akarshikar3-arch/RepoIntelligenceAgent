using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RepoIntel.Application.Abstractions;
using RepoIntel.Infrastructure.Architecture;
using RepoIntel.Infrastructure.Chat;
using RepoIntel.Infrastructure.Configuration;
using RepoIntel.Infrastructure.Indexing;
using RepoIntel.Infrastructure.Ingestion;
using RepoIntel.Infrastructure.Llm;
using RepoIntel.Infrastructure.Scanning;
using RepoIntel.Infrastructure.Sessions;

namespace RepoIntel.Infrastructure;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Wire infrastructure-level services: GitHub fetcher, scanners, providers,
    /// vector store, session store, progress broadcaster.
    /// Concrete implementations are added incrementally in later phases.
    /// </summary>
    public static IServiceCollection AddRepoIntelInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .Configure<AiOptions>(configuration.GetSection(AiOptions.SectionName))
            .Configure<IngestionOptions>(configuration.GetSection(IngestionOptions.SectionName));

        services.AddMemoryCache();
        services.AddHttpClient();

        services.AddSingleton<ISessionStore, InMemorySessionStore>();
        services.AddSingleton<IFileScanner, FileScanner>();
        services.AddSingleton<IMetadataExtractor, MetadataExtractor>();
        services.AddSingleton<IArchitectureGraphBuilder, HeuristicGraphBuilder>();
        services.AddSingleton<IRepoFetcher, GitRepoFetcher>();

        // RAG / indexing
        services.AddSingleton<IChunker, LineWindowChunker>();
        services.AddSingleton<IEmbeddingProvider, HashingEmbeddingProvider>();
        services.AddSingleton<IVectorStore, InMemoryVectorStore>();
        services.AddSingleton<IRetriever, Retriever>();

        // Chat
        services.AddSingleton<IChatHistoryStore, InMemoryChatHistoryStore>();
        services.AddSingleton<ILlmProvider, GroqLlmProvider>();

        // Slot reservations for upcoming phases:
        //   services.AddSingleton<IProgressBroadcaster, ChannelProgressBroadcaster>();

        return services;
    }
}
