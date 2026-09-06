using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace PoSeeReview.Client.Services;

/// <summary>How much visual and audible effort the device is currently allowed to spend.</summary>
public enum FxTier
{
    /// <summary>Static CSS only. Also what an OS reduced-motion request forces.</summary>
    Off = 0,

    /// <summary>Audio and CSS materials, but no persistent GPU loop.</summary>
    Lite = 1,

    /// <summary>Everything, including the persistent WebGL shader loops.</summary>
    Full = 2
}

/// <param name="Tier">Effects level currently in force.</param>
/// <param name="ReducedMotion">The OS asked for reduced motion; the tier is pinned to Off.</param>
/// <param name="WebGl2">Whether a WebGL2 context could be created at all.</param>
/// <param name="AutoDowngraded">The tier was lowered by the frame-budget watchdog, not by the user.</param>
public readonly record struct FxCapabilities(FxTier Tier, bool ReducedMotion, bool WebGl2, bool AutoDowngraded);

/// <param name="Fps">Rolling frames per second across the shared scheduler.</param>
/// <param name="FrameMs">Rolling mean frame time.</param>
/// <param name="WorstFrameMs">Worst frame seen since the last reset.</param>
/// <param name="DroppedFrames">Frames that exceeded the 20ms budget.</param>
/// <param name="SampledFrames">Total frames measured since the last reset.</param>
/// <param name="ActiveTasks">Effects currently registered on the scheduler.</param>
/// <param name="CpuMs">
/// Mean time per frame spent inside effect callbacks. The gap between this and
/// <paramref name="FrameMs"/> is everything else on the main thread — Blazor renders, GC, layout.
/// A large gap means the shaders were never the problem.
/// </param>
/// <param name="GpuMs">
/// Mean GPU time per frame, from EXT_disjoint_timer_query_webgl2. Null where the extension is
/// unavailable — which is most of Safari and Firefox. Null is not zero.
/// </param>
/// <param name="HeapMb">Used JS heap. Chromium only; null elsewhere.</param>
/// <param name="LongTasks">Main-thread tasks over 50ms since the last reset.</param>
/// <param name="WorstLongTaskMs">Longest single blocking task seen.</param>
/// <param name="InpMs">
/// Worst interaction latency observed. A pessimistic stand-in for true INP, which needs a
/// session-long 98th percentile — the right direction to be wrong in for a diagnostic.
/// </param>
/// <param name="LayoutShift">Cumulative layout shift, excluding shifts following real input.</param>
/// <param name="GlContexts">Live WebGL contexts, pooled plus direct. Creep here is a leak.</param>
/// <param name="GlSurfaces">Effects holding a render surface.</param>
/// <param name="ContextLosses">Times the shared atlas context was lost.</param>
public readonly record struct FxFrameStats(
    double Fps, double FrameMs, double WorstFrameMs, int DroppedFrames, int SampledFrames, int ActiveTasks,
    double CpuMs, double? GpuMs, double? HeapMb, int LongTasks, double WorstLongTaskMs,
    double? InpMs, double LayoutShift, int GlContexts, int GlSurfaces, int ContextLosses);

/// <param name="BaseMs">AudioContext base latency.</param>
/// <param name="OutputMs">Output latency, where the browser reports one.</param>
/// <param name="SampleRate">Context sample rate.</param>
/// <param name="ContextState">running / suspended / closed.</param>
public readonly record struct FxAudioLatency(
    double BaseMs, double OutputMs, double SampleRate, string? ContextState);

/// <summary>
/// One card on the 3D shelf. Deliberately just rank and score — the shelf renders shapes, not
/// text, so passing restaurant names or blob URLs across interop would ship data the renderer
/// cannot use and would put third-party review content into a decorative layer for no reason.
/// </summary>
/// <param name="Rank">1-based board position; drives colour and how high the card floats.</param>
/// <param name="Score">Strangeness score, 0-100.</param>
public readonly record struct FxShelfEntry(int Rank, double Score);

/// <summary>
/// Blazor-side facade over <c>wwwroot/js/fx.js</c>.
/// <para>
/// Every method swallows <see cref="JSException"/> and returns a benign default. These calls
/// decorate the app; a graphics failure surfacing through interop would show the user the
/// framework's error strip over a page that is otherwise working perfectly. Callers are written
/// to treat a zero handle as "the effect is not running", which is also what they get on a
/// device where the effect was never allowed to start.
/// </para>
/// </summary>
public sealed class FxService(IJSRuntime js)
{
    private FxCapabilities? _capabilities;

