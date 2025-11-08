namespace Po.SeeReview.Core.Entities;

/// <summary>
/// Represents a restaurant discovered via Google Maps API with cached metadata and reviews
/// </summary>
public class Restaurant
{
    /// <summary>
    /// Google Maps place_id (unique identifier)
    /// </summary>
    public string PlaceId { get; set; } = string.Empty;

    /// <summary>
    /// Restaurant name
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
    /// Region for partitioning (format: {Country}-{State}-{City}, e.g., "US-CA-SF")
    /// </summary>
    public string Region { get; set; } = string.Empty;

    /// <summary>
    /// Average rating from Google (0-5 stars)
    /// </summary>
    public double AverageRating { get; set; }

    /// <summary>
    /// Total number of reviews on Google
    /// </summary>
    public int TotalReviews { get; set; }

    /// <summary>
    /// Top 10 reviews from Google Maps (cached)
    /// </summary>
    public List<Review> Reviews { get; set; } = new();

    /// <summary>
    /// Timestamp when data was cached (for 24-hour expiration)
    /// </summary>
    public DateTimeOffset CachedAt { get; set; }
}
