# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

PoSeeReview turns Google Maps restaurant reviews into AI-generated four-panel comic strips with a
0–100 "strangeness score", ranked on a regional Hall of Fame leaderboard. One ASP.NET Core 10 host
serves both the API and the Blazor WASM client from the same origin (BFF pattern — no CORS, no
tokens in the browser). See [README.md](README.md) for the full PRD.

## Commands

```powershell
dotnet restore
dotnet build PoSeeReview.sln
dotnet run   --project src/PoSeeReview.Api --launch-profile https   # HTTP 5000 / HTTPS 5001
dotnet watch --project src/PoSeeReview.Api --launch-profile https   # code-only changes
```

Config changes (appsettings, Key Vault) need a full restart — hot reload will not pick them up.

VS Code tasks `start-api-clean` / `start-api-watch-clean` do the correct sequence:
kill stray `dotnet` processes → start Azurite → start the API.

### Tests

Four tiers, all xUnit. `dotnet test PoSeeReview.sln` runs everything, but E2EUI needs a live app.

```powershell
dotnet test tests/PoSeeReview.Unit          # pure unit
dotnet test tests/PoSeeReview.Integration   # Testcontainers Azurite (needs Docker)
dotnet test tests/PoSeeReview.E2EAPI        # in-memory host + Testcontainers Azurite, AI mocked
$env:E2E_BASE_URL = "https://localhost:5001"; dotnet test tests/PoSeeReview.E2EUI  # C# Playwright

# single test / class
dotnet test tests/PoSeeReview.Unit --filter "FullyQualifiedName~GeoUtilsTests"
```

Tier quotas (NET_RULES 5.1): Unit 100 / Integration 50 / E2EAPI 25 / E2EUI 25. There is exactly
one E2E UI suite — the C# Playwright project. Playwright browsers install via
`pwsh tests/PoSeeReview.E2EUI/bin/Debug/net10.0/playwright.ps1 install chromium` after a build.

### Local storage

```powershell
docker compose up -d azurite    # container name is "PoSeeReview", ports 10000-10002
# Uses the official mcr.microsoft.com/azure-storage/azurite image. The hand-rolled
# Dockerfile.azurite it replaced only ran `npm install -g azurite`, and had been deleted
# while docker-compose.yml still pointed at it — `docker compose up` failed on a clean clone.
```

Set `"Storage:UseAzurite": true` in `appsettings.Development.json` — without it local dev
short-circuits to the **real** Azure storage account via Key Vault.

### Deploy & smoke

`git push origin master` triggers [.github/workflows/deploy.yml](.github/workflows/deploy.yml):
`build` → `deploy` (App Service `app-poseereview`, RG `PoSeeReview`) → `smoke`. The tiered test
suites deliberately do **not** run in CI.

```powershell
$env:BASE_URL = "https://app-poseereview.azurewebsites.net"; node SCRIPTS/post-deploy-smoke.mjs
```

`SCRIPTS/setup.ps1` is the first-run bootstrap (WinGet/Docker/Azure CLI checks).

## Architecture

### Vertical slices, and the rules that keep them honest

```
src/PoSeeReview.Api        ASP.NET Core host; also serves the WASM client
  Features/<Slice>/        endpoints + handlers + entities + repositories + services together
                           (Auth, Comics, Restaurants, Leaderboard, DevSessions, Diagnostics,
                            Takedowns, Reports, Reactions, Analytics)
  Storage/                 cross-slice TableStorageRepository, BlobStorageService
  Identity/                ICurrentRequestIdentityAccessor + HttpContext impl
  Telemetry/               App Insights + OpenTelemetry, RoleNameTelemetryInitializer
  Testing/                 AiMockDelegatingHandler — never registered in Production
src/PoSeeReview.Client     mobile-first Blazor WASM, no tokens
src/PoSeeReview.Shared     wire DTOs, Ids/, Enums/, Contracts/, FluentValidation rules
```

**Slices must not reference each other.** Anything two slices need lives in
`PoSeeReview.Shared/Contracts/` (`Comic`, `Restaurant`, `Review`, `LeaderboardEntry`;
`IComicRepository`, `ILeaderboardRepository`, `IRestaurantService`, `ILeaderboardService`,
`IHallOfFameArchive`). `IHallOfFameArchive` exists for exactly the reason `ILeaderboardRepository`
does: Takedowns must erase archived entries without referencing the Leaderboard slice that owns
them. Only the delete is exposed there — reads stay in the slice.
[Features/FeatureEndpoints.cs](src/PoSeeReview.Api/Features/FeatureEndpoints.cs) is the composition
root and is the only file allowed to reference every slice. `Abstractions/IMockable.cs` is a
cross-cutting marker deliberately outside any slice. Each slice owns its own options type.