    private static FxTier ParseTier(string? tier) => tier switch
    {
        "full" => FxTier.Full,
        "lite" => FxTier.Lite,
        _ => FxTier.Off
    };

    private static string TierToJs(FxTier tier) => tier switch
    {
        FxTier.Full => "full",
        FxTier.Lite => "lite",
        _ => "off"
    };

    /// <summary>
    /// The annotation is required, not decorative: <c>IJSRuntime.InvokeAsync&lt;TValue&gt;</c>
    /// deserializes reflectively, so the trim analyzer needs to know the members of every T that
    /// flows through here are preserved. Without it this file fails the build under
    /// <c>EnableTrimAnalyzer</c> + <c>TreatWarningsAsErrors</c> (IL2091).
    /// </summary>
    private async Task<T> SafeAsync<
        [DynamicallyAccessedMembers(
            DynamicallyAccessedMemberTypes.PublicConstructors |
            DynamicallyAccessedMemberTypes.PublicFields |
            DynamicallyAccessedMemberTypes.PublicProperties)] T>(
        string identifier, T fallback, params object?[] args)
    {
        try
        {
            return await js.InvokeAsync<T>(identifier, args);
        }
        catch (JSException)
        {
            return fallback;
        }
        catch (InvalidOperationException)
        {
            // Prerender / no JS runtime available yet.
            return fallback;
        }
        catch (TaskCanceledException)
        {
            // Circuit or component torn down mid-call.
            return fallback;
        }
    }

    private async Task SafeVoidAsync(string identifier, params object?[] args)
    {
        try
        {
            await js.InvokeVoidAsync(identifier, args);
        }
        catch (JSException) { }
        catch (InvalidOperationException) { }
        catch (TaskCanceledException) { }
    }

    // ── Capabilities ─────────────────────────────────────────────────────────────────────

    public async Task<FxCapabilities> GetCapabilitiesAsync()
    {
        // Memoised: this is read on nearly every page, and the underlying detection does not
        // change unless the tier changes (which goes through SetTierAsync).
        if (_capabilities is { } cached)
        {
            return cached;
        }

        var raw = await SafeAsync<CapabilitiesPayload?>("poseeFx.describe", null);
        var result = raw is null
            ? new FxCapabilities(FxTier.Off, true, false, false)
            : new FxCapabilities(ParseTier(raw.Tier), raw.ReducedMotion, raw.Webgl2, raw.AutoDowngraded);

        _capabilities = result;
        return result;
    }

    public async Task<FxTier> SetTierAsync(FxTier tier)
    {
        var applied = await SafeAsync("poseeFx.setTier", TierToJs(tier), TierToJs(tier));
        _capabilities = null; // Force a re-read; JS may have refused the change.
        return ParseTier(applied);
    }

    public Task<FxFrameStats> GetFrameStatsAsync() =>
        SafeAsync("poseeFx.stats", default(FxFrameStats));

    public Task ResetFrameStatsAsync() => SafeVoidAsync("poseeFx.resetStats");

    /// <summary>
    /// Shows or hides the live performance overlay. Also bound to Ctrl+Shift+F and <c>?fx=debug</c>
    /// in JS, so this is a convenience for the diagnostics page rather than the only way in.
    /// </summary>
    public Task<bool> TogglePerfHudAsync() => SafeAsync("poseeFx.togglePerfHud", false);

    public Task<bool> IsPerfHudVisibleAsync() => SafeAsync("poseeFx.perfHudVisible", false);

    // ── Audio ────────────────────────────────────────────────────────────────────────────

    public Task<bool> IsAudioEnabledAsync() => SafeAsync("poseeFx.audioEnabled", false);

    /// <summary>
    /// Must be awaited from a handler on a real user gesture. Browsers only let an AudioContext
    /// start from a trusted event, so calling this from a timer leaves it permanently suspended.
    /// </summary>
    public Task<bool> SetAudioEnabledAsync(bool enabled) =>
        SafeAsync("poseeFx.setAudioEnabled", false, enabled);

    public Task UnlockAudioAsync() => SafeVoidAsync("poseeFx.unlockAudio");

    /// <summary>Output latency and context state, for the diagnostics panel. Null before unlock.</summary>
    public Task<FxAudioLatency?> GetAudioLatencyAsync() =>
        SafeAsync<FxAudioLatency?>("poseeFx.audioLatency", null);

    public Task PlayTapAsync() => SafeVoidAsync("poseeFx.playTap");

