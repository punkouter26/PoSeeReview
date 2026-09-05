using Azure;
using Azure.Data.Tables;
using PoSeeReview.Shared.Enums;
using PoSeeReview.Shared.Ids;

namespace PoSeeReview.Api.Features.Reports;

/// <summary>
/// A viewer's report of a comic.
/// <para>
/// PartitionKey: <c>REPORT_{PlaceId}</c> — every report for one comic in one partition, so
/// counting them is a single partition scan.
/// RowKey: the reporting principal, which makes "one report per person per comic" a property
/// of the key rather than something a query has to enforce.
/// </para>
/// </summary>
public class ComicReportEntity : ITableEntity
{
    private const string PartitionKeyPrefix = "REPORT";

    /// <summary>
    /// Strips the characters Table Storage rejects in keys (<c>/ \ # ?</c> and control
    /// characters). Google place ids are alphanumeric in practice, but this key is built from
    /// request input and must not be able to produce a malformed row.
    /// </summary>
    private static string Sanitize(string value)
    {
        var cleaned = new string(value
            .Where(c => !char.IsControl(c) && c is not ('/' or '\\' or '#' or '?'))
            .ToArray());

        return string.IsNullOrWhiteSpace(cleaned) ? "unknown" : cleaned;
    }

    /// <summary>Builds the partition key holding every report for one place.</summary>
    public static string PartitionKeyFor(PlaceId placeId) => $"{PartitionKeyPrefix}_{Sanitize(placeId.Value)}";

    /// <summary>Builds the row key identifying one reporter.</summary>
    public static string RowKeyFor(UserId userId) => Sanitize(userId.Value);

    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    /// <summary>Reference quoted back to the reporter.</summary>
    public string ReportId { get; set; } = string.Empty;

    /// <summary>Google Maps place identifier of the reported comic.</summary>
    public string PlaceId { get; set; } = string.Empty;

    /// <summary>Stored as the enum name so a stored row stays readable if the numbering changes.</summary>
    public string Reason { get; set; } = ComicReportReason.Other.ToString();

    /// <summary>Reporter's free text. Never rendered to other users.</summary>
    public string Details { get; set; } = string.Empty;

    /// <summary>
    /// Optional reporter contact. This is PII: it is written here for follow-up and is
    /// deliberately kept out of logs and telemetry (NET_RULES 5.1/6.1).
    /// </summary>
    public string ContactEmail { get; set; } = string.Empty;

    /// <summary>When the report was first received.</summary>
    public DateTimeOffset ReportedAt { get; set; }
}
