using Microsoft.JSInterop;

namespace PoSeeReview.Client.Services;

/// <summary>How an install attempt ended.</summary>
public enum InstallOutcome
{
    /// <summary>The user accepted and the app is being installed.</summary>
    Accepted,

    /// <summary>The user declined.</summary>
    Dismissed,

    /// <summary>No prompt was available — already installed, or an unsupported browser.</summary>
    Unavailable
}

/// <summary>
/// Install-to-homescreen, fronting <c>window.poseePwa</c>.
/// <para>
/// Mirrors <see cref="FxService"/>'s contract: nothing here may throw into .NET. A JS interop
/// failure over an install banner would surface the framework's red error strip across a working
/// page, which is a far worse outcome than simply not offering the install.
/// </para>
/// </summary>
public sealed class PwaService(IJSRuntime jsRuntime, ILogger<PwaService> logger)
{
    /// <summary>True when a native install prompt can be shown right now.</summary>
    public Task<bool> CanInstallAsync() => SafeAsync("poseePwa.canInstall", false);

    /// <summary>True when the app is already running installed, so nothing should be offered.</summary>
    public Task<bool> IsInstalledAsync() => SafeAsync("poseePwa.isInstalled", false);

    /// <summary>
    /// True on iOS Safari, which can install but exposes no prompt API — the user has to be
    /// told to use the Share sheet, because no button can do it for them.
    /// </summary>
    public Task<bool> NeedsManualInstructionsAsync() => SafeAsync("poseePwa.needsManualInstructions", false);

    /// <summary>Shows the native install prompt.</summary>
    public async Task<InstallOutcome> PromptInstallAsync()
    {
        var outcome = await SafeAsync<string>("poseePwa.promptInstall", "unavailable");

        return outcome switch
        {
            "accepted" => InstallOutcome.Accepted,
            "dismissed" => InstallOutcome.Dismissed,
            _ => InstallOutcome.Unavailable
        };
    }

    /// <summary>
    /// Invokes an interop function, returning <paramref name="fallback"/> on any failure.
    /// </summary>
    /// <remarks>
    /// The <c>DynamicallyAccessedMembers</c> annotation is required, not decorative:
    /// <see cref="IJSRuntime.InvokeAsync{TValue}(string, object?[])"/> deserializes reflectively,
    /// and without it the trim-analyzed client fails <c>IL2091</c> under
    /// <c>TreatWarningsAsErrors</c> — the same reason <see cref="FxService"/> carries one.
    /// </remarks>
    private async Task<T> SafeAsync<[System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(
        System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors
        | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicFields
        | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties)] T>(
        string identifier,
        T fallback)
    {
        try
        {
            return await jsRuntime.InvokeAsync<T>(identifier);
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException or TaskCanceledException)
        {
            // Includes prerender, where no JS runtime exists yet.
            logger.LogDebug(ex, "PWA interop {Identifier} unavailable", identifier);
            return fallback;
        }
    }
}
