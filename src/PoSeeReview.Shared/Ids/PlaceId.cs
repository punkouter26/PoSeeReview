namespace PoSeeReview.Shared.Ids;

/// <summary>
/// Google Maps place identifier (NET_RULES 1.5 — strongly-typed IDs).
/// Wire DTOs keep the raw <see cref="string"/>; conversion happens at the endpoint boundary
/// so the WASM client's source-generated JSON context stays trim-safe.
/// </summary>
public readonly record struct PlaceId(string Value)
{
    /// <summary>The unset identifier.</summary>
    public static PlaceId Empty => new(string.Empty);

    /// <summary>True when no place has been assigned.</summary>
    public bool IsEmpty => string.IsNullOrWhiteSpace(Value);

    /// <summary>Wraps a raw Google place_id, normalising null to <see cref="Empty"/>.</summary>
    public static PlaceId From(string? value) => new(value ?? string.Empty);

    public override string ToString() => Value;
}
