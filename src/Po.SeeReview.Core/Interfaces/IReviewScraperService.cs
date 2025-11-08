using Po.SeeReview.Core.Entities;

namespace Po.SeeReview.Core.Interfaces;

/// <summary>
/// Service for scraping and analyzing Google Maps reviews with AI strangeness scoring
/// </summary>
public interface IReviewScraperService
{
    /// <summary>
    /// Fetches top 10 reviews for a restaurant from Google Maps
    /// Analyzes each review with Azure OpenAI to calculate strangeness score (0-100)
    /// </summary>
    /// <param name="placeId">Google Maps place ID</param>
    /// <returns>List of reviews sorted by strangeness score (descending)</returns>
    /// <exception cref="ArgumentNullException">placeId is null or empty</exception>
    /// <exception cref="HttpRequestException">Google Maps API or Azure OpenAI failure</exception>
    Task<List<Review>> GetReviewsAsync(string placeId);
}
