namespace PoSeeReview.Shared.Enums;

/// <summary>
/// Why a viewer flagged a comic (NET_RULES 1.5 — enums over magic strings).
/// <para>
/// This is the <em>public</em> intake vocabulary, deliberately separate from the owner/legal
/// takedown path in the Takedowns slice: a report is a queued signal, whereas a takedown is an
/// authenticated, immediate delete.
/// </para>
/// </summary>
public enum ComicReportReason
{
    /// <summary>The comic says something about the restaurant that is not in its reviews.</summary>
    Inaccurate = 0,

    /// <summary>Sexual, violent, or otherwise unacceptable imagery or text.</summary>
    Offensive = 1,

    /// <summary>Identifies or targets a real individual.</summary>
    TargetsAPerson = 2,

    /// <summary>The viewer represents the business and wants it removed.</summary>
    OwnerRequest = 3,

    /// <summary>Anything the fixed reasons do not cover.</summary>
    Other = 4
}
