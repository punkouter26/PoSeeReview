using PoSeeReview.Shared.Dtos;

namespace PoSeeReview.Api.Features.Insights;

/// <summary>Builds the cross-restaurant aggregates behind <c>GET /api/insights</c>.</summary>
public interface IInsightsService
{
    Task<InsightsDto> GetInsightsAsync(CancellationToken cancellationToken = default);
}
