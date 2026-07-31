using PoSeeReview.Shared.Enums;

namespace PoSeeReview.Shared.Ids;

/// <summary>
/// Geographic partition key in <c>{Country}-{State}-{City}</c> form, e.g. <c>US-WA-Seattle</c>
/// (NET_RULES 1.5 — strongly-typed IDs, zero magic strings).
/// </summary>
public readonly record struct RegionCode(string Value)
{
    /// <summary>Fallback partition used when a restaurant's region cannot be resolved.</summary>
    public static RegionCode Default => From(CountryRegion.US);

    /// <summary>
    /// Every country partition a restaurant can be filed under — used to probe the cache
    /// without hardcoding the code list at each call site.
    /// </summary>
    public static IReadOnlyList<RegionCode> KnownCountries { get; } =
        Enum.GetValues<CountryRegion>().Select(From).ToArray();

    /// <summary>Projects a country enum onto its partition code.</summary>
    public static RegionCode From(CountryRegion country) => new(country.ToString());

    /// <summary>True when no region has been assigned.</summary>
    public bool IsEmpty => string.IsNullOrWhiteSpace(Value);

    /// <summary>Wraps a raw region code, falling back to <see cref="Default"/> when absent.</summary>
    public static RegionCode From(string? value) =>
        string.IsNullOrWhiteSpace(value) ? Default : new(value);

    public override string ToString() => Value;
}
