using System.Net.Http.Json;
using PoSeeReview.Shared.Dtos;

namespace PoSeeReview.Client.Services;

/// <summary>
/// Typed wrapper over <c>GET /api/insights</c>.
/// <para>
/// Separate from <see cref="ApiClient"/> rather than another method on it: this is one read for
/// one page, and <see cref="ApiClient"/> is already the app's longest file. Unlike most calls
/// there, a failure is surfaced — the whole page is the charts, so a silent null would render
/// as "no data yet" and quietly misreport an outage as an empty database.
/// </para>
/// </summary>
public sealed class InsightsClient(HttpClient httpClient)
{
    public async Task<InsightsDto> GetInsightsAsync(CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync("/api/insights", cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync(AppJsonContext.Default.InsightsDto, cancellationToken)
            ?? throw new InvalidOperationException("Insights response was empty.");
    }
}
