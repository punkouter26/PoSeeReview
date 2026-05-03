using Microsoft.Extensions.DependencyInjection;
using Po.SeeReview.Application.Comics;
using Po.SeeReview.Application.DevSessions;
using Po.SeeReview.Application.Diagnostics;

namespace Po.SeeReview.Application;

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
