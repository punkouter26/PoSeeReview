namespace PoSeeReview.Api.Features.Collections;

/// <summary>
/// Configuration for kept comics. Each slice owns its own options type (NET_RULES 2.2).
/// </summary>
public sealed class CollectionsOptions
{
    public const string SectionName = "Collections";

    /// <summary>
    /// How many comics one person may keep.
    /// <para>
    /// Bounded because keeping is the one action in this app that opts a blob out of the
    /// cleanup service. Unbounded keeps are an unbounded storage bill attached to a free
    /// feature, which is the same reasoning behind the daily generation budget.
    /// </para>
    /// </summary>
    public int MaxKeptPerUser { get; set; } = 50;
}
