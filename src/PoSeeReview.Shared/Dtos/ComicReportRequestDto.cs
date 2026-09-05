using PoSeeReview.Shared.Enums;

namespace PoSeeReview.Shared.Dtos;

/// <summary>
/// A viewer's report of a comic. Body of <c>POST /api/reports</c>.
/// Validation lives in <see cref="Validation.ComicReportRequestValidator"/> (NET_RULES 2.2).
/// <para>
/// Deliberately not the same thing as <see cref="TakedownRequestDto"/>. That path carries a
/// shared admin API key and deletes the comic, its blob and its leaderboard row immediately —
/// correct for a verified legal request, catastrophic if exposed to the internet. This one is
/// session-authenticated, rate-limited, and only ever records a signal for review.
/// </para>
/// </summary>
public sealed class ComicReportRequestDto
{
    /// <summary>Google Maps place identifier of the reported comic.</summary>
    public string PlaceId { get; set; } = string.Empty;

    /// <summary>Why the viewer flagged it.</summary>
    public ComicReportReason Reason { get; set; } = ComicReportReason.Other;

    /// <summary>Optional free-text context from the reporter (never rendered to other users).</summary>
    public string Details { get; set; } = string.Empty;

    /// <summary>Optional contact address, so a business owner can be followed up with.</summary>
    public string ContactEmail { get; set; } = string.Empty;
}

/// <summary>Acknowledgement returned by <c>POST /api/reports</c>.</summary>
public sealed class ComicReportResponseDto
{
    /// <summary>Reference the reporter can quote in follow-up correspondence.</summary>
    public string ReportId { get; set; } = string.Empty;

    /// <summary>User-facing confirmation copy.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// True when this principal had already reported this comic. The report is not recorded
    /// twice, but the caller still gets a 202 — telling someone their report "failed" because
    /// they tapped twice reads as the app ignoring them.
    /// </summary>
    public bool AlreadyReported { get; set; }
}
