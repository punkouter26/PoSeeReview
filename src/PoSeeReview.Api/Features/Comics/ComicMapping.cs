using PoSeeReview.Shared.Contracts;
using PoSeeReview.Shared.Dtos;
using PoSeeReview.Shared.Enums;

namespace PoSeeReview.Api.Features.Comics;

/// <summary>
/// Domain <see cref="Comic"/> to wire <see cref="ComicDto"/>. Extracted because three call
/// sites now project it — generate, cached read, and the social-preview page — and the receipts
/// list would otherwise be easy to add to two of them and forget in the third.
/// </summary>
internal static class ComicMapping
{
    public static ComicDto ToDto(this Comic comic, bool? isCached = null) => new()
    {
        ComicId = comic.Id.Value,
        PlaceId = comic.PlaceId.Value,
        RestaurantName = comic.RestaurantName,
        Narrative = comic.Narrative,
        StrangenessScore = comic.StrangenessScore,
        BlobUrl = comic.ImageUrl,
        GeneratedAt = comic.CreatedAt,
        ExpiresAt = comic.ExpiresAt,
        IsCached = isCached ?? comic.CacheState == ComicCacheState.Cached,
        Receipts = comic.Receipts
            .Select(r => new StrangenessReceiptDto { Quote = r.Quote, Points = r.Points })
            .ToList()
    };
}
