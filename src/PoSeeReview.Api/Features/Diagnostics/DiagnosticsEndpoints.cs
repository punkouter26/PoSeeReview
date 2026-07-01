using PoSeeReview.Application.Diagnostics;
using PoSeeReview.Core.Interfaces;
using PoSeeReview.Shared.Dtos;

namespace PoSeeReview.Api.Features.Diagnostics;

/// <summary>
/// UI-less diagnostics slice. Maps the top-level <c>/diag</c> route (NET_RULES 3.2):
/// masked configuration keys and integration statuses, active in Dev and Prod.
/// Values are masked by the snapshot handler, so only key names and statuses leak.
/// </summary>
internal static class DiagnosticsEndpoints
{
    public static IEndpointRouteBuilder MapDiagnosticsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/diag").WithTags("Diagnostics");

        group.MapGet("", async (
            IWebHostEnvironment environment,
            DiagnosticsSnapshotQueryHandler handler,
            CancellationToken cancellationToken) =>
        {
            var diagnostics = await handler.ExecuteAsync(cancellationToken);
            diagnostics.Environment = environment.EnvironmentName;
            return Results.Ok(diagnostics);
        });

        group.MapGet("/mock-status", (IEnumerable<IMockable> mockables, IConfiguration configuration) =>
        {
            var activeMocks = mockables.Select(m => m.GetType().Name).Distinct().ToList();
            var isMock = activeMocks.Count > 0 || configuration.GetValue<bool>("UseMockData");
            return Results.Ok(new MockStatusDto { IsMockActive = isMock, ActiveMocks = activeMocks });
        });

        return app;
    }
}
