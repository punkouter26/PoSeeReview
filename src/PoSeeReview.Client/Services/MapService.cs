using System.Diagnostics.CodeAnalysis;
using Microsoft.JSInterop;

namespace PoSeeReview.Client.Services;

/// <summary>One pin on the discovery map.</summary>
public sealed class MapPlace
{
    public string PlaceId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public double Lat { get; set; }
    public double Lng { get; set; }

    /// <summary>True when a live cached comic exists — instant and free, rather than a paid draw.</summary>
    public bool HasComic { get; set; }
}

/// <summary>Where the map opens.</summary>
public sealed class MapCentre
{
    public double Lat { get; set; }
    public double Lng { get; set; }
}

/// <summary>What <c>poseeMap.show</c> is handed.</summary>
public sealed class MapPayload
{
    public MapCentre Centre { get; set; } = new();
    public List<MapPlace> Places { get; set; } = [];
}

/// <summary>Pin colours, resolved from design tokens rather than hardcoded.</summary>
public sealed class MapStyles
{
    public string Ready { get; set; } = string.Empty;
    public string Pending { get; set; } = string.Empty;
}

/// <summary>
/// .NET side of <c>map.js</c>.
/// <para>
/// Modelled on <see cref="FxService"/> and for the same reason: nothing here may throw into
/// .NET. A map that fails to load is a panel that does not open — surfacing it through interop
/// would put the framework's red error strip over a discovery page that is otherwise working
/// perfectly, with the results grid right there underneath.
/// </para>
/// </summary>
public sealed class MapService(IJSRuntime jsRuntime)
{
    private const string ShowFunction = "poseeMap.show";
    private const string HideFunction = "poseeMap.hide";

    /// <summary>
    /// Mounts or refreshes the map. Returns false when it could not be shown, which the caller
    /// treats as "stay on the list".
    /// </summary>
    /// <param name="callback">
    /// A <see cref="DotNetObjectReference{TValue}"/> exposing <c>OnPlaceSelected</c>. Typed as
    /// <see cref="object"/> because the reference is closed over the calling component and
    /// <c>DotNetObjectReference&lt;T&gt;</c> is invariant; interop resolves its converter from
    /// the runtime type.
    /// </param>
    public Task<bool> ShowAsync(
        string containerId,
        MapPayload payload,
        MapStyles styles,
        object callback) =>
        SafeAsync<bool>(ShowFunction, containerId, payload, styles, callback);

    /// <summary>Tears the map down. Safe to call when nothing is mounted.</summary>
    public Task<bool> HideAsync() => SafeAsync<bool>(HideFunction);

    /// <summary>
    /// Calls into JS and swallows every interop failure.
    /// <para>
    /// The <see cref="DynamicallyAccessedMembersAttribute"/> is required, not decorative:
    /// <c>InvokeAsync&lt;TValue&gt;</c> deserializes reflectively, and without it the client
    /// fails <c>IL2091</c> under <c>EnableTrimAnalyzer</c> + <c>TreatWarningsAsErrors</c>. Same
    /// annotation, same reason, as <c>FxService.SafeAsync</c>.
    /// </para>
    /// </summary>
    private async Task<T> SafeAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors
        | DynamicallyAccessedMemberTypes.PublicFields
        | DynamicallyAccessedMemberTypes.PublicProperties)] T>(
        string identifier,
        params object?[] args)
    {
        try
        {
            return await jsRuntime.InvokeAsync<T>(identifier, args);
        }
        catch (JSException)
        {
            return default!;
        }
        catch (InvalidOperationException)
        {
            // Prerender, or a disposed circuit. Neither is an error worth showing anyone.
            return default!;
        }
    }
}