Folder == namespace == slice: the old Application/Core/Infrastructure projects were collapsed into
Api and their namespaces retired, so every type is `PoSeeReview.Api.<Folder>`.

### Domain primitives, and where strings survive

`Shared/Ids/` holds `readonly record struct` ids — `PlaceId`, `ComicId`, `UserId`, `RegionCode` —
used across domain models and service/repository signatures. Two boundaries still speak raw strings
and convert at the edge:

- **Table entities** (`ComicEntity`, `LeaderboardEntity`, `RestaurantEntity`) — the Table SDK
  persists primitives only; `FromDomain`/`ToDomain` wrap them.
- **Wire DTOs** — keeps the client's source-generated `AppJsonContext` trim-safe. Endpoints convert
  with `PlaceId.From(...)` / `.Value`.

### Authentication (BFF cookie proxy)

The WASM client never handles tokens. Session = `.PoSeeReview.Auth` cookie (HttpOnly,
SameSite=Strict, Secure) issued by the API. Routes: `/auth/login/microsoft` (Entra `/common` OIDC),
`/auth/login/fake` (guest cookie, 404 in Prod), `/auth/logout`, `/auth/me`.

**Authz is deny-by-default**: `AddBffAuthentication` sets a `FallbackPolicy` of
`RequireAuthenticatedUser`. Public endpoints must opt out with `.AllowAnonymous()` — `/auth`,
`/health*`, `/diag`, `/api/devsession`, `/api/takedowns` (own X-Api-Key filter), OpenAPI/Scalar, and
the SPA `index.html` fallback. Business slices require a session. Client `[Authorize]` is UI-only.

`FakeAuthHandler` maps `X-Fake-User`/`X-Fake-Roles` to a principal in Dev/Test; its constructor
throws in Production. Integration and E2EAPI clients authenticate by sending `X-Fake-User`.
The `X-Dev-User-Id` identity override is honored only outside Production.

The login view renders the guest button in **both** Development and Test — every E2E UI test clicks
it, so a Test-only auto-navigate silently breaks that suite.

### Configuration & secrets

- .NET 10 pinned by `global.json`; CPM via `Directory.Packages.props`; `TreatWarningsAsErrors` +
  `Nullable` global via `Directory.Build.props`. MinVer drives versioning from git tags (`v` prefix).
- Key Vault `kv-poshared`, secrets prefix-scoped `PoSeeReview--` via `PrefixKeyVaultSecretManager`,
  `DefaultAzureCredential` throughout.
- Azure storage access is **Managed Identity only** (`AzureStorage:TableEndpoint`/`BlobEndpoint`).
  Connection strings exist solely for local Azurite and never touch Key Vault.
- `Takedowns:ApiKey` gates `/api/takedowns` (timing-safe compare, 503 when unset). It appears in no
  appsettings file — Key Vault in Azure, `dotnet user-secrets set "Takedowns:ApiKey" "<value>"`
  locally. The config path is the constant `TakedownOptions.ApiKeyConfigurationKey`.
- `AzureAd:ClientId` and `AzureAd:AllowedTenants` stay in appsettings.json on purpose — public
  application identifiers, not credentials. `AzureAd:ClientSecret` is Key Vault only.
- AI backend is selected by `Ai:ImageProvider`, bound to the `AiImageProvider` enum
  (`Gemini` default, `HuggingFace`) in `InfrastructureServiceCollectionExtensions`. It replaced a
  `UseHuggingFace` boolean; an unparseable value fails startup rather than silently falling back
  to the paid default. There is deliberately **no** `AzureOpenAI` member — only two
  `IImageGenerationService` implementations exist, so a third value could only ever throw. The
  choice selects the chat provider too; that pairing is real (HuggingFace's chat and image
  endpoints share a token), not an oversight.

### Pipeline ordering that matters

`UseForwardedHeaders()` runs first so the rate limiter and request logging see the real client IP
behind App Service's proxy. `MapFallbackToFile("index.html")` must be a real endpoint in the main
pipeline (not a `MapWhen` branch after `UseAuthorization`) or its `AllowAnonymous` metadata is
ignored and the deny-by-default policy 401s `/`.

