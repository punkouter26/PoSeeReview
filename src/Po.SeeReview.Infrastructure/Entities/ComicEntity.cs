using Azure;
using Azure.Data.Tables;
using Po.SeeReview.Core.Entities;

namespace Po.SeeReview.Infrastructure.Entities;

/// <summary>
/// Table Storage entity for Comic with 24-hour cache support
/// PartitionKey: "COMIC"
/// RowKey: PlaceId (Google Maps Place ID)
/// </summary>
public class ComicEntity : ITableEntity
{
    public string PartitionKey { get; set; } = "COMIC";
    public string RowKey { get; set; } = string.Empty; // PlaceId
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    // Comic properties
    public string Id { get; set; } = string.Empty;
    public string PlaceId { get; set; } = string.Empty;
    public string RestaurantName { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public string Narrative { get; set; } = string.Empty;
    public int StrangenessScore { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Converts from domain Comic entity to Table Storage entity
    /// </summary>
    public static ComicEntity FromDomain(Comic comic)
    {
        return new ComicEntity
        {
            PartitionKey = "COMIC",
            RowKey = comic.PlaceId,
            Id = comic.Id,
            PlaceId = comic.PlaceId,
            RestaurantName = comic.RestaurantName,
            ImageUrl = comic.ImageUrl,
            Narrative = comic.Narrative,
            StrangenessScore = comic.StrangenessScore,
            ExpiresAt = comic.ExpiresAt,
            CreatedAt = comic.CreatedAt
        };
    }

    /// <summary>
    /// Converts from Table Storage entity to domain Comic entity
    /// </summary>
    public Comic ToDomain()
    {
        return new Comic
        {
            Id = Id,
            PlaceId = PlaceId,
            RestaurantName = RestaurantName,
            ImageUrl = ImageUrl,
            Narrative = Narrative,
            StrangenessScore = StrangenessScore,
            ExpiresAt = ExpiresAt,
            CreatedAt = CreatedAt,
            IsCached = false // Set by service layer
        };
    }
}
