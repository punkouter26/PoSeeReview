namespace Po.SeeReview.Shared.Dtos;

/// <summary>
/// DTO for detailed restaurant information with reviews (GET /api/restaurants/{placeId})
/// </summary>
public class RestaurantDetailsDto
{
    /// <summary>
    /// Google Maps place ID (unique identifier)
    /// </summary>
    public string PlaceId { get; set; } = string.Empty;

    /// <summary>
    /// Restaurant name (max 255 chars)
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Full street address
    /// </summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>
    /// Latitude coordinate (-90 to 90)
    /// </summary>
    public double Latitude { get; set; }

    /// <summary>
    /// Longitude coordinate (-180 to 180)
    /// </summary>
    public double Longitude { get; set; }

    /// <summary>
    /// Google average rating (0-5 stars)
    /// </summary>
    public double AverageRating { get; set; }

    /// <summary>
    /// Total number of reviews on Google
    /// </summary>
    public int TotalReviews { get; set; }

    /// <summary>
    /// Top 10 reviews sorted by strangeness score (descending)
    /// </summary>
    public List<ReviewDto> Reviews { get; set; } = new();
}
