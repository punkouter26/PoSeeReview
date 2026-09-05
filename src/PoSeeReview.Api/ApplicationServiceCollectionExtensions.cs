using Microsoft.Extensions.DependencyInjection;
using PoSeeReview.Api.Features.Comics;
using PoSeeReview.Api.Features.DevSessions;
using PoSeeReview.Api.Features.Diagnostics;

namespace PoSeeReview.Api;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // The application layer acts as a thin facade over domain and infrastructure services,
        // keeping controllers aligned with SRP and feature-slice boundaries.
        services.AddScoped<GenerateComicCommandHandler>();
        services.AddScoped<GetCachedComicQueryHandler>();
        services.AddScoped<ComicStatsQueryHandler>();
        services.AddScoped<DiagnosticsSnapshotQueryHandler>();
        services.AddScoped<DevSessionCommandHandler>();

        return services;
    }
}