Rate limiting: global fixed window (`RateLimiting:GlobalPermitLimit`, 240/min) partitioned by IP,
plus `comics-post` at 3/min on the paid generation endpoint. The SPA document and the whole `/auth`
group call `.DisableRateLimiting()` — a 429 on `index.html` returns no HTML at all, so the user gets
a blank white page with no error UI and no retry.

Tables and the comics blob container are created once at startup by `TableStorageInitializer`
(`IHostedService`); repositories never call `CreateIfNotExists` per request, and startup fails fast
if storage is unreachable.

### Comic generation: streaming

`AnalyzeStrangenessAsync` returns a `StrangenessAnalysis` record (not a tuple) carrying the score,
panel count, and narrative.

**Strangeness receipts were removed.** The comic used to ship model-claimed verbatim review
fragments, gated by a `VerifyReceipts` check that dropped any quote not present in the reviews
actually sent. The UI that rendered them was deleted, which left the whole vertical — prompt
tokens, the verification gate, a `ReceiptsJson` column, and `ComicDto.Receipts` — running with no
consumer, still shipping third-party quotes to every client. It was pruned end to end rather than
left half-connected. Existing Table rows keep an orphaned `ReceiptsJson` column, which Table
Storage ignores. **If receipts ever come back, the verbatim gate must come back with them**: these
strings render as quotations from real reviewers about a named restaurant, so a fabricated one is
a defamation problem, not a cosmetic bug.

`POST /api/comics/{placeId}/stream` runs the same pipeline as the plain POST but emits server-sent
events, one JSON `ComicGenerationEventDto` per `data:` line (`phase` / `complete` / `error`). It
carries the same `comics-post` limiter — otherwise it would be a way around the 3/min cap on the
only endpoint that spends money. Notes:

- The stream is a 200 before generation can fail, so the real status travels in `ErrorStatus`;
  `ComicsEndpoints.DescribeFailure` is shared with the plain POST so both agree on what a 422 means.
- Phases reach the response through a `Channel`, never straight from `IProgress.Report` — the
  BCL `Progress<T>` has no SynchronizationContext here and would post callbacks to the thread pool,
  racing the completion write.
- `ComicGenerationPhase` members map 1:1 to real pipeline steps and are ordered by
  `ComicView._comicSteps`. Add a phase in both places or the stepper misreports.
- The client falls back to the plain POST **only** on 404/405 — a status that proves nothing ran.
  A mid-stream failure is surfaced, not retried, because a retry pays for the same comic twice.
- App Service's proxy may still buffer the whole response despite `X-Accel-Buffering: no`. That
  degrades to a correct comic with useless progress, which is why the fallback is not wired to it.

### Client-side comic history

`ComicHistoryService` keeps the user's seen-comics list in `localStorage` under
`posee_comic_history`, rendered at `/my-comics`. Saved history was a v1 non-goal when the
alternative was accounts and a server store; it is not — comics are addressed by place id, so a
list of ids reconstructs the feature with no backend and nothing leaving the device. Because
comics expire in 24h, an aged entry becomes a one-tap prompt to regenerate a place the user
already showed interest in. Every method degrades to a no-op: `localStorage` throws in private
modes and blocked-cookie configurations, and a history list is never worth taking the page down
for. The type is registered in `AppJsonContext` like every wire DTO — the client is
trim-analyzed, so reflection-based serialization fails the build.

`/my-comics` is linked from the **right-hand session zone**, not `nav.nav-links`: it is per-user
state, and `HeaderContractUiTests` asserts the primary nav is exactly two items.

### Design system and CSS architecture

`wwwroot/css/app.css` is the whole design system. **Read its header comment before adding CSS.**

**Cascade layers.** Order is `reset, vendor, tokens, base, page, shared, utilities`, and the order
is the point:

