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
/// <param name="Haptics">The Vibration API exists. Absent on every iOS browser.</param>
/// <param name="Narration">speechSynthesis exists, so the narrative can be read aloud.</param>
public readonly record struct FxCapabilities(
    FxTier Tier, bool ReducedMotion, bool WebGl2, bool AutoDowngraded,
    bool Haptics, bool Narration);

/// <param name="Supported">Whether the underlying API exists on this device at all.</param>
/// <param name="Enabled">Whether it is actually active right now.</param>
/// <param name="Explicit">The user pinned it, rather than inheriting the audio preference.</param>
public readonly record struct FxToggleState(bool Supported, bool Enabled, bool Explicit);

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

    public async Task<FxCapabilities> GetCapabilitiesAsync(bool refresh = false)
    {
        // Memoised: this is read on nearly every page. The tier also changes without going
        // through SetTierAsync — the frame watchdog downgrades it — so a live readout passes refresh.
        if (!refresh && _capabilities is { } cached)
        {
            return cached;
        }

        var raw = await SafeAsync<CapabilitiesPayload?>("poseeFx.describe", null);
        var result = raw is null
            ? new FxCapabilities(FxTier.Off, true, false, false, false, false)
            : new FxCapabilities(ParseTier(raw.Tier), raw.ReducedMotion, raw.Webgl2, raw.AutoDowngraded,
                raw.Haptics, raw.Narration);

        _capabilities = result;
        return result;
    }

    public async Task<FxTier> SetTierAsync(FxTier tier)
    {
        var applied = await SafeAsync("poseeFx.setTier", TierToJs(tier), TierToJs(tier));
        _capabilities = null; // Force a re-read; JS may have refused the change.
        return ParseTier(applied);
    }

    // ── Audio ────────────────────────────────────────────────────────────────────────────

    public Task<bool> IsAudioEnabledAsync() => SafeAsync("poseeFx.audioEnabled", false);

    /// <summary>
    /// Must be awaited from a handler on a real user gesture. Browsers only let an AudioContext
    /// start from a trusted event, so calling this from a timer leaves it permanently suspended.
    /// </summary>
    public Task<bool> SetAudioEnabledAsync(bool enabled) =>
        SafeAsync("poseeFx.setAudioEnabled", false, enabled);

    public Task UnlockAudioAsync() => SafeVoidAsync("poseeFx.unlockAudio");

    /// <summary>Master volume, 0–1. Persisted by the JS side alongside the on/off preference.</summary>
    public Task<double> GetVolumeAsync() => SafeAsync("poseeFx.volume", 1d);

    public Task<double> SetVolumeAsync(double volume) => SafeAsync("poseeFx.setVolume", volume, volume);

    /// <summary>
    /// The whole count-up, scheduled on the audio clock in one call — the ticks used to cost an
    /// interop round trip each. Ends on the same landing chord as <see cref="PlayScoreLandAsync"/>.
    /// </summary>
    public Task PlayScoreRollAsync(int score, int durationMs) =>
        SafeVoidAsync("poseeFx.playScoreRoll", score, durationMs);

    /// <summary>False while the tab is hidden, so a poll can skip a beat nobody would see.</summary>
    public Task<bool> IsPageVisibleAsync() => SafeAsync("poseeFx.pageVisible", false);

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
    public Task PlayShareStingerAsync() => SafeVoidAsync("poseeFx.playShareStinger");
    public Task PlayErrorAsync() => SafeVoidAsync("poseeFx.playError");

    /// <summary>Two rising ticks, for an action that landed.</summary>
    public Task PlayConfirmAsync() => SafeVoidAsync("poseeFx.playConfirm");

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
    /// "Too normal to draw": a deflating sad trombone. Not <see cref="PlayErrorAsync"/> — the
    /// place was rejected for being boring, which is a joke about the place, not a malfunction.
    /// </summary>
    public Task PlayFizzleAsync() => SafeVoidAsync("poseeFx.playFizzle");

    // ── Generation wait ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// A stable-fluids ink simulation behind the storyboard for the ten-second wait; the pointer
    /// stirs it. Full tier only — 0 elsewhere, and the storyboard alone carries the wait.
    /// </summary>
    public Task<int> StartInkAsync(ElementReference canvas) => SafeAsync("poseeFx.startInk", 0, canvas);

    public Task InkPhaseAsync(int handle, int index, int total) =>
        handle == 0 ? Task.CompletedTask : SafeVoidAsync("poseeFx.inkPhase", handle, index, total);

    /// <summary>Pulls the ink into the frame over about 700ms; the module then stops itself.</summary>
    public Task DrainInkAsync(int handle) =>
        handle == 0 ? Task.CompletedTask : SafeVoidAsync("poseeFx.drainInk", handle);

    public Task StopInkAsync(int handle) =>
        handle == 0 ? Task.CompletedTask : SafeVoidAsync("poseeFx.stopInk", handle);

    // ── Score burst ──────────────────────────────────────────────────────────────────────

    /// <summary>Builds the burst's GPU pipelines early; call when a comic is about to reveal.</summary>
    public Task PrepareBurstAsync() => SafeVoidAsync("poseeFx.prepareBurst");

    /// <summary>
    /// Particles from the score ring, sized by the score — WebGPU compute where available, WebGL2
    /// otherwise, nothing below the full tier. Above 50 the backdrop ripples and a thump lands.
    /// </summary>
    public Task<int> BurstScoreAsync(ElementReference ring, int score) =>
        SafeAsync("poseeFx.burstScore", 0, ring, score);

    /// <summary>Grey motes that pop and flop, from the element the selector finds.</summary>
    public Task FizzleAtAsync(string selector) => SafeVoidAsync("poseeFx.fizzleAt", selector);

    // ── Soundtrack ───────────────────────────────────────────────────────────────────────

    /// <summary>The comic's generated loop is opt-in, unlike every other sound.</summary>
    public Task<bool> IsSoundtrackEnabledAsync() => SafeAsync("poseeFx.soundtrackEnabled", false);

    /// <summary>Call from the click itself: switching it on may be what unlocks the audio.</summary>
    public Task<bool> SetSoundtrackEnabledAsync(bool enabled) =>
        SafeAsync("poseeFx.setSoundtrackEnabled", false, enabled);

    /// <summary>
    /// Asks for this comic's four-bar loop, seeded by place id and voiced by score. Remembered
    /// when it cannot play yet, so switching music on later picks it up.
    /// </summary>
    public Task StartSoundtrackAsync(string seed, int score) =>
        SafeVoidAsync("poseeFx.startSoundtrack", seed, score);

    /// <summary>Must be called when the comic leaves the screen, or the loop follows the user.</summary>
    public Task StopSoundtrackAsync() => SafeVoidAsync("poseeFx.stopSoundtrack");

    // ── Narration ────────────────────────────────────────────────────────────────────────

    /// <summary>Whether speechSynthesis exists. Independent of whether audio has been unlocked.</summary>
    public Task<bool> CanNarrateAsync() => SafeAsync("poseeFx.canNarrate", false);

    /// <summary>
    /// Reads text aloud. Gated on the audio preference but NOT on the AudioContext — speech is a
    /// separate output that never enters the graph, so it works before the first gesture.
    /// </summary>
    public Task<bool> NarrateAsync(string text) => SafeAsync("poseeFx.narrate", false, text);

    public Task StopNarrationAsync() => SafeVoidAsync("poseeFx.stopNarration");

    /// <summary>Completes when the current utterance ends, is cancelled, or was never started.</summary>
    public Task WaitNarrationEndAsync() => SafeVoidAsync("poseeFx.narrationEnded");

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

    // ── Ink development ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Develops the comic onto the page over about 1.4 seconds instead of popping it into the
    /// layout in one frame — the single frame that was carrying the payoff of a ten-second wait.
    /// A CSS mask on the container, so it runs above the Off tier with no WebGL at all.
    /// </summary>
    /// <param name="container">The .comic-strip-container: the mask covers everything inside it.</param>
    /// <param name="bands">Panels to develop in sequence. Two matches the server's cap.</param>
    /// <param name="placeId">
    /// Lets the full tier print the comic instead — CMYK plates landing in register over the
    /// same-origin copy of the artwork. Without it, or when that copy does not arrive, the mask runs.
    /// </param>
    public Task<int> StartComicRevealAsync(ElementReference container, int bands, string? placeId = null) =>
        SafeAsync("poseeFx.startComicReveal", 0, container, bands, placeId);

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

    // ── Leaderboard ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Slides every row carrying <c>data-delta</c> from where it sat last visit to where it sits
    /// now, each with its own panned climb or fall. Call after the rows have rendered.
    /// </summary>
    public Task AnimateBoardMovesAsync(ElementReference container) =>
        SafeVoidAsync("poseeFx.animateBoardMoves", container);

    // ── Hot off the press ────────────────────────────────────────────────────────────────

    /// <summary>Prepares the curl while the share sheet is up, so it does not start cold.</summary>
    public Task WarmPeelAsync() => SafeVoidAsync("poseeFx.warmPeel");

    /// <summary>
    /// A printed copy of the comic on the page curls off and flies into
    /// <paramref name="targetSelector"/>, then a CERTIFIED STRANGE stamp lands on the original.
    /// </summary>
    public Task PeelComicAsync(string placeId, string targetSelector) =>
        SafeVoidAsync("poseeFx.peelComic", placeId, targetSelector);

    // ── 3D mode ──────────────────────────────────────────────────────────────────────────

    /// <summary>WebGL2 and a tier above Off: whether the 3D toggle is worth showing.</summary>
    public Task<bool> IsDepthSupportedAsync() => SafeAsync("poseeFx.depthSupported", false);

    /// <summary>
    /// Turns the strip into a tilting 2.5D diorama. <paramref name="listener"/> is told through
    /// <c>DepthEnded</c> when the effect stops on its own, so the toggle does not stay pressed.
    /// </summary>
    public Task<int> StartDepthAsync<T>(string placeId, DotNetObjectReference<T> listener) where T : class =>
        SafeAsync("poseeFx.startDepth", 0, placeId, listener);

    public Task StopDepthAsync(int handle) =>
        handle == 0 ? Task.CompletedTask : SafeVoidAsync("poseeFx.stopDepth", handle);

    // ── Duel ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Strikes the arc between the two halves of the duel card, leaning toward the stranger
    /// comic, then floods the winner's side while each comic's motif answers the other. Full
    /// tier only; below it the page's CSS seam stands in.
    /// </summary>
    public Task<int> StartDuelAsync(ElementReference canvas, string seedA, int scoreA, string seedB, int scoreB) =>
        SafeAsync("poseeFx.startDuel", 0, canvas, seedA, scoreA, seedB, scoreB);

    public Task StopDuelAsync(int handle) =>
        handle == 0 ? Task.CompletedTask : SafeVoidAsync("poseeFx.stopDuel", handle);

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
        public bool Haptics { get; set; }
        public bool Narration { get; set; }
    }
}
