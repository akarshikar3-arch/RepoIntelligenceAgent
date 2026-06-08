using Microsoft.Extensions.DependencyInjection;
using RepoIntel.Application.Abstractions;
using RepoIntel.Application.Chat;
using RepoIntel.Application.Pipeline;

namespace RepoIntel.Application;

public static class ServiceCollectionExtensions
{
    /// <summary>Register Application-layer services (use-cases, orchestrators).</summary>
    public static IServiceCollection AddRepoIntelApplication(this IServiceCollection services)
    {
        services.AddSingleton<IAnalysisPipeline, RealAnalysisPipeline>();
        services.AddSingleton<IChatOrchestrator, ChatOrchestrator>();
        return services;
    }
}
