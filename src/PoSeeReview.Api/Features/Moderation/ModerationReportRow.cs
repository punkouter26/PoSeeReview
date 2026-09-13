using Azure;
using Azure.Data.Tables;

namespace PoSeeReview.Api.Features.Moderation;

/// <summary>
/// Read-only projection of a <c>PoSeeReviewReports</c> row.
/// <para>
/// A separate type from the Reports slice's entity rather than a reference to it: slices must
/// not reference each other (NET_RULES 2.2), and Table Storage is schemaless per row so a POCO
/// carrying a subset of the columns reads the same rows without owning them. Same arrangement
/// the Insights slice uses over the Hall of Fame table.
/// </para>
/// <para>
/// Note what is deliberately <em>not</em> projected: <c>Details</c> and <c>ContactEmail</c>. The
/// queue needs counts and reasons to prioritise; the reporter's free text and contact address
/// are PII that belongs in the row and nowhere else (NET_RULES 5.1/6.1).
/// </para>
/// </summary>
public class ModerationReportRow : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string PlaceId { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTimeOffset ReportedAt { get; set; }
}
