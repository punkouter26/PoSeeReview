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
/// <param name="WebGpu">
/// WebGPU is available, so the compute-simulated particle burst can run instead of the
/// stateless WebGL2 one. Not a tier: the fallback is a complete effect, not a degraded one.
/// </param>
/// <param name="Haptics">The Vibration API exists. Absent on every iOS browser.</param>
/// <param name="Narration">speechSynthesis exists, so the narrative can be read aloud.</param>
public readonly record struct FxCapabilities(
    FxTier Tier, bool ReducedMotion, bool WebGl2, bool AutoDowngraded,
    bool WebGpu, bool Haptics, bool Narration);

/// <summary>
/// How hard a moderation action is to undo. The three sound different on purpose: a moderator
/// working a queue hears which one they took without reading the confirmation, and they are one
/// mis-tap apart from each other.
/// </summary>
public enum FxSeverity
{
    /// <summary>Reversible, and what unreviewed reports get.</summary>
    Hide = 0,

    /// <summary>Blocks regeneration. What makes a removal stick.</summary>
    Suppress = 1,

    /// <summary>Erases the comic, blob, board row, archive entry and every kept copy.</summary>
    Remove = 2
}

/// <summary>Which seeding the Verlet solver uses. See <c>js/physics.js</c>.</summary>
public enum FxInkMode
{
    /// <summary>Drops fall in from above and pool at the bottom. Follows the burst.</summary>
    Ink = 0,

    /// <summary>Pieces fly out from an origin and fall. Reserved for a high score.</summary>
    Shatter = 1
}

