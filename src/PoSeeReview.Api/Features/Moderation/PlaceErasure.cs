using PoSeeReview.Api.Storage;
using PoSeeReview.Shared.Contracts;
using PoSeeReview.Shared.Ids;

namespace PoSeeReview.Api.Features.Moderation;

/// <summary>
/// Erases a place everywhere it is published. One path for the moderator's Remove and the keyed
/// takedown, because the two drifting apart is how a takedown came to leave the board row up.
/// The caller suppresses the place first, so a partial failure here is the safe half-state.
/// </summary>
internal static class PlaceErasure
{
    public static async Task EraseAsync(
        PlaceId placeId,
        IComicRepository comicRepository,
        IBlobStorageService blobStorageService,
        ILeaderboardRepository leaderboardRepository,
        IHallOfFameArchive hallOfFameArchive,
        CancellationToken cancellationToken)
    {
        var comic = await comicRepository.GetByPlaceIdAsync(placeId);
        if (comic is not null)
        {
            await comicRepository.DeleteAsync(placeId);

            if (!comic.Id.IsEmpty)
            {
                await blobStorageService.DeleteComicImageAsync(comic.Id.Value);
            }
        }

        // Looked up by place, independently of the comic row: the board row outlives the comic
        // and sits in whatever region partition generation filed it under.
        var entry = await leaderboardRepository.GetByPlaceIdAsync(placeId);
        if (entry is not null)
        {
            await leaderboardRepository.DeleteAsync(placeId, entry.Region);
        }

        // The weekly archive is built to outlive everything else, so it is precisely the copy
        // that survives when the rest has already expired.
        await hallOfFameArchive.DeleteAllForPlaceAsync(placeId, cancellationToken);
    }
}
