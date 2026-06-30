using System.Reflection;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.Extensibility;

namespace Po.SeeReview.Api.Telemetry;

/// <summary>
/// Maps Application Insights <c>cloud_RoleName</c> to this host's execution assembly via
/// reflection so traces never surface as <c>unknown_service:dotnet</c> (NET_RULES 6.2).
/// Registered only in the API host — never in the Blazor WASM client.
/// </summary>
internal sealed class RoleNameTelemetryInitializer : ITelemetryInitializer
{
    private static readonly string RoleName =
        Assembly.GetExecutingAssembly().GetName().Name ?? "PoSeeReview.Api";

    public void Initialize(ITelemetry telemetry) =>
        telemetry.Context.Cloud.RoleName = RoleName;
}
