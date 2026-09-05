using Azure;
using Azure.Data.Tables;
using PoSeeReview.Shared.Enums;
using PoSeeReview.Shared.Ids;

namespace PoSeeReview.Api.Features.Reactions;

/// <summary>
/// Key construction shared by both reaction row shapes. Every row for one comic lives in one
/// partition, so a tally read and a "what did I pick" read are the same point query pair.
/// </summary>
internal static class ReactionKeys
{
    private const string PartitionKeyPrefix = "REACT";

    /// <summary>Row holding the tally. Not a legal principal key, so it cannot collide with a voter row.</summary>
    public const string TallyRowKey = "__counts__";

    private static string Sanitize(string value)
    {
        var cleaned = new string(value
            .Where(c => !char.IsControl(c) && c is not ('/' or '\\' or '#' or '?'))
            .ToArray());

        return string.IsNullOrWhiteSpace(cleaned) ? "unknown" : cleaned;
    }

    public static string PartitionKeyFor(PlaceId placeId) => $"{PartitionKeyPrefix}_{Sanitize(placeId.Value)}";

    public static string RowKeyFor(UserId userId) => Sanitize(userId.Value);
}

/// <summary>
/// Running totals for one comic.
/// <para>
/// A denormalised tally rather than a count over voter rows: reactions are read on every comic
/// view and written rarely, so a partition scan per page load would be the wrong trade. The
/// cost is that two simultaneous voters can lose an increment to a concurrency retry exhaustion
/// — a decorative counter off by one, never a wrong comic.
/// </para>
/// </summary>
public class ReactionTallyEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = ReactionKeys.TallyRowKey;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public int Laugh { get; set; }
    public int Mind { get; set; }
    public int Grim { get; set; }
    public int Love { get; set; }

    /// <summary>Applies <paramref name="delta"/> to one kind, never letting a tally go negative.</summary>
    public void Apply(ReactionKind kind, int delta)
    {
        switch (kind)
        {
            case ReactionKind.Laugh: Laugh = Math.Max(0, Laugh + delta); break;
            case ReactionKind.Mind: Mind = Math.Max(0, Mind + delta); break;
            case ReactionKind.Grim: Grim = Math.Max(0, Grim + delta); break;
            case ReactionKind.Love: Love = Math.Max(0, Love + delta); break;
        }
    }
}

/// <summary>
/// One person's reaction to one comic. The row key is the principal, so a viewer physically
/// cannot hold two reactions on the same comic.
/// </summary>
public class ReactionVoteEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    /// <summary>Stored as the enum name so rows stay readable if the numbering ever changes.</summary>
    public string Reaction { get; set; } = string.Empty;

    /// <summary>Parses the stored name back, treating anything unrecognised as no reaction.</summary>
    public ReactionKind? ToKind() =>
        Enum.TryParse<ReactionKind>(Reaction, out var kind) ? kind : null;
}
