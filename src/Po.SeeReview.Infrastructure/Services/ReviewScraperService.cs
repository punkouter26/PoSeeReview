using Microsoft.Extensions.Logging;
using Po.SeeReview.Core.Entities;
using Po.SeeReview.Core.Interfaces;

namespace Po.SeeReview.Infrastructure.Services;

/// <summary>
/// Service for scraping and analyzing Google Maps reviews with AI strangeness scoring
/// NOTE: This is a stub implementation - will be completed in Phase 3 User Story 2
/// </summary>
public class ReviewScraperService : IReviewScraperService
{
    private readonly ILogger<ReviewScraperService> _logger;

    public ReviewScraperService(ILogger<ReviewScraperService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Fetches top 10 reviews for a restaurant from Google Maps
    /// Analyzes each review with Azure OpenAI to calculate strangeness score (0-100)
    /// </summary>
    /// <param name="placeId">Google Maps place ID</param>
    /// <returns>List of reviews sorted by strangeness score (descending)</returns>
    public async Task<List<Review>> GetReviewsAsync(string placeId)
    {
        if (string.IsNullOrWhiteSpace(placeId))
        {
            throw new ArgumentNullException(nameof(placeId));
        }

        _logger.LogInformation("GetReviewsAsync called for placeId: {PlaceId} (stub implementation)", placeId);

        // TODO: Implement in Phase 3 User Story 2
        // 1. Call Google Places API to fetch reviews
        // 2. Analyze each review with Azure OpenAI for strangeness score
        // 3. Return sorted by strangeness score

        await Task.CompletedTask;
        return new List<Review>();
    }
}