/// <param name="Supported">Whether the underlying API exists on this device at all.</param>
/// <param name="Enabled">Whether it is actually active right now.</param>
/// <param name="Explicit">The user pinned it, rather than inheriting the audio preference.</param>
public readonly record struct FxToggleState(bool Supported, bool Enabled, bool Explicit);

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
            ? new FxCapabilities(FxTier.Off, true, false, false, false, false, false)
            : new FxCapabilities(ParseTier(raw.Tier), raw.ReducedMotion, raw.Webgl2, raw.AutoDowngraded,
                raw.Webgpu, raw.Haptics, raw.Narration);

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

    /// <summary>Two rising ticks. For something added to a collection, not for a whole flow completing.</summary>
    public Task PlayConfirmAsync() => SafeVoidAsync("poseeFx.playConfirm");

    /// <summary>Moderation outcome, graded. See <see cref="FxSeverity"/> for why the three differ.</summary>
    public Task PlaySeverityAsync(FxSeverity severity) => SafeVoidAsync("poseeFx.playSeverity", severity switch
    {
        FxSeverity.Remove => "remove",
        FxSeverity.Suppress => "suppress",
        _ => "hide"
    });

    /// <summary>
    /// The comic's own four-note motif, seeded from its place id and voiced by its score.
    /// <para>
    /// Deterministic: the same restaurant always plays the same figure, which is what makes this
    /// an identity rather than a flourish — the Hall of Fame becomes something you can recognise
    /// by ear. The seed should be the place id, never the restaurant name: names collide across
    /// chains, and two branches of the same chain are not the same comic.
    /// </para>
    /// </summary>
    public Task PlaySignatureAsync(string seed, int score) =>
        SafeVoidAsync("poseeFx.playSignature", seed, score);

    /// <summary>
    /// Plays a numeric series as pitch, sweeping left to right. Long series are decimated on the
    /// JS side rather than truncated, so the contour survives — which is the only thing being
    /// communicated.
    /// </summary>
    public Task PlaySeriesAsync(IReadOnlyList<double> values) =>
        SafeVoidAsync("poseeFx.playSeries", values);

    // ── Narration ────────────────────────────────────────────────────────────────────────

    /// <summary>Whether speechSynthesis exists. Independent of whether audio has been unlocked.</summary>
    public Task<bool> CanNarrateAsync() => SafeAsync("poseeFx.canNarrate", false);

    /// <summary>
    /// Reads text aloud. Gated on the audio preference but NOT on the AudioContext — speech is a
    /// separate output that never enters the graph, so it works before the first gesture.
    /// </summary>
    public Task<bool> NarrateAsync(string text) => SafeAsync("poseeFx.narrate", false, text);

    public Task StopNarrationAsync() => SafeVoidAsync("poseeFx.stopNarration");

    // ── Haptics ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Haptics follow the audio preference and can be switched off on their own, never on on
    /// their own — someone who muted the app did not ask to be buzzed instead.
    /// </summary>
    public Task<FxToggleState> GetHapticsAsync() =>
        SafeAsync("poseeFx.hapticsDescribe", default(FxToggleState));

    public Task<bool> SetHapticsEnabledAsync(bool enabled) =>
        SafeAsync("poseeFx.setHapticsEnabled", false, enabled);

    // ── Ambient bed ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Starts the generative bed, if audio is on and unlocked and the user has not opted out.
    /// Safe to call repeatedly — a second call retunes the running node rather than stacking a
    /// second one, which is what a route re-entry or a regenerate should do.
    /// </summary>
    public Task<bool> StartAmbientAsync(int score) => SafeAsync("poseeFx.startAmbient", false, score);

    public Task SetAmbientScoreAsync(int score) => SafeVoidAsync("poseeFx.setAmbientScore", score);

    public Task StopAmbientAsync() => SafeVoidAsync("poseeFx.stopAmbient");

    public Task<FxToggleState> GetAmbientAsync() =>
        SafeAsync("poseeFx.ambientDescribe", default(FxToggleState));

    public Task<bool> SetAmbientEnabledAsync(bool enabled) =>
        SafeAsync("poseeFx.setAmbientEnabled", false, enabled);

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

    /// <summary>
    /// An expanding ring of displacement and chromatic split through the panel, fired when the
    /// score lands. Origin is 0..1 across and down the panel; the score ring sits above the
    /// strip, so callers pass the top edge rather than the centre.
    /// <para>
    /// Requires the post-process to have attached, which it only does at the Full tier and only
    /// when the blob is CORS-readable — so a zero handle is the ordinary case, not a failure.
    /// </para>
    /// </summary>
    public Task ComicShockwaveAsync(int handle, int score, double originX, double originY) =>
        handle == 0 ? Task.CompletedTask
                    : SafeVoidAsync("poseeFx.comicShockwave", handle, score, originX, originY);

    // ── Ink development ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Develops the comic onto the page over about 1.4 seconds instead of popping it into the
    /// layout in one frame — the single frame that was carrying the payoff of a ten-second wait.
    /// <para>
    /// Two layers. A CSS mask on the container, which always runs above the Off tier and is what
    /// nearly everyone sees; and the shader's ink-threshold boundary on top of it when
    /// <paramref name="comicFxHandle"/> is non-zero. Pass 0 and the CSS mask runs alone.
    /// </para>
    /// </summary>
    /// <param name="container">The .comic-strip-container: it carries the mask over image and canvas together.</param>
    /// <param name="bands">Panels to develop in sequence. Two matches the server's cap.</param>
    public Task<int> StartComicRevealAsync(ElementReference container, int bands, int comicFxHandle) =>
        SafeAsync("poseeFx.startComicReveal", 0, container, bands, comicFxHandle);

    /// <summary>
    /// Ends a reveal with the comic fully visible. MUST be called on teardown: the mask only
    /// applies while the element carries <c>data-comic-reveal</c>, so abandoning a running reveal
    /// would leave part of the comic permanently hidden.
    /// </summary>
    public Task FinishComicRevealAsync(int handle) =>
        handle == 0 ? Task.CompletedTask : SafeVoidAsync("poseeFx.finishComicReveal", handle);

    /// <summary>
    /// Fires the ink burst. Prefers the WebGPU compute backend, where the drops decelerate, hit
    /// the bottom of the panel and settle; falls back to the stateless WebGL2 sim, which fades
    /// out mid-air because a vertex-shader simulation cannot know the floor exists.
    /// </summary>
    public Task<int> BurstParticlesAsync(ElementReference canvas, int score) =>
        SafeAsync("poseeFx.burstParticles", 0, canvas, score);

    public Task StopParticlesAsync(int handle) =>
        handle == 0 ? Task.CompletedTask : SafeVoidAsync("poseeFx.stopParticles", handle);

    // ── Physics ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Where the ink ends up: a Verlet pile that collides with itself and the bottom of the
    /// frame. Lazy-loaded on first use and Full tier only, on the same terms the 3D shelf is —
    /// no library, one route, and the real element stays underneath.
    /// </summary>
    /// <param name="score">Scales the drop count and how hard they are thrown.</param>
    /// <param name="originX">0..1 across the canvas. Only used by <see cref="FxInkMode.Shatter"/>.</param>
    /// <param name="originY">0..1 down the canvas. Only used by <see cref="FxInkMode.Shatter"/>.</param>
    public Task<int> SettleInkAsync(ElementReference canvas, int score, FxInkMode mode,
        double originX = 0.5, double originY = 0.5) =>
        SafeAsync("poseeFx.settleInk", 0, canvas, score,
            mode == FxInkMode.Shatter ? "shatter" : "ink", originX, originY);

    public Task StopInkAsync(int handle) =>
        handle == 0 ? Task.CompletedTask : SafeVoidAsync("poseeFx.stopInk", handle);

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
        public bool Webgpu { get; set; }
        public bool Haptics { get; set; }
        public bool Narration { get; set; }
    }
}
