using System.Security.Cryptography;
using System.Text;
using Azure;
using Azure.Data.Tables;
using PoSeeReview.Shared.Ids;

namespace PoSeeReview.Api.Features.Collections;

/// <summary>
/// Key construction for kept comics.
/// <para>
/// Two row shapes, one table, mirroring the two questions that get asked. "What has this person
/// kept" is a single partition read on every visit to their collection. "Who has kept this
/// comic" is asked once, by a takedown, and without the second shape it would be a
/// cross-partition scan of every user's collection — the one query that must not be slow when
/// something has to come down.
/// </para>
/// </summary>
internal static class KeptComicKeys
{
    private const string UserPartitionPrefix = "KEPT";
    private const string PlacePartitionPrefix = "KEPTPLACE";

    /// <summary>Strips the characters Table Storage rejects in keys.</summary>
    private static string Sanitize(string value)
    {
        var cleaned = new string((value ?? string.Empty)
            .Where(c => !char.IsControl(c) && c is not ('/' or '\\' or '#' or '?'))
            .ToArray());

        return string.IsNullOrWhiteSpace(cleaned) ? "unknown" : cleaned;
    }

    public static string UserPartitionFor(UserId userId) => $"{UserPartitionPrefix}_{Sanitize(userId.Value)}";

    public static string PlacePartitionFor(PlaceId placeId) => $"{PlacePartitionPrefix}_{Sanitize(placeId.Value)}";

    public static string PlaceRowKeyFor(PlaceId placeId) => Sanitize(placeId.Value);

    public static string UserRowKeyFor(UserId userId) => Sanitize(userId.Value);

    /// <summary>
    /// Folder name for one user's kept blobs.
    /// <para>
    /// A hash rather than the principal itself. Blob paths turn up in storage explorers, access
    /// logs and support screenshots, and a principal is frequently an email address — this is
    /// the one place a user identifier would otherwise be written somewhere nobody thinks of as
    /// a data store.
    /// </para>
    /// </summary>
    public static string BlobFolderFor(UserId userId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(userId.Value ?? string.Empty));
        return Convert.ToHexStringLower(hash)[..24];
    }

    /// <summary>Blob name of one user's kept copy of one comic.</summary>
    public static string BlobNameFor(UserId userId, PlaceId placeId) =>
        $"{BlobFolderFor(userId)}/{Sanitize(placeId.Value)}.png";
}

/// <summary>
/// One comic in one person's collection. Carries its own copy of the name and score, so the
/// collection still renders after the comic row has expired and been purged — which is the
/// entire point of keeping it.
/// </summary>
public class KeptComicEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string PlaceId { get; set; } = string.Empty;
    public string RestaurantName { get; set; } = string.Empty;
    public int StrangenessScore { get; set; }

    /// <summary>Blob name in the kept container, or empty when the copy could not be written.</summary>
    public string BlobName { get; set; } = string.Empty;

    public DateTimeOffset KeptAt { get; set; }
}

/// <summary>
/// Reverse index: which users kept a given comic. Written alongside the row above and read only
/// by a takedown.
/// </summary>
public class KeptComicByPlaceEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string PlaceId { get; set; } = string.Empty;
    /// <summary>Partition of the forward row. Its RowKey is the place id, already known here.</summary>
    public string UserPartitionKey { get; set; } = string.Empty;

    public string BlobName { get; set; } = string.Empty;
}
