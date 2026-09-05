using PoSeeReview.Shared.Ids;

namespace PoSeeReview.Shared.Contracts;

/// <summary>
/// The takedown-facing surface of the permanent weekly archive.
/// <para>
/// Declared in Shared for the same reason <see cref="ILeaderboardRepository"/> is: the Takedowns
/// slice has to erase archived entries without referencing the Leaderboard slice that owns them
/// (NET_RULES 2.2). Only the delete is exposed — reads belong to the slice.
/// </para>
/// <para>
/// This matters more than it looks. The archive deliberately outlives the 24-hour comic, so a
/// takedown that removed the comic, the blob and the live leaderboard row but not the archive
/// would leave the restaurant's name and score on a page that is designed never to expire.
/// </para>
/// </summary>
public interface IHallOfFameArchive
{
    /// <summary>Removes every archived entry for a place, across all weeks and regions.</summary>
    Task DeleteAllForPlaceAsync(PlaceId placeId, CancellationToken cancellationToken = default);
}
