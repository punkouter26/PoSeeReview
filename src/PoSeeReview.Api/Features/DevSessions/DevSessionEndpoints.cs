using PoSeeReview.Application.DevSessions;
using PoSeeReview.Shared.Dtos;

namespace PoSeeReview.Api.Features.DevSessions;

/// <summary>
/// Dev/guest session slice. Maps <c>/api/devsession</c> (NET_RULES 3.3).
/// Stays mounted in Production so the WASM client never 404s on the session probe.
/// </summary>
internal static class DevSessionEndpoints
{
    public static IEndpointRouteBuilder MapDevSessionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/devsession").WithTags("DevSession");

        group.MapGet("", (DevSessionCommandHandler handler, IWebHostEnvironment env) =>
            Results.Ok(IsEnabled(env) ? handler.GetCurrentSession() : DevSessionCommandHandler.BuildAnonymousSession()));

        group.MapPost("/anon", (DevSessionCommandHandler handler, IWebHostEnvironment env) =>
            Results.Ok(IsEnabled(env) ? handler.CreateRandomAnonymousSession() : DevSessionCommandHandler.BuildAnonymousSession()));

        return app;
    }

    private static bool IsEnabled(IWebHostEnvironment env) =>
        env.IsDevelopment() || env.IsEnvironment("Test");
}
