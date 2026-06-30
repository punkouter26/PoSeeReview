using Microsoft.Extensions.DependencyInjection;
using PoSeeReview.Application.Comics;
using PoSeeReview.Application.DevSessions;
using PoSeeReview.Application.Diagnostics;

namespace PoSeeReview.Application;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // The application layer acts as a thin facade over domain and infrastructure services,
        // keeping controllers aligned with SRP and feature-slice boundaries.
        services.AddScoped<GenerateComicCommandHandler>();
        services.AddScoped<GetCachedComicQueryHandler>();
        services.AddScoped<DiagnosticsSnapshotQueryHandler>();
        services.AddScoped<DevSessionCommandHandler>();

        return services;
    }
}
