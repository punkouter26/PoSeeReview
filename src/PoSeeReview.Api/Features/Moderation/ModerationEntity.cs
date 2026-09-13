using Azure;
using Azure.Data.Tables;
using PoSeeReview.Shared.Ids;

namespace PoSeeReview.Api.Features.Moderation;

/// <summary>
/// The moderation state of one place.
/// <para>
/// PartitionKey is a single constant and RowKey is the place id, so the question asked on every
/// comic read — "is this place clear?" — is one point query. There is deliberately no row until
/// something happens: the absence of a row is the clear verdict, which keeps the table
/// proportional to the problem rather than to the catalogue.
/// </para>
/// </summary>
public class ModerationEntity : ITableEntity
{
    /// <summary>Single partition holding every moderated place.</summary>
    public const string PartitionKeyValue = "MOD";

    /// <summary>Strips the characters Table Storage rejects in keys.</summary>
    public static string RowKeyFor(PlaceId placeId)
    {
        var cleaned = new string((placeId.Value ?? string.Empty)
            .Where(c => !char.IsControl(c) && c is not ('/' or '\\' or '#' or '?'))
            .ToArray());

        return string.IsNullOrWhiteSpace(cleaned) ? "unknown" : cleaned;
    }

    public string PartitionKey { get; set; } = PartitionKeyValue;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string PlaceId { get; set; } = string.Empty;

    /// <summary>Withheld from every read path, reversibly.</summary>
    public bool IsHidden { get; set; }

    /// <summary>No comic may be generated for this place. What a takedown leaves behind.</summary>
    public bool IsSuppressed { get; set; }

    /// <summary>Distinct reporters as last counted. Drives the auto-hide threshold.</summary>
    public int ReportCount { get; set; }

    /// <summary>True once a human has acted, so the queue can separate open from settled.</summary>
    public bool IsReviewed { get; set; }

    /// <summary>Operator note from the last action. Never rendered to end users.</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// Who last acted. A moderation trail exists to be audited, and "the system did it" is not
    /// an answer anyone can follow up on.
    /// </summary>
    public string UpdatedBy { get; set; } = string.Empty;

    public DateTimeOffset UpdatedAt { get; set; }
}
