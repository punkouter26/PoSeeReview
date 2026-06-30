using System.Runtime.InteropServices.JavaScript;

namespace PoSeeReview.Client;

/// <summary>
/// Provides synchronous JS interop for persisting the MSAL login mode preference
/// across a forced page reload (popup → redirect fallback).
/// The functions are defined in wwwroot/index.html before Blazor boots.
/// </summary>
internal static partial class LoginModeInterop
{
    /// <summary>
    /// Reads and immediately clears the stored login mode from sessionStorage.
    /// Returns "popup" if nothing is stored.
    /// </summary>
    [JSImport("globalThis.getMsalLoginMode")]
    internal static partial string GetMsalLoginMode();

    /// <summary>
    /// Stores a one-shot login mode preference in sessionStorage for the next page load.
    /// </summary>
    [JSImport("globalThis.setMsalLoginMode")]
    internal static partial void SetMsalLoginMode(string mode);
}
