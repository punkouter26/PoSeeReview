using PoSeeReview.Shared.Ids;

namespace PoSeeReview.Shared.Contracts;

/// <summary>
/// The erase half of the kept-comics store, declared in Shared for exactly the reason
/// <see cref="IHallOfFameArchive"/> is: a takedown has to remove every kept copy of a comic
/// without the Takedowns slice referencing the Collections slice that owns them (NET_RULES 2.2).
/// <para>
/// Only the delete is exposed here. Reads — a person's own collection — stay inside the slice.
/// Kept comics are the copies that deliberately survive the 24-hour expiry and the cleanup
/// service, which makes them, along with the Hall of Fame, the ones a takedown must go after
/// explicitly.
/// </para>
/// </summary>
public interface IKeptComicArchive
{
    /// <summary>Removes every user's kept copy of one comic, blobs included.</summary>
    Task DeleteAllForPlaceAsync(PlaceId placeId, CancellationToken cancellationToken = default);
}
