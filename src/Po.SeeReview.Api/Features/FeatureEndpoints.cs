using Po.SeeReview.Api.Features.Comics;
using Po.SeeReview.Api.Features.DevSessions;
using Po.SeeReview.Api.Features.Diagnostics;
using Po.SeeReview.Api.Features.Leaderboard;
using Po.SeeReview.Api.Features.Restaurants;
using Po.SeeReview.Api.Features.Takedowns;

namespace Po.SeeReview.Api.Features;

/// <summary>
/// Central registration of every Minimal-API feature slice via MapGroup (NET_RULES 3.3).
/// </summary>
internal static class FeatureEndpoints
{
    public static IEndpointRouteBuilder MapFeatureEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapComicsEndpoints();
        app.MapRestaurantsEndpoints();
        app.MapLeaderboardEndpoints();
        app.MapDevSessionEndpoints();
        app.MapTakedownEndpoints();
        app.MapDiagnosticsEndpoints();
        return app;
    }
}
