namespace PoSeeReview.Shared.Ids;

/// <summary>
/// Identifier for a generated comic (NET_RULES 1.5 — strongly-typed IDs).
/// </summary>
public readonly record struct ComicId(string Value)
{
    /// <summary>The unset identifier.</summary>
    public static ComicId Empty => new(string.Empty);

    /// <summary>True when no comic has been assigned.</summary>
    public bool IsEmpty => string.IsNullOrWhiteSpace(Value);

    /// <summary>Wraps a raw comic id, normalising null to <see cref="Empty"/>.</summary>
    public static ComicId From(string? value) => new(value ?? string.Empty);

    /// <summary>Mints a fresh identifier for a newly generated comic.</summary>
    public static ComicId New() => new(Guid.NewGuid().ToString("N"));

    public override string ToString() => Value;
}
