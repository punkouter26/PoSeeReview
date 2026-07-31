namespace PoSeeReview.Shared.Ids;

/// <summary>
/// Identifier for the request principal — an Entra object id in Production or a dev-session
/// id locally (NET_RULES 1.5 — strongly-typed IDs).
/// </summary>
public readonly record struct UserId(string Value)
{
    /// <summary>
    /// Sentinel written when no principal could be resolved. Kept as a named constant so the
    /// literal never appears at a call site (NET_RULES 1.5 — zero magic strings).
    /// </summary>
    public const string AnonymousValue = "anonymous";

    /// <summary>The unauthenticated principal.</summary>
    public static UserId Anonymous => new(AnonymousValue);

    /// <summary>True when the request carries no identified principal.</summary>
    public bool IsAnonymous =>
        string.IsNullOrWhiteSpace(Value) || Value == AnonymousValue;

    /// <summary>Wraps a raw subject claim, normalising blank input to <see cref="Anonymous"/>.</summary>
    public static UserId From(string? value) =>
        string.IsNullOrWhiteSpace(value) ? Anonymous : new(value);

    public override string ToString() => Value;
}