    /// <summary>
    /// Click panned to where the control actually is on screen. Prefer this over
    /// <see cref="PlayTapAsync()"/> wherever an <see cref="ElementReference"/> is already to hand:
    /// a tap that sounds from the side of the screen it happened on is the cheapest spatial cue
    /// the app has.
    /// </summary>
    public Task PlayTapAsync(ElementReference element) => SafeVoidAsync("poseeFx.playTap", element);

    /// <summary>
    /// Click panned to where the pointer was. The practical form for repeated lists: a click
    /// handler already receives <see cref="Microsoft.AspNetCore.Components.Web.MouseEventArgs"/>,
    /// so no per-item <see cref="ElementReference"/> is needed.
    /// </summary>
    public Task PlayTapAtAsync(double clientX) => SafeVoidAsync("poseeFx.playTapAt", clientX);
    public Task PlayScoreTickAsync(int value, int target) => SafeVoidAsync("poseeFx.playScoreTick", value, target);
    public Task PlayScoreLandAsync(int score) => SafeVoidAsync("poseeFx.playScoreLand", score);
    public Task PlayPhaseAsync(int index, int total) => SafeVoidAsync("poseeFx.playPhase", index, total);
    public Task PlaySplatAsync(double intensity) => SafeVoidAsync("poseeFx.playSplat", intensity);
    public Task PlayShareStingerAsync() => SafeVoidAsync("poseeFx.playShareStinger");
    public Task PlayErrorAsync() => SafeVoidAsync("poseeFx.playError");

    // ── Effects. Handles are opaque; 0 means "not running". ──────────────────────────────

    public Task<int> StartGradientAsync(ElementReference canvas, int score) =>
        SafeAsync("poseeFx.startGradient", 0, canvas, score);

    public Task SetGradientScoreAsync(int handle, int score) =>
        handle == 0 ? Task.CompletedTask : SafeVoidAsync("poseeFx.setGradientScore", handle, score);

    public Task StopGradientAsync(int handle) =>
        handle == 0 ? Task.CompletedTask : SafeVoidAsync("poseeFx.stopGradient", handle);

    public Task<int> AttachComicFxAsync(ElementReference canvas, ElementReference image) =>
        SafeAsync("poseeFx.attachComicFx", 0, canvas, image);

    public Task DetachComicFxAsync(int handle) =>
        handle == 0 ? Task.CompletedTask : SafeVoidAsync("poseeFx.detachComicFx", handle);

    public Task<int> BurstParticlesAsync(ElementReference canvas, int score) =>
        SafeAsync("poseeFx.burstParticles", 0, canvas, score);

    public Task<int> StartLoadingRingAsync(ElementReference canvas, double progress) =>
        SafeAsync("poseeFx.startLoadingRing", 0, canvas, progress);

    public Task SetLoadingRingProgressAsync(int handle, double progress) =>
        handle == 0 ? Task.CompletedTask : SafeVoidAsync("poseeFx.setLoadingRingProgress", handle, progress);

    public Task StopLoadingRingAsync(int handle) =>
        handle == 0 ? Task.CompletedTask : SafeVoidAsync("poseeFx.stopLoadingRing", handle);

    // ── Hall of Fame shelf ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Starts the 3D shelf behind the leaderboard list. The module is fetched on demand, so this
    /// is the one effect whose first call pays a network cost — deliberately, to keep a renderer
    /// off the first-load path of every other route.
    /// <para>
    /// The DOM list underneath must stay exactly where it is. It is the only keyboard-reachable
    /// and screen-reader-legible form of the leaderboard; this canvas is decoration over it.
    /// </para>
    /// </summary>
    public Task<int> StartShelfAsync(ElementReference canvas, IReadOnlyList<FxShelfEntry> entries) =>
        SafeAsync("poseeFx.startShelf", 0, canvas, entries);

    public Task StopShelfAsync(int handle) =>
        handle == 0 ? Task.CompletedTask : SafeVoidAsync("poseeFx.stopShelf", handle);

    // ── Route transitions ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Closes the view transition opened when the user clicked the link. MUST be called after
    /// every navigation: the JS side snapshots the old document and holds it until this resolves,
    /// so failing to call it would leave the page frozen under a stale image. The JS side also
    /// carries its own timeout for exactly that reason.
    /// </summary>
    public Task SettleViewTransitionAsync() => SafeVoidAsync("poseeFx.settleViewTransition");

    private sealed class CapabilitiesPayload
    {
        public string? Tier { get; set; }
        public bool ReducedMotion { get; set; }
        public bool Webgl2 { get; set; }
        public bool AutoDowngraded { get; set; }
    }
}
