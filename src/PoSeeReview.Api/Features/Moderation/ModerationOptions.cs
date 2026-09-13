namespace PoSeeReview.Api.Features.Moderation;

/// <summary>
/// Configuration for the moderation slice. Each slice owns its own options type (NET_RULES 2.2).
/// </summary>
public sealed class ModerationOptions
{
    public const string SectionName = "Moderation";

    /// <summary>
    /// Authorization policy name for operator endpoints.
    /// </summary>
    public const string PolicyName = "moderator";

    /// <summary>
    /// Role a principal must hold to reach <c>/api/moderation</c>.
    /// <para>
    /// Not a secret and not in Key Vault: it names a role, it does not grant one. Membership is
    /// what is privileged, and that lives in the identity provider.
    /// </para>
    /// </summary>
    public string RequiredRole { get; set; } = "Moderator";

    /// <summary>
    /// Distinct reporters that auto-hide a comic pending review.
    /// <para>
    /// Three, not one: a single report is a signal, and letting one person unilaterally
    /// unpublish a business's comic is a griefing tool. Three strangers independently flagging
    /// the same comic is evidence worth acting on before a human gets to it.
    /// </para>
    /// </summary>
    public int AutoHideReportThreshold { get; set; } = 3;

    /// <summary>How far back the queue looks.</summary>
    public int QueueWindowDays { get; set; } = 30;

    /// <summary>Maximum rows the queue returns. This is an operator page, not a user path.</summary>
    public int QueueLimit { get; set; } = 100;

    /// <summary>
    /// Rejects a generated narrative that trips the content screen before it reaches the image
    /// model. Off would mean publishing model-written text about a named business unchecked.
    /// </summary>
    public bool ScreenGeneratedContent { get; set; } = true;
}
