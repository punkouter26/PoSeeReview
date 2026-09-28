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
/// One voice of the leaderboard chord.
/// </summary>
/// <param name="Seed">
/// The place id, never the restaurant name. Names collide across chains, and two branches of the
/// same chain are not the same comic — a motif that cannot tell them apart is not an identity.
/// </param>
/// <param name="Score">Strangeness, 0-100. Picks the scale, tempo and timbre.</param>
public readonly record struct FxMotif(string Seed, double Score);

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
    /// <summary>
    /// A search leaving — location request or text search. A ping and a delayed, quieter echo of
    /// it: the gap is what distinguishes "a request is outstanding" from "a button was pressed",
    /// which on the landing page were previously the same sound.
    /// </summary>
    public Task PlayLocatingAsync() => SafeVoidAsync("poseeFx.playLocating");

    /// <summary>
    /// Results landing. The figure's LENGTH carries the count, capped at five notes — "a lot came
    /// back" and "barely anything came back" are the two states the user is about to act on.
    /// </summary>
    public Task PlayArrivalAsync(int count) => SafeVoidAsync("poseeFx.playArrival", count);

    /// <summary>
    /// Zero results. Deliberately not <see cref="PlayErrorAsync"/>: an empty answer is an answer,
    /// and a search that found nothing should not sound like the app broke.
    /// </summary>
    public Task PlayEmptyAsync() => SafeVoidAsync("poseeFx.playEmpty");

    /// <summary>
    /// Tap on a restaurant card, voiced by whether that comic already exists.
    /// <para>
    /// A cache hit opens instantly and costs nothing; a miss spends a paid image call and about
    /// ten seconds of waiting. They are adjacent controls in the same grid with wildly different
    /// consequences, and until now they made exactly the same click.
    /// </para>
    /// </summary>
    public Task PlayTapAtAsync(double clientX, bool cached) =>
        SafeVoidAsync("poseeFx.playTapCached", clientX, cached);

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
    /// The top of the board as a chord, each voice one restaurant's own motif.
    /// <para>
    /// Because a motif is deterministic from its place id, a board that has changed sounds
    /// different from one that has not — before the visitor has read a single row. The Hall of
    /// Fame had a 3D shelf on it and not one sound.
    /// </para>
    /// </summary>
    public Task PlayBoardChordAsync(IReadOnlyList<FxMotif> entries) =>
        SafeVoidAsync("poseeFx.playBoardChord", entries);

    /// <summary>
    /// A row that moved since this visitor last saw the board. Positive is a climb.
    /// </summary>
    /// <param name="pan">-1..1, so the cue comes from where the row is on screen.</param>
    public Task PlayRankDeltaAsync(int delta, double pan) =>
        SafeVoidAsync("poseeFx.playRankDelta", delta, pan);

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

    /// <summary>
    /// Plays the comic's conversation. Takes the skit PRE-SERIALIZED by <c>AppJsonContext</c>:
    /// a complex type as an interop argument would be serialized reflectively, which is the
    /// exact thing the source-generated context exists to avoid on a trim-analyzed client.
    /// Shares speechSynthesis with narration, so <see cref="StopNarrationAsync"/> stops it too —
    /// one queue, one cancel.
    /// <para>
    /// Each line pops a speech bubble over <paramref name="strip"/> as it starts speaking, so
    /// the voices have faces. The bubbles are cleared by the same stop.
    /// </para>
    /// </summary>
    public Task<bool> PlaySkitJsonAsync(string skitJson, ElementReference strip) =>
        SafeAsync("poseeFx.playSkit", false, skitJson, strip);

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

    // ── Effects. Handles are opaque; 0 means "not running". ──────────────────────────────

    public Task<int> StartGradientAsync(ElementReference canvas, int score) =>
        SafeAsync("poseeFx.startGradient", 0, canvas, score);

    public Task SetGradientScoreAsync(int handle, int score) =>
        handle == 0 ? Task.CompletedTask : SafeVoidAsync("poseeFx.setGradientScore", handle, score);

    public Task StopGradientAsync(int handle) =>
        handle == 0 ? Task.CompletedTask : SafeVoidAsync("poseeFx.stopGradient", handle);

    /// <summary>
    /// Dresses the shader backdrop in the comic's own colours.
    /// <para>
    /// The palette is sampled on the SERVER, off the finished image bytes. It cannot be sampled
    /// here: the comic blob is served without CORS headers, so a canvas that has drawn it is
    /// tainted and cannot be read back.
    /// </para>
    /// </summary>
    /// <returns>Whether a palette was actually applied. False for a comic drawn before the
    /// extractor existed, which renders on brand tokens exactly as it always did.</returns>
    public Task<bool> SetComicPaletteAsync(int handle, IReadOnlyList<string> palette) =>
        handle == 0 ? Task.FromResult(false)
                    : SafeAsync("poseeFx.setComicPalette", false, handle, palette);

    /// <summary>
    /// Restores the brand gradient. MUST be called when leaving a comic: the variables are set
    /// on the document element, so a tint left behind follows the user to the next route and
    /// colours a page with no comic to justify it.
    /// </summary>
    public Task ClearComicPaletteAsync(int handle) =>
        handle == 0 ? Task.CompletedTask : SafeVoidAsync("poseeFx.clearComicPalette", handle);

    /// <summary>
    /// Viewport width, for converting a pointer coordinate into a fraction of a full-width
    /// canvas. Falls back to 1, which every caller clamps against — a bad width places an effect
    /// in the wrong spot, never off the canvas.
    /// </summary>
    public Task<double> GetViewportWidthAsync() => SafeAsync("poseeFx.viewportWidth", 1d);

    // ── Ink development ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Develops the comic onto the page over about 1.4 seconds instead of popping it into the
    /// layout in one frame — the single frame that was carrying the payoff of a ten-second wait.
    /// A CSS mask on the container, so it runs above the Off tier with no WebGL at all.
    /// </summary>
    /// <param name="container">The .comic-strip-container: the mask covers everything inside it.</param>
    /// <param name="bands">Panels to develop in sequence. Two matches the server's cap.</param>
    public Task<int> StartComicRevealAsync(ElementReference container, int bands) =>
        SafeAsync("poseeFx.startComicReveal", 0, container, bands);

    /// <summary>
    /// Ends a reveal with the comic fully visible. MUST be called on teardown: the mask only
    /// applies while the element carries <c>data-comic-reveal</c>, so abandoning a running reveal
    /// would leave part of the comic permanently hidden.
    /// </summary>
    public Task FinishComicRevealAsync(int handle) =>
        handle == 0 ? Task.CompletedTask : SafeVoidAsync("poseeFx.finishComicReveal", handle);

    /// <summary>
    /// Plays the comic as it is read: one note of its own motif per panel, as that panel crosses
    /// the middle of the screen.
    /// <para>
    /// The notes come from the same seeded sequence the signature uses, so what a reader hears
    /// while scrolling is the figure that played when the score landed — same notes, same order,
    /// at their own pace. The travelling highlight that accompanies it is CSS
    /// (<c>animation-timeline: view()</c>) and costs nothing in the frame budget; only the
    /// boundary crossing needs JS.
    /// </para>
    /// </summary>
    /// <param name="seed">Place id, never the restaurant name — names collide across chains.</param>
    public Task<int> StartPanelScrubAsync(ElementReference container, int panels, string seed, int score) =>
        SafeAsync("poseeFx.startPanelScrub", 0, container, panels, seed, score);

    public Task StopPanelScrubAsync(int handle) =>
        handle == 0 ? Task.CompletedTask : SafeVoidAsync("poseeFx.stopPanelScrub", handle);

    /// <summary>Fires the WebGL2 ink burst, sized to the score. Full tier only.</summary>
    public Task<int> BurstParticlesAsync(ElementReference canvas, int score) =>
        SafeAsync("poseeFx.burstParticles", 0, canvas, score);

    public Task StopParticlesAsync(int handle) =>
        handle == 0 ? Task.CompletedTask : SafeVoidAsync("poseeFx.stopParticles", handle);

    // ── Generation wait ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// One pipeline phase landed: its blip, plus one more sustained voice stacked on the riser,
    /// so ten seconds of waiting builds instead of beeping. <paramref name="score"/> is 0 until
    /// the analysis has produced one; a strange score detunes the stack.
    /// </summary>
    public Task PlayRiserStepAsync(int index, int total, int score) =>
        SafeVoidAsync("poseeFx.riserStep", index, total, score);

    /// <summary>Lets the riser ring out as the comic arrives. The score reveal follows it.</summary>
    public Task ResolveRiserAsync() => SafeVoidAsync("poseeFx.riserResolve");

    /// <summary>Cuts the riser. MUST be called on failure and teardown — it is sustained.</summary>
    public Task StopRiserAsync() => SafeVoidAsync("poseeFx.riserStop");

    // ── The comic as an object ───────────────────────────────────────────────────────────

    /// <summary>
    /// Attaches the strip's pointer surface: loupe support always, and holographic foil that
    /// follows the pointer (or the phone's tilt) when <paramref name="holo"/> is set. All of it
    /// is CSS drawing the comic's own URL, so none of it reads pixels and the cross-origin blob
    /// is no obstacle.
    /// </summary>
    public Task<int> StartComicSurfaceAsync(ElementReference container, ElementReference image, bool holo) =>
        SafeAsync("poseeFx.startComicSurface", 0, container, image, holo);

    /// <summary>Loupe on/off. Call from a click: turning it on also starts its hum.</summary>
    public Task<bool> ToggleLoupeAsync(int handle) =>
        handle == 0 ? Task.FromResult(false) : SafeAsync("poseeFx.toggleLoupe", false, handle);

    /// <summary>A displacement ripple through the artwork, as deep as the score is strange.</summary>
    public Task WobbleComicAsync(int handle, int score) =>
        handle == 0 ? Task.CompletedTask : SafeVoidAsync("poseeFx.wobbleComic", handle, score);

    /// <summary>MUST be called on teardown: the loupe hum is sustained.</summary>
    public Task StopComicSurfaceAsync(int handle) =>
        handle == 0 ? Task.CompletedTask : SafeVoidAsync("poseeFx.stopComicSurface", handle);

    /// <summary>
    /// The page misbehaves for an absurd score: the nav twitches, the title splits, the pointer
    /// leaves ink, and a detuned drone sits under everything. MUST be cleared on teardown — it is
    /// stamped on the document element and would follow the user to the next route.
    /// </summary>
    public Task SetWeirdAsync(int score) => SafeVoidAsync("poseeFx.setWeird", score);

    public Task ClearWeirdAsync() => SafeVoidAsync("poseeFx.clearWeird");

    /// <summary>A comic-book sound word ("POW!") stamped at a viewport point, with its thwack.</summary>
    public Task StampAsync(string word, double clientX, double clientY) =>
        SafeVoidAsync("poseeFx.stamp", word, clientX, clientY);

    /// <summary>The same stamp, centred on an element.</summary>
    public Task StampAsync(string word, ElementReference element) =>
        SafeVoidAsync("poseeFx.stamp", word, element);

    // ── Leaderboard ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Slides every row carrying <c>data-delta</c> from where it sat last visit to where it sits
    /// now, each with its own panned climb or fall. Call after the rows have rendered.
    /// </summary>
    public Task AnimateBoardMovesAsync(ElementReference container) =>
        SafeVoidAsync("poseeFx.animateBoardMoves", container);

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
