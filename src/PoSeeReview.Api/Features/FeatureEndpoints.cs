using PoSeeReview.Api.Features.Analytics;
using PoSeeReview.Api.Features.Auth;
using PoSeeReview.Api.Features.Comics;
using PoSeeReview.Api.Features.DevSessions;
using PoSeeReview.Api.Features.Diagnostics;
using PoSeeReview.Api.Features.Leaderboard;
using PoSeeReview.Api.Features.Reactions;
using PoSeeReview.Api.Features.Reports;
using PoSeeReview.Api.Features.Restaurants;
using PoSeeReview.Api.Features.Takedowns;

namespace PoSeeReview.Api.Features;

/// <summary>
/// Central registration of every Minimal-API feature slice via MapGroup (NET_RULES 3.3).
/// </summary>
internal static class FeatureEndpoints
{
    public static IEndpointRouteBuilder MapFeatureEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapAuthEndpoints();
        app.MapComicsEndpoints();
        app.MapRestaurantsEndpoints();
        app.MapLeaderboardEndpoints();
        app.MapDevSessionEndpoints();
        app.MapTakedownEndpoints();
        app.MapReportEndpoints();
        app.MapReactionEndpoints();
        app.MapAnalyticsEndpoints();
        app.MapDiagnosticsEndpoints();
        return app;
    }
}
