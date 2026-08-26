using Azure;
using Azure.Data.Tables;
using PoSeeReview.Shared.Contracts;
using PoSeeReview.Shared.Enums;
using PoSeeReview.Shared.Ids;
// The entity exposes a string column also called PlaceId, which would shadow the id type
// inside ToDomain(); the alias keeps both readable.
using PlaceIdentifier = PoSeeReview.Shared.Ids.PlaceId;

namespace PoSeeReview.Api.Features.Comics;

/// <summary>
/// Table Storage entity for <see cref="Comic"/> with 24-hour cache support.
/// PartitionKey: <see cref="PartitionKeyValue"/>; RowKey: the Google Maps place id.
/// The Table SDK only persists primitives, so the strongly-typed ids are unwrapped here and
/// re-wrapped in <see cref="ToDomain"/> — this is the single storage boundary that sees raw
/// strings (NET_RULES 1.5).
/// </summary>
public class ComicEntity : ITableEntity
{
    /// <summary>Single partition holding every comic row.</summary>
    public const string PartitionKeyValue = "COMIC";

    public string PartitionKey { get; set; } = PartitionKeyValue;
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
    public string RequestedByUserId { get; set; } = string.Empty;

    /// <summary>
    /// Converts from domain <see cref="Comic"/> to the Table Storage entity.
    /// </summary>
    public static ComicEntity FromDomain(Comic comic)
    {
        return new ComicEntity
        {
            PartitionKey = PartitionKeyValue,
            RowKey = comic.PlaceId.Value,
            Id = comic.Id.Value,
            PlaceId = comic.PlaceId.Value,
            RestaurantName = comic.RestaurantName,
            ImageUrl = comic.ImageUrl,
            Narrative = comic.Narrative,
            StrangenessScore = comic.StrangenessScore,
            ExpiresAt = comic.ExpiresAt,
            CreatedAt = comic.CreatedAt,
            RequestedByUserId = comic.RequestedByUserId.Value
        };
    }

    /// <summary>
    /// Converts from the Table Storage entity back to the domain <see cref="Comic"/>.
    /// </summary>
    public Comic ToDomain()
    {
        return new Comic
        {
            Id = ComicId.From(Id),
            PlaceId = PlaceIdentifier.From(PlaceId),
            RestaurantName = RestaurantName,
            ImageUrl = ImageUrl,
            Narrative = Narrative,
            StrangenessScore = StrangenessScore,
            ExpiresAt = ExpiresAt,
            CreatedAt = CreatedAt,
            RequestedByUserId = UserId.From(RequestedByUserId),
            // Cache provenance is a service-layer concern; storage never knows it.
            CacheState = ComicCacheState.Generated
        };
    }
}
