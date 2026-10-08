using System.Text.RegularExpressions;

namespace PoSeeReview.Shared.Ids;

/// <summary>
/// Google Maps place identifier (NET_RULES 1.5 — strongly-typed IDs).
/// Wire DTOs keep the raw <see cref="string"/>; conversion happens at the endpoint boundary
/// so the WASM client's source-generated JSON context stays trim-safe.
/// </summary>
public readonly partial record struct PlaceId(string Value)
{
    /// <summary>The <see cref="IsValid"/> shape, shared with the FluentValidation rules.</summary>
    public const string Pattern = "^[A-Za-z0-9_-]{1,255}$";

    /// <summary>The unset identifier.</summary>
    public static PlaceId Empty => new(string.Empty);

    /// <summary>True when no place has been assigned.</summary>
    public bool IsEmpty => string.IsNullOrWhiteSpace(Value);

    /// <summary>Wraps a raw Google place_id, normalising null to <see cref="Empty"/>.</summary>
    public static PlaceId From(string? value) => new(value ?? string.Empty);

    /// <summary>
    /// Whether caller-supplied text has the shape of a place id. Checked once at the edge: the
    /// value goes on to be a table RowKey, a metric dimension and a path segment of an upstream URL.
    /// </summary>
    public static bool IsValid(string? value) => value is not null && ValidShape().IsMatch(value);

    [GeneratedRegex(Pattern, RegexOptions.CultureInvariant)]
    private static partial Regex ValidShape();

    public override string ToString() => Value;
}
