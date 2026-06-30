using PoSeeReview.Application.Diagnostics;
using PoSeeReview.Shared.Dtos;

namespace PoSeeReview.Api.Features.Diagnostics;

/// <summary>
/// UI-less diagnostics slice. Maps the top-level <c>/diag</c> route (NET_RULES 3.2):
/// masked configuration keys and integration statuses.
/// Gated to Development only — the snapshot enumerates the full configuration/secret-key
/// inventory, which must never be reachable in Production. Use <c>/health</c> for prod probes.
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
            // Never expose the configuration/secret-key dump outside Development.
            if (!environment.IsDevelopment())
            {
                return Results.NotFound();
            }

            var diagnostics = await handler.ExecuteAsync(cancellationToken);
            diagnostics.Environment = environment.EnvironmentName;
            return Results.Ok(diagnostics);
        });

        group.MapGet("/mock-status", (IWebHostEnvironment environment, IConfiguration configuration) =>
        {
            var isMock = environment.IsEnvironment("Test") || configuration.GetValue<bool>("UseMockData");
            return Results.Ok(new MockStatusDto { IsMockActive = isMock });
        });

        return app;
    }
}