- `page` (Blazor's scoped-CSS bundle) sits **before** `shared`, so a scoped sheet can no longer
  silently fork a shared primitive like `.btn`. That was a real bug — Diagnostics and the Hall of
  Fame each grew their own `.btn-primary`.
- `base` holds bare **element** defaults (`a`, `code`, `body`) and sits **before** `page`, so pages
  can still override them. Getting this wrong is easy and one-directional: an `a { color }` rule in
  `shared` outranks even `.nav-item ::deep a` in `page`, because **layer order beats specificity**.
  It turned every nav label brand-purple on the dark bar. Element selectors → `base`; class-based
  components → `shared`.
- Radzen's stylesheet and the scoped bundle are pulled in with `@import ... layer()`, **not**
  `<link>`. Unlayered CSS outranks every layer regardless of source order, so a plain `<link>`
  would put vendor defaults above the entire design system.

**Boot splash.** `#app` centres `.loading-progress` with `min-height: 100dvh` + `place-content`,
guarded by `:has(.loading-progress)` so the rule stops applying once Blazor mounts. The splash
previously pushed itself down with `margin: 20dvh`, so `body` snapped from y=20dvh to y=0 on the
swap — a measured **CLS of 0.20 (mobile) / 0.14 (desktop) on every full page load**, and login and
logout both navigate with `forceLoad`. Do not reintroduce a top offset on the splash; centre it.

**Tokens.** Colour uses a **surface/ink split**: `--color-accent` is a background (bright amber),
`--color-accent-ink` is the readable text version. The fix for a failing colour is to pair it, not
to darken it — `--color-on-accent` on `--color-accent` measures 7.64:1. Two border weights:
`--color-border` (decorative) and `--color-border-strong` (real control boundaries, ≥3:1).
`--surface-inverse` / `--color-on-dark-*` exist because the nav bar and hero are dark in *both*
themes — `color: white` there was never a bug, just unnamed. Translucent brand tints use
`color-mix()` against `--color-brand` so they follow the theme instead of freezing light-mode purple.

Scales: 8-step spacing (`--space-*`), 7-step fluid type (`--text-*`), `--tap-target` (44px), and
exactly four breakpoints (`40/48/64/80rem`) — there were 13 before. Prefer a **container query**
(`.cq-card`, `.cq-list`) over a media query when the question is how much room a *component* has.

**Motion** is a scale too: `--duration-instant/fast/base/slow/deliberate` and
`--ease-out/in/in-out/spring`. Duration tracks distance travelled, not importance — a 4px chip
needs less time than a full-screen sheet or it looks sluggish. The easings are not
interchangeable: `--ease-out` for things arriving, `--ease-in` for things leaving (it starts slow,
which on an entrance reads as hesitation), `--ease-spring` for a *single* emphasised element —
overshoot applied to a list reads as instability. Reduced motion zeroes the durations at `:root`
rather than hunting individual transitions, so end states are unchanged.

**Elevation is a PAIR**, `--elevation-N-surface` + `--elevation-N-shadow`, N in 1..4 (resting card
/ raised / popover / modal). Reaching past them for a bare `--shadow-md` is correct in light mode
and invisible in dark, where a black shadow on a near-black ground conveys no height — dark mode
signals elevation by the surface getting *lighter* as it rises, the opposite of light mode. Use
`.elevation-N`, and `.elevation-raise` for hover (pointer-only: on touch there is no hover to
leave, so the card sticks raised after a tap).

> Watch the two dark blocks. `:root[data-theme="dark"]` did not redefine `--shadow-xs..lg` at all,
> so an explicit dark choice kept light mode's 0.08–0.18 alphas; only `ThemeUiTests` sets
> `data-theme`, which is why nobody saw it. Redefine a token in **both** the media query and the
> attribute selector, every time.

[ColorContrastTests.cs](tests/PoSeeReview.Unit/Utilities/ColorContrastTests.cs) parses the real
token values out of app.css and asserts WCAG ratios. It reads the stylesheet rather than restating
the hex codes on purpose — and it immediately caught a dark-mode border at 1.88:1 that hand
calculation had missed.

It asserts every text token against **every surface token**, not just `--color-card`. Checking
only the white card is what let `--color-text-muted` ship at 4.26:1 on `--color-brand-surface`,
where the Hall of Fame timestamp renders — axe caught in the browser what the test could not.
Widening it exposed the same bug in `--color-accent-ink`, `--color-success-ink` and
`--color-danger`, all of which had been tuned against pure white alone. **Tune a text token
against `--color-surface-alt` (light) and `--color-highlight` (dark)** — those are the worst
cases, not the card.

**Component library: Radzen, not Fluent.** FluentUI was removed. Radzen is themed by mapping
`--rz-*` onto the design tokens in app.css; that is the only reason its controls are on-brand.
Two traps: Radzen resolves `TextProperty`/`ValueProperty` by reflection over **properties**, so
binding a `ValueTuple` throws at render time, and scoped CSS cannot reach a **child component's**
markup — `RadzenTextBox` renders its own `<input>`, so those rules need `::deep`.

Bootstrap was deleted (31 KB gz for six classes app.css already redefined).

### Graphics and audio layer

Lives in [src/PoSeeReview.Client/wwwroot/js/](src/PoSeeReview.Client/wwwroot/js/), fronted by
`FxService` on the .NET side. `fx.js` is the only entry point index.html loads; it publishes
`window.poseeFx` to match the existing `window.geolocation` / `window.shareUtils` convention.

**`gfx-core.js` is the thing to understand first.** Everything else registers with it:

- **One `requestAnimationFrame` loop** for every effect. Never start a private rAF — N loops
  means N wake-ups per frame and no single place that can measure or stop the work.
- **A 20ms frame budget with automatic downgrade.** 1500ms of sustained overrun steps the tier
  down. The threshold is **milliseconds, not frames** — it used to be `90 frames`, commented as
  "~1.5s", which holds only at 60 FPS. At the 60ms frames that actually trip it, 90 frames is
  5.4s, so the guard fired slowest exactly when the device most needed it; measured live it had
  still not fired after 76 consecutive over-budget frames. The downgrade is deliberately *not*
  persisted — one heavy page shouldn't become a permanent setting.
- **Tiers** `off` / `lite` / `full`, stamped onto `<html data-fx-tier>` so CSS can respond.
  `off` is forced by `prefers-reduced-motion` or missing WebGL2 and cannot be overridden;
  Save-Data, low `deviceMemory`, or few cores default to `lite`.

Effect modules: `audio.js` (zero-asset Web Audio synthesis), `gradient.js`, `comic-fx.js`,
`particles.js`, `loading-ring.js`, `scroll-guard.js`, `shelf.js`.

**`gl-pool.js` owns every WebGL2 context.** Effects no longer call `canvas.getContext('webgl2')`;
they call `createSurface()` and get a *surface* — a band of one shared offscreen atlas plus its
own FBO — then `beginFrame()` / draw / `present()`. Four simultaneous effects on the comic page
used to mean four live contexts, each with its own GL state and its own share of a hard browser
cap that evicts the oldest **silently**. It is now one. `direct: true`, or any browser without
`OffscreenCanvas`, falls back to a private context — pooling is an optimisation and fails closed.

> **`present()` must not flip.** The blit into the atlas is a straight copy. A 2D context reading
> a WebGL canvas already sees it flipped for display, so "cancelling drawImage's flip" inverts
> every effect. This was shipped and invisible on the radially symmetric effects (noise gradient,
> loading ring) until the shelf gave the scene a top and a bottom. The band's `originY` is in GL
> coordinates and `drawImage`'s source Y is in image coordinates — converting between them is why
> the source Y is `atlasHeight - (originY + height)` and not `originY`.

**`telemetry.js` answers "why", where the frame budget only answers "whether".** GPU time
(`EXT_disjoint_timer_query_webgl2`), JS heap, long tasks, worst interaction latency, CLS, and the
live context count, merged into `gfx.stats()`. Two rules: an unavailable metric reports **null,
never zero**, and the GPU query tracks `active` (begun, not ended) separately from `pending`
(ended, result not back) — conflating them calls `endQuery` twice on any frame whose result was
not ready, which WebGL rejects every frame.

**`perf-hud.js` is the instrument that matters**, because `/diagnostics` is never the page that is
slow. It draws from inside the shared rAF loop — registered as an ordinary task, so its own cost
lands in the budget it reports — and repaints at 10Hz while sampling every frame. Toggle with
`Ctrl+Shift+F`, `?fx=debug`, or `poseeFx.togglePerfHud()`.

**Audio is spatial.** Every voice runs through a `StereoPannerNode` into a dry bus and a shared
convolver reverb whose impulse response is generated at runtime (decorrelated stereo noise plus
two early reflections — no asset). The score count-up sweeps left to right, pipeline phases pan
across the stepper, the resolution chord is spread, and `playTapAt(clientX)` pans a click to where
it happened. Errors stay dry and centred on purpose. An `AnalyserNode` on the master feeds
`audio-reactive.js`, which is a **separate module** so the coupling points one way: the gradient
exposes a setter and knows nothing about audio, and if the driver never runs nothing notices.

**The two heavy scenes were removed, and one came back on different terms.** `hall-shelf.js`
(Three.js) and `grid-physics.js` (Rapier) cost ~2.4 MB of vendored library for decoration layered
over a DOM list and a card grid that already worked. Gone with them: `wwwroot/lib/three`,
`wwwroot/lib/rapier`, the `startHallShelf`/`startGridPhysics` interop, and their lazy-import
assertions. `wwwroot/lib/bootstrap` went too — 228 KB nothing had referenced since Bootstrap was
dropped.

`shelf.js` is the replacement, and the conditions are the point: **no library** (hand-rolled
WebGL2 and 4x4 matrix maths, procedurally generated meshes, no model file), **lazy** (dynamic
`import()` on one route — `SCRIPTS/fx-perf-check.mjs` asserts it is absent on first load and
present after `/leaderboard`), **`full` tier only**, and **the DOM list stays** underneath,
`aria-hidden` + `pointer-events:none`. Two things about it are experience, not taste:

- **`fanSlot()` puts #1 at the centre.** Rank order along the arc puts the winner at the far end,
  which is the smallest and furthest position in the frame — backwards for a leaderboard.
- **The plank exists so the shadows have somewhere to land**, and the key light is above and
  *behind*. A near-overhead light drops each shadow into the card's own footprint, where the
  shadow pass costs full price and shows nothing. The plank is also mid-tone rather than
  near-black: a shadow is a contrast, and there is none available below the ambient floor.

Rules that are load-bearing, not stylistic:

- **Decoration never replaces the real element.** The comic `<img>` stays in the DOM under the
  post-process canvas (or long-press-save and right-click-save break on the app's most shareable
  artifact); the leaderboard DOM list stays under the 3D shelf (or it becomes unreachable by
  keyboard and invisible to screen readers); the SVG loading ring stays under the shader ring.
  Every overlay canvas is `aria-hidden` + `pointer-events:none` and only becomes visible once its
  shader confirms it started.
- **Nothing in `fx.js` may throw into .NET.** A graphics failure surfacing through interop shows
  the framework's red error strip over a working page. `FxService` swallows `JSException` and
  returns a benign default; a `0` handle means "not running".
- Audio defaults to **off** and needs a real user gesture to unlock — `AudioContext` created
  outside a trusted event stays `suspended` forever. Unlock is hung off existing button handlers.
- `FxService.SafeAsync<T>` carries a `[DynamicallyAccessedMembers]` annotation. It is required:
  `InvokeAsync<TValue>` deserializes reflectively, and without it the client fails `IL2091` under
  `EnableTrimAnalyzer` + `TreatWarningsAsErrors`.

```powershell
# Both need playwright resolvable from the script's own directory (the repo has no node_modules),
# same as SCRIPTS/post-deploy-smoke.mjs — CI does `npm install --no-save playwright` first.
$env:BASE_URL = "https://localhost:5001"
node SCRIPTS/fx-perf-check.mjs   # frame budget + lazy-load assertions
node SCRIPTS/ui-check.mjs        # cascade layers, tokens, mobile overflow, comic pipeline
```

`ui-check.mjs` performs ONE real comic generation, which spends a paid image call. Its overflow
check walks `/`, `/leaderboard` and `/diagnostics` at 320px and 390px — it used to measure only
whichever page happened to be loaded, which is how `/diagnostics` shipped a 628px-wide document
inside a 390px viewport.

Measured under forced software rendering (no GPU in headless Chromium), with the 3D shelf and
Rapier grid now removed: the gradient alone still runs ~17-19 FPS at 53-60ms per frame, so the
`full` tier auto-downgrades to `lite` within 1.5s, as designed. Treat these numbers as a
software-rendering floor, not a device measurement — a real GPU is far faster.

### Spend control, reactions, reports and the funnel

Four capabilities were added on top of the original slices. Each exists because of a specific
gap, and the reasons matter more than the mechanics.

**Daily generation budget (`Features/Comics/GenerationBudget*`).** The `comics-post` limiter caps
bursts at 3/min *by IP*, which bounds nothing over a day — a rotating mobile IP or a modest spike
could run the paid image model indefinitely. `IGenerationBudgetService` charges a UTC-day counter
per principal and an app-wide one, both in `PoSeeReviewBudget`. Notes:

- The reservation happens **before** the pipeline and is **refunded** on a cache hit, so it counts
  paid generations rather than requests. Failures refund only when they provably preceded the
  image call (`restaurant_not_found`, `insufficient_reviews`, `insufficient_strangeness`); a
  generic 500 does not, because it can be thrown after the spend.
- The service ceiling is checked first, so a user is not charged their own quota for a request the
  app was going to refuse anyway. If the per-user check then fails, the service unit is returned.
- The counter **fails open** after `MaxConcurrencyRetries` — a contended ETag must not become an
  outage on the app's primary action.
- `GET /api/comics/budget` is free, so the client greys out the generate button *before* a tap
  rather than after a 429.

**`GET /api/comics/{placeId}/image`** re-serves the comic from this origin. The `download`
attribute is ignored on a cross-origin href and the storage account sends no CORS headers, so a
blob URL can only be opened in a tab, never saved. It is deliberately **not** used for display —
that would move every view onto the app's bandwidth for no user-visible gain.

**Reports (`Features/Reports`)** are the public moderation intake. `POST /api/takedowns` is not
that path: it carries a shared admin key and deletes the comic, blob and leaderboard row on the
spot. `/api/reports` requires a session, is rate limited, dedupes by reporter (the RowKey *is* the
principal, so the 409 is the duplicate check), and only ever writes a row.

**Weekly Hall of Fame (`Features/Leaderboard/HallOfFame*`).** Comics expire in 24h and the live
board churns with them, so nothing accumulated and there was no reason to return. Entries are
promoted as scores are recorded and outlive the comic — which is why `ImageExpired` exists, and
why a takedown must purge the archive too (it is the copy that survives everything else).

**Funnel analytics (`Features/Analytics`).** The PRD sets targets the app never measured; the
server only tracked `ComicGenerated`, which cannot see a denied location or an abandoned
generation. The client reports steps from a **closed vocabulary** (`FunnelSteps`) that the server
enforces — an open one would let a client bug mint unbounded telemetry dimensions, which is a
billing problem. Rendered on `/diagnostics`.

> A rate whose denominator is zero is reported as `null`, not `0` — an absent rate is honest.
> `TapThroughRate` divides *started generations* by taps, both tapped-flow-only events. Dividing
> all delivered comics by taps reported **200%**, because a comic opened from a shared link or the
> Hall of Fame has no tap in front of it.

Five tables were added and are created by `TableStorageInitializer` alongside the originals:
`PoSeeReviewReports`, `PoSeeReviewReactions`, `PoSeeReviewHallOfFame`, `PoSeeReviewBudget`,
`PoSeeReviewAnalytics`. The initializer now creates them concurrently — eight serial round trips
were all on the startup critical path.

### PWA

`manifest.webmanifest`, `service-worker.js`, `offline.html`, generated icons under `wwwroot/icons/`
and `pwa.js` (published as `window.poseeFx`-style `window.poseePwa`).

**The worker is network-first, and that is not a preference.** Blazor verifies every framework file
against the integrity hashes in `blazor.boot.json`, and those files are not fingerprinted by name.
A cache-first worker serving yesterday's `_framework/*.wasm` against today's boot manifest produces
an integrity failure and a white screen the user cannot clear without wiping site data. `/api`,
`/auth`, `/diag` and `/health` are never cached; cross-origin (comic blobs) passes straight through.

`pwa.js` must load **before** Blazor: `beforeinstallprompt` fires early and is only capturable if
its default is prevented the moment it arrives. The install nudge renders on the comic page rather
than the landing page — it asks once the app has shown why it is worth keeping. iOS Safari can
install but exposes no prompt API, so it gets instructions instead of a button.

### Link previews

`SocialPreviewMiddleware` (in the Comics slice, registered from `Program.cs`) answers link-preview
crawlers on `/comic/{placeId}` with a real Open Graph document; everyone else falls through to the
SPA. It is middleware rather than an endpoint on purpose — a mapped endpoint would out-rank
`MapFallbackToFile` and would then have to reproduce how static web assets resolve `index.html` in
Development. It runs before `UseRateLimiter` and before authentication, since a 429 or 401 on a
preview fetch unfurls as the same blank card as no tags at all.

`UserAgentValidationMiddleware` lets `SocialCrawlers` through **only on non-`/api` paths** — the
API is where the paid AI calls and the Maps quota live. Everything interpolated into the preview
HTML is `HtmlEncode`d: the restaurant name and narrative are third-party review text, and this is
the one place in the app that emits raw HTML.

### Endpoints worth knowing

`/health` (+ `/live`, `/ready`), and `/diag` — masked keys plus integration statuses, active in Dev
**and** Prod; `/diag/mock-status` reports active `IMockable` registrations and drives the client's
"USING MOCK DATA" banner. `/diagnostics` has no nav entry; reach it by URL. It was briefly added to the primary nav, which
both put ops tooling (machine name, .NET version, masked config) into a consumer app's main
navigation and broke `HeaderContractUiTests`, which asserts exactly two nav items. Note `/diag` sits behind
`UserAgentValidationMiddleware`, so anonymous scripted fetches get a 400 — that is why the smoke
script no longer asserts on it.

## Conventions

- Minimal APIs only, no controllers: one `MapGroup()` extension per slice, registered via
  `MapFeatureEndpoints()`.
- `Testcontainers.Azurite` is pinned at 4.14.0 specifically to clear `NU1903` on the `SSH.NET`
  2025.1.0 it used to drag in transitively. Do not downgrade it: with `TreatWarningsAsErrors`
  that advisory breaks the whole solution build, not just the test projects.
- C# 14 density — primary constructors, collection expressions, pattern matching; minimal comments.
- Client + Shared are `EnableTrimAnalyzer`; JSON is source-generated — **add every new DTO to
  `AppJsonContext`**. The Client deliberately does *not* set `IsTrimmable` (Router/LayoutView use
  reflection and member-level trimming kills them at runtime, `CtorNotLocated`). No AOT.
- NuGet audit warnings (NU1901/2/3) are never suppressed; with `TreatWarningsAsErrors` a new
  advisory breaks the build. `Microsoft.OpenApi` carries a direct pin to override the vulnerable
  2.0.0 that `Microsoft.AspNetCore.OpenApi` drags in.
- UI: no inline styles. Scoped `.razor.css` + design tokens in `wwwroot/css/app.css`. Themes follow
  the OS via `@media (prefers-color-scheme: dark)`, with `:root[data-theme="dark"|"light"]`
  overrides that no UI currently sets (only `ThemeUiTests` exercises them via JS).
  **Never hardcode a colour in scoped CSS** — a literal `white` under token-driven text renders
  white-on-white in dark mode, and the theme tests assert token *values*, not rendered contrast.
- Shared `.btn`/`.btn-primary`/`.btn-secondary`/`.alert*`/`.chip-toggle`/`.toast` primitives belong
  in `app.css`, not in scoped page CSS. `.chip-toggle` (discovery sort + Hall of Fame scope) and
  `.toast` (comic actions + Hall of Fame share) are shared for exactly the reason this file already
  documents: two pages needing the same control is how `.btn-primary` forked last time. Scoped sheets load after `app.css` and carry a `[b-*]` attribute, so a page-level
  redefinition silently wins — that is how Diagnostics and the Hall of Fame drifted apart.

## Working rules (NET_AGENTS)

These govern how the agent operates in this repo, not how the code is written.

- **`master` only.** Do all work on `master`. Use another branch only when explicitly asked to.
- **Restart and verify after every code change.** Stop the app, start it again
  (`dotnet run --project src/PoSeeReview.Api --launch-profile https`, or the
  `start-api-clean` VS Code task), and confirm it actually came up before reporting done.
  Config/appsettings/Key Vault changes need a full restart — `dotnet watch` will not pick them up.
- **Read [docs/](docs/) first** for the project overview, when it exists. The generated reports
  were cleared out and are due to be rebuilt; until then this file and [README.md](README.md) are
  the authoritative overview, so do not assume `docs/index.html` is there to read.
- **No `dotnet user-secrets`.** Non-secret config goes in `appsettings*.json`; real secrets go in
  Key Vault `kv-poshared` under the `PoSeeReview--` prefix. The one existing exception is
  `Takedowns:ApiKey` for local dev — it is a live credential, so it must never land in an
  appsettings file that is committed.
- **Never push to remote unless asked.** Committing locally is fine; `git push` is not, until the
  user says so.
- **On "git sync": commit and push.** Short American-slang message that reads like a human wrote it
  ("fixed the busted nav", "cleaned up that css mess"), then push.
- **TL;DR any answer over 100 words** with a ~20-word summary at the end.
