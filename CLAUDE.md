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
stop the running API → start Azurite → start the API.

**Never kill every `dotnet` process.** The Bicep, C#/Roslyn, MSSQL and Unity language servers are
all framework-dependent .NET apps launched through a `dotnet` host, so a blanket
`Get-Process dotnet | Stop-Process` takes the editor's tooling down with the app — it crashed the
Bicep server five times in three minutes before VS Code gave up restarting it. That old task was
useless anyway: the app runs as `PoSeeReview.Api.exe`, not `dotnet`, so it left the DLL lock that
breaks the next `dotnet build` (MSB3021/MSB3027) exactly where it was. `kill-api-processes` stops
`PoSeeReview.Api` and only those `dotnet` hosts whose command line names this project.

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

Azurite blob URLs carry the account as their first path segment
(`/devstoreaccount1/comics/{blob}`); Azure's are `/comics/{blob}`. `BlobStorageService.ResolveBlobClient`
anchors on the container segment rather than skipping a fixed one — skipping one resolved every
local URL to `comics/{blob}` inside the comics container, so `/api/comics/{id}/image`, existence
checks and blob deletes all 404'd against Azurite while the JSON said the comic was there.

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
                            Insights, Reports, Reactions, Collections, Moderation)
                           Eleven, not the fourteen this list used to name. ShareLinks moved into
                           Comics (a short link only ever addresses a comic, and the link-preview
                           card was already there); Analytics moved into Diagnostics (its only
                           reader is `/diagnostics`); Takedowns moved into Moderation — the same
                           erasure reached through a different gate, and the same obligation to
                           suppress first. Routes did not move: `/api/share`, `/s/{code}`,
                           `/api/analytics` and `/api/takedowns` all still answer where they did.
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
`IHallOfFameArchive`, `IKeptComicArchive`, `IContentModerationGate`, `IContentSafetyScreener`).
`IHallOfFameArchive` exists for exactly the reason `ILeaderboardRepository` does: Takedowns must
erase archived entries without referencing the Leaderboard slice that owns them. Only the delete
is exposed there — reads stay in the slice. `IKeptComicArchive` is the same shape for kept
comics, and for the same reason: they are the *other* thing built to outlive expiry.
`IContentModerationGate` is read by Comics, written by Reports and Takedowns, and owned by
Moderation; `IContentSafetyScreener` is called by Comics and implemented by Moderation.

Two slices read another slice's **table** rather than its repository — Insights over
`PoSeeReviewHallOfFame`, Moderation over `PoSeeReviewReports`. Each declares its own read-only
row projection (`ArchivedScoreRow`, `ModerationReportRow`). Table Storage is schemaless per row,
so a POCO with a subset of the columns reads the same rows without owning them, and no slice
reference is created. Note what Moderation deliberately does *not* project: the reporter's
`Details` and `ContactEmail`.
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
- The client request must call `SetBrowserResponseStreamingEnabled(true)`. `ResponseHeadersRead`
  alone is not enough on WASM: the browser `HttpClient` buffers the whole body, so every phase
  arrived in one burst after `complete` and the stepper never moved. The proxy was blamed for
  what the client was doing.

### The AI pipeline: one call, one flight, one price

**Chat is not the image provider, and the two are now chosen separately.** `Ai:ImageProvider`
still picks the painter (`Gemini` → `GeminiComicService`, `HuggingFace` → `FLUX`), and
`Ai:ChatProvider` picks the scorer (`AzureOpenAI` | `HuggingFace` | `Ollama`). A missing
`Ai:ChatProvider` derives the old pairing, so nothing shifted under an untouched deployment.
They were one setting, and that made every image-model experiment a scorer experiment too —
there was no way to tell "this comic is worse" from "this scorer is stricter". `Ollama`
(`OllamaChatService`, `http://localhost:11434/v1`) is the local tier: zero marginal cost, no
key, and the only way to exercise the whole pipeline without a bill. `AiPricing:FreeProviders`
lists the providers with no token price so a local call does not report a fictional dollar
figure. **Inference stays on the server** — a client-side scorer would need the review text in
the browser, would produce a score only that device agrees with, and `webgpu-pool.js` is scoped
to effects where compute changes what the effect *can be*.

**There is one chat call, not two.** Captions used to come from a second completion issued after
the image existed, though it shared no input with the image call it waited behind — it needs
only the narrative, which the first call produced. `IChatCompletionService` has one method and
`ChatPrompts.BuildAnalysisPrompt` asks for `captions` alongside `narrative`. The neat part is
what that removes: `ComicTextOverlayService` no longer holds a chat service at all, so the
drawing step is text-in/pixels-out and the model coupling lives in one place.
`ChatPrompts.NormalizeCaptions` makes the result total — a language model asked to count panels
gets it wrong often enough to matter, and topping up from the narrative beats paying a second
call to improve a subtitle.

**Single flight.** `ComicGenerationLock` is a per-place `SemaphoreSlim`; the pipeline takes it
after the first cache miss and re-reads the cache under it. Without it a double-tap or two tabs
both miss and both pay, and the second comic is discarded by the upsert. It is deliberately
**in-process** — a distributed lease would add an ETag protocol and a failure mode where a
crashed instance blocks a restaurant.

**`Comics:PromptVersion` is part of the cache key.** The cache was keyed by place and age, so a
prompt tune kept serving the previous output until somebody paid for a forced regeneration,
which made evaluating a prompt change cost one generation per restaurant tested. A row whose
version differs misses; rows written before versioning read back as 0 and miss once. No
migration, no backfill job.

**Cost is now a number worth reading.** `AiCostTracker` emits `Ai.Cost.Usd` and `Ai.Tokens`
tagged `Provider`/`Model`, priced from `AiPricing:Models` per **input and output** rate. It
replaced a single hardcoded blended `$0.10/1K` over total tokens, which moved with the
prompt/completion mix rather than with the price and so could not answer the one question a cost
metric is asked.

**A token cap on a reasoning model does not reach the wire, and startup says so.** Reasoning
deployments reject `max_tokens` (the 2026-06-15 outage) and require `max_completion_tokens`,
which OpenAI SDK 2.1.0 cannot express — `ChatCompletionOptions` has no additional-properties bag.
`ChatTokenBudget.CanApplyCap` is what lets `StartupSecretValidator` warn instead of leaving a
spend ceiling sitting in appsettings looking active. On a reasoning model the cap would be the
wrong instrument anyway: reasoning tokens are billed against the same allowance.

**An image refusal is a refusal, not a retry.** Gemini's `SAFETY`/`PROHIBITED_CONTENT` used to be
answered by redrawing with a fixed "happy restaurant" prompt and publishing *that* under the
reviews' score, with no marker that the subject had been swapped — the app spending money to
lie. `ImageDeclinedException` now surfaces as a 422 and flags the place for moderation, because
a refusal is a real signal about the source material. `SanitizeNarrative` still blunts the terms
the image filter rejects (the alternative is refusing the one-star reviews the product mines)
but now reports which ones via `Gemini.Image.PromptTermsBlunted` — a rewrite should be visible.

**Embeddings, and the `/comics/{placeId}/similar` row.** `IEmbeddingService` (`Embedding:*`,
**opt-in and off by default**) vectors the narrative onto the comic row; `VectorMath` packs it
little-endian into an `Edm.Binary` column and ranks by cosine. This is the app's first "and what
else?" — a comic page was a dead end. Two rules: the service **never throws** (a lost vector
costs one related link, never a comic), and only *live* comics are candidates, because a similar
comic is something to open and an expired one is a dead link wearing a recommendation. Comics
drawn before the feature was switched on have no vector and are **not** backfilled; the rows
expire within a day, so the feature fills itself in rather than embedding on a GET.

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

`/my-comics` has **no nav entry**: not in `nav.nav-links`, which `HeaderContractUiTests` pins at
exactly two items, and no longer in the right-hand session zone either. It is reached by URL,
the way `/diagnostics` and `/moderation` are.

`BoardMemoryService` is the same pattern for a different question — where each place sat on the
leaderboard last visit, keyed per region under `posee_board_ranks_<region>`. Both are registered in
`AppJsonContext` (`List<ComicHistoryEntry>`, `Dictionary<string, int>`) for the reason every wire
DTO is: the client is trim-analyzed, so reflection-based serialization fails the build.

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
`particles.js`, `loading-ring.js`, `scroll-guard.js`, `shelf.js`, `physics.js`, `comic-reveal.js`,
`haptics.js`, `ambient.js` (+ `posee-synth-processor.js`), `webgpu-pool.js`, `particles-gpu.js`,
`glsl-backdrop.js`, `glass.js`, `comic-tint.js`, `ink-field.js`, `panel-scrub.js`, `paper.js`.

> **The backdrop was invisible, and that is worth knowing before touching it.** `.fx-backdrop` is
> `position: fixed; z-index: -1`, and `.page` painted an opaque `--color-brand-surface` straight
> over it — so the strangeness-reactive scene, the thing the score retunes, was covered on every
> route. `.page` now stands aside only while `:root[data-fx-backdrop="on"]`, a flag `gradient.js`
> sets when a backdrop is genuinely drawing and clears when it stops. Every path where the shader
> does not run leaves the CSS background exactly where it was, so the page is never bare. The
> shader's field is built from `--color-surface` / `--color-brand-surface` / `--color-brand` (not
> the three hardcoded dark purples it used to carry) so handing the ground over does not change
> which surface the text tokens are measured against; `uAmbient` is 0.94 in light and 0.62 in
> dark, because the same floor cannot serve both.

**`fx.js` is the composition root, and that is load-bearing.** `audio.js` does not know haptics
exist; `haptics.js` does not know about the bed; `gradient.js` does not know about the analyser.
Deciding that a tap should also buzz, or that enabling sound should also start a drone, is a
product decision and it is made in exactly one file — the same reason `audio-reactive.js` is a
separate module rather than a branch inside `audio.js`. `propagateAudioState()` is the single
place that fans a sound-preference change out to the visual driver, haptics and the bed; missing
any one of the three produces a muted app that still pulses, buzzes, or drones.

**There is no sound switch in the header, and sound now defaults to on.** The switch was put in
the session zone because audio defaulted to off and can only be unlocked from a trusted event, so
the *only* control was on `/diagnostics` — a page with no nav entry, reached by typing a URL. That
made the default the problem, not the placement: flipping the default fixes the reachability, and
then the control earns nothing. Removing it also returns width, because `.nav-links` is `flex: 1;
min-width: 0` against a session zone that cannot shrink, so anything parked on the right comes
straight out of the primary nav — the mobile 30px-circle-with-a-`::after`-hit-box treatment
(including the header contract test that caught a full-size button squeezing the nav to zero
width) went with the button.

Sound, haptics and the ambient bed all default to on (`audio.js` reads an explicit `posee_audio_enabled`
of only `'false'` as off), and all three keep their switches on `/diagnostics`. Nothing is heard
before a gesture regardless — an AudioContext cannot start outside a trusted event, so the first
cue lands on the first tap. An explicit reduced-motion request still forces silence.

### Refractive glass, and the one reason it is possible

`.glass` in app.css is `backdrop-filter: blur()`. That is frosting, not glass: real glass **bends**
what is behind it, splits the colour where the bend is steepest, and catches a highlight that
moves. `glass.js` does all three, on a pooled surface, `full` tier only.

**It works only because the backdrop is procedural.** No browser exposes composited DOM to a
shader, so a pane cannot read what is behind it — but `gradient.js` is a pure function of position
and time, so a pane can *evaluate* it, at any coordinate the refraction asks for, including
outside its own bounds. Both passes share `glsl-backdrop.js` precisely so they cannot disagree
about what that coordinate contains. This is why glass over the **comic** is not on the table:
that blob is cross-origin and taints a texture upload, the same wall `comic-fx.js` hits.

Three things here were shipped wrong once and are easy to reintroduce:

- **The pane and the backdrop are a pair**, enforced in `fx.js`. A pane running while
  `gradient.activeIds()` is empty refracts a scene that is not on screen — observed exactly once,
  when the budget watchdog downgraded the backdrop while a pane started. `gradient.onActiveChanged`
  is the other half, for a gradient stopping on a lost context rather than a tier change.
- **The edge field is a superellipse, not a rounded-box SDF.** The box SDF's gradient is
  axis-aligned inside the box and flips along the diagonals, so a normal taken from it draws a
  hard **X** across the pane. That shipped.
- **`paneY` is `1 - rect.bottom / vh`, not `rect.top / vh`.** `vUv.y = 1` is the top of the frame;
  `getBoundingClientRect().top` measures down. Getting it wrong mirrors the sampled scene, which
  still looks like a material — which is why it survives a glance. Same class of mistake as the
  `present()` flip in `gl-pool.js`.

Mount it as `<GlassPane />`, the **first child** of any `.glass` element: the JS takes the canvas's
parent as the pane, so there is no second `ElementReference` and no id plumbing. It flags that
parent `data-glass-pane="on"` **after** a successful compile, and app.css stands the CSS blur down
only on that flag — so every failure path keeps the material that shipped before.

### The page wears the comic's colours

`ComicPaletteExtractor` (server) samples three colours off the finished image bytes and ships them
on `ComicDto.Palette`; `ComicEntity.PaletteHex` persists them in one column, absent on older rows.
**It has to run on the server**: the blob is served without CORS headers, so a browser canvas that
has drawn it cannot be read back. Not a quantizer — population alone returns three browns for
every comic, because paper and gutters dominate — so buckets are ranked by population **weighted
by chroma** and the survivors must differ in hue.

The palette is **blended into** the theme's base, never substituted for it: lightness stays the
theme's, only hue travels. An image model's idea of a mid-tone is not a surface `ColorContrastTests`
has ever measured. For the same reason `comic-tint.js` publishes `--comic-tint-1..3` for **accents
only** — ring stroke, card rim, chip edge — and never a text colour or a text background.
`MainLayout.RaisePaletteChanged([])` is the clear, and a comic route **must** raise it on teardown
or the leaderboard wears the last comic's colours.

### WebGPU, and why it is narrow

`webgpu-pool.js` owns one shared `GPUDevice` on the same terms `gl-pool.js` owns one WebGL2
context. It is used by **two** effects, and the bar for a third is the same one both cleared: not
"would this be faster in WGSL" but "does compute change what the effect can be". WebGPU is not a
faster WebGL; what it has that WebGL2 does not is **compute**. `particles.js` simulates
every particle in the vertex shader from immutable seeds — which is why 1500 cost the same as 20,
and also why no particle can know about the floor or about any other particle. `particles-gpu.js`
writes state back to a storage buffer, so the drops decelerate, hit the bottom of the panel and
**settle**. The visible difference is the ending: the WebGL2 burst fades out mid-air because a
stateless sim has no other option.

`ink-field.js` is the second, and the argument has the same shape. The CSS reveal mask and the
shader's noise threshold are both functions of **position** — the boundary looks the way it does
because of where it is. Ink on paper is a function of **history**: it wicks along the grain, runs
ahead of itself where the sheet is thirsty, and pools at the front, because each cell reads what
its neighbours did last step. That is a 96x192 diffusion grid, ping-ponged between two storage
buffers (a compute pass cannot read and write one buffer coherently across workgroups), and it
cannot be expressed statelessly. It **covers** the comic in paper colour and eats the cover away
rather than masking it — `mask-image` takes a URL, not a live canvas, so a simulated boundary
cannot become a mask without a per-frame readback.

> The front is **driven, not simulated**: `comic-reveal.js` pushes its own eased progress in. A
> diffusion front left to find its own pace takes as long as it takes, and the comic would still
> be half covered when the reader started scrolling. The sim decides what the edge *looks like*;
> the reveal decides when it is over.

Everything else in the app is a fullscreen fragment pass where WebGL2 is entirely adequate and
already pooled; porting those would mean maintaining WGSL and GLSL for identical output. The
device is probed before the module is imported, so a device without WebGPU never fetches a byte
of it, and a null device means the WebGL2 path runs. Never read the fallback as degraded — it is
the effect that has always shipped.

> The particle state machine lives in the shader, not in JS, on purpose. Reading the storage
> buffer back to find out which drops have settled would stall on a `mapAsync` every frame — a
> round trip costing more than the whole simulation. The CPU never reads any of it; it only knows
> how long the burst has been running.

### Physics is back, on the shelf.js terms

Rapier was deleted because ~2.4 MB of WASM solver was jiggling a card grid that already worked.
That verdict was about the trade, not about physics. `physics.js` is a hand-rolled Verlet solver —
**no library**, **lazy** (dynamic `import()`, one route, on one event), **`full` tier only**, and
the real element stays underneath. It earns its place by doing the thing the shader sim cannot:
ink that *lands*, piles unevenly, and where it piles depends on where the last drop went.

- Verlet, not Euler: position and previous position **are** the velocity, so a collision is
  resolved by moving a body. Stacking is stable at three substeps, which is the entire behaviour
  these effects are built on.
- A uniform spatial hash rebuilt per substep, and bodies that **fall asleep** after a few quiet
  frames. Pooled ink is the steady state of both presets, so within about a second most of the
  work stops happening at all. Contact wakes a sleeping body — without that, a new drop falls
  straight through a settled pile, because the pile is skipped by integration *and* collision.
- 2D canvas, never `gl-pool`: it asks for no WebGL context, so it does not spend a slot in a pool
  capped at eight to save nothing. It still registers with the shared rAF loop, so its cost lands
  in the budget that can downgrade it.
- Three presets. `ink` follows the burst; `shatter` breaks the score ring apart and is gated at
  **90**, because a ring that shatters on every comic stops meaning anything; `pile` is the
  reaction surface over the comic.
- **`pile` is the one preset that is not a one-shot.** It starts empty, is fed by `emit()` as the
  user taps, recycles its oldest body at capacity (so a tap is never silently dropped), and has
  `holdMs = Infinity` — it is stopped by its component's teardown, never by ageing out, because
  the reactions are the user's own marks on the page. Forgetting to stop it leaks a frame task
  past the route. Its bodies carry a `glyph` and are drawn in a **second pass** with
  `source-over`: `multiply` would turn a heap of yellow faces brown, and flipping the composite
  mode per body is exactly the 2D state change this renderer avoids. Tumble is integrated from
  horizontal travel in the draw loop — Verlet has no angular term, and solving one for decoration
  would need an inertia tensor per body.
- **Contact must wake sleepers.** `emit()` clears `asleep` wholesale, because a new body falls
  straight through a settled pile otherwise — a sleeping body is skipped by integration *and*
  collision.

### Ink development: the comic arrives instead of appearing

A generation is ten seconds of stepper and then the finished strip pops into the layout in one
frame — the single frame carrying the payoff of the whole wait, spent on a layout change.
`comic-reveal.js` develops it top-down over ~1.4s instead.

It could not be driven by the SSE phases, and that is worth knowing before someone tries: **the
artwork does not exist until `Publishing`.** Every earlier phase is text and metadata, so there is
nothing to reveal while they run. The reveal fires on ARRIVAL; the stepper still narrates the wait.

Two layers, and the CSS one is the important one:

1. A `mask-image` on `.comic-strip-container`, driven by the registered `--comic-reveal` property
   this module writes each frame. Always runs above the `off` tier, needs no WebGL, and is
   therefore what nearly everyone sees — `comic-fx` only attaches when the blob happens to be
   CORS-readable, which in this deployment it usually is not.
2. The shader's own noise-threshold mask when `comic-fx` did attach, forwarded through
   `setReveal`. That adds the ragged wet-ink boundary a linear gradient cannot express.

The mask is on the **container** so it covers the `<img>` and the post-process canvas together;
masking one would develop the shader layer over an already-visible copy of the finished comic.
The composite pass is opaque during a reveal for the same reason. **Every exit path must clear
`data-comic-reveal`** — the mask only applies while the attribute is present, so abandoning a
running reveal leaves part of the comic permanently hidden. That is the one failure here a user
would actually notice, and it is why `ComicStrip.DetachAsync` finishes the reveal first and
unconditionally.

The **score shockwave** lives in the same composite pass: an expanding annulus that displaces the
lookup outward and splits the channels across the wavefront, fired from the top edge where the
score ring sits. A separate pass would refetch the scene buffer for something live for one second.
A negative age is the inactive sentinel, so the branch is cold the rest of the time.

### Sound beyond the cues

- **`haptics.js`** derives its patterns from the same attack/decay/peak envelopes `voice()` takes,
  so a cue is described once and both played and felt. `navigator.vibrate` has no amplitude — only
  duration and rhythm — so `fromEnvelope` maps a note's energy to a run length and its `delay` to
  a gap. Haptics **follow the sound preference and can be switched off on their own, never on on
  their own**: someone who muted the app did not ask to be buzzed instead. No-ops on iOS, which
  exposes no Vibration API at all.
- **The ambient bed is the one sound that needs an AudioWorklet.** Every other cue is a transient
  scheduled against `ctx.currentTime`, which is already sample-accurate. A *sustained* tone whose
  parameters are written from the main thread clicks whenever that thread is busy — and here it is
  running Blazor renders and the shared rAF loop. `posee-synth-processor.js` generates the whole
  bed per sample on the audio thread; the main thread only posts targets, which it smooths toward.
  It **ducks** under every foreground voice via `audio.onVoice`, fades over seconds, and has its
  own opt-out on top of the sound preference. The processor is loaded with
  `new URL('./posee-synth-processor.js', import.meta.url)` — a bare relative string resolves
  against the *route*, and on `/comic/{placeId}` the SPA fallback would answer with `index.html`
  at a 200, surfacing as a syntax error inside a worklet rather than a missing file.
- **Per-comic signature.** `audio.signature(placeId, score)` seeds a mulberry32 PRNG from an FNV-1a
  hash of the place id: the same restaurant always plays the same four-note figure. The *seed*
  picks the notes and contour; the *score* picks the scale, tempo and timbre, so two places sound
  different from each other and a strange one sounds strange rather than merely different. Seed
  with the place id, never the name — names collide across chains. The body lives in `motif()`,
  lifted out of `signature()` so the board can voice several at once without going through the
  throttle that exists to stop *one* of them retriggering on a re-render.
- **The board as a chord.** `audio.boardChord` plays the top three as one chord, each voice that
  restaurant's own motif — #1 centred (same reasoning as `shelf.js`'s `fanSlot`), the others out
  to the sides, staggered so it arrives as an arpeggio rather than mud, and shortened to three
  notes because twelve notes of arpeggio is a tune. Because a motif is deterministic, a board that
  has **changed** sounds different before a single row has been read. `/leaderboard` had a 3D
  shelf on it and not one sound.
- **Rank movement.** `BoardMemoryService` remembers where each place sat last visit, per region, in
  `localStorage` — on exactly the terms `ComicHistoryService` is justified. Compare *before*
  saving or the board is compared against itself and every row reads as unchanged. A first visit
  reports **nothing**: "we have no record" and "nothing moved" must look identical, because in
  both cases there is nothing to point at, and ten "new" badges would be noise pretending to be
  information. Positive is a climb, so the subtraction is `was - now` — ranks count the other way.
- **The discovery beats.** The landing page had two cues on it, one of them the audio unlock.
  `locating()` is a ping with a delayed echo — the *gap* is what says a request is outstanding,
  which a click cannot; `arrival(count)` maps the result count to figure LENGTH, so "a lot came
  back" is legible without being explained; `empty()` is deliberately not `error()`, because a
  search that found nothing is an answer, not a malfunction. `tapCached` / `tapUncached` voice the
  consequence of the tap rather than just its position.
- **Sonification** (`audio.sonify`) plays a numeric series as pitch, sweeping left to right, and
  drives the 🎧 control on each `/insights` chart. Long series are **decimated, not truncated**:
  the contour is the only thing being communicated. Whether a distribution is flat, humped or
  bimodal is instantly obvious as a shape, including to someone who cannot see the SVG.
- **Narration** (`speechSynthesis`) reads the narrative aloud. It is a separate output that never
  enters the AudioContext, so it works before unlock and is gated on the preference alone — and it
  must be cancelled explicitly on teardown, because the queue is browser-global and would follow
  the user to the next route. `cancel()` before every `speak()`: Chrome queues indefinitely.
- **The skit is narration's queuing "bug" turned into a feature.** `POST /api/comics/{placeId}/audio`
  asks the chat model to invent a short conversation between the strip's characters
  (`ChatPrompts.BuildSkitPrompt`, one call alongside the analysis contract
  `IChatCompletionService.GenerateSkitAsync`), serializes it onto the comic row as
  `AudioSkitJson`, and serves every later tap from that column — the chat call is paid once per
  comic. It rides the `comics-post` limiter, because the first tap spends tokens and a cache hit
  is indistinguishable before the read. The client does not chain utterances on `boundary`
  events: `speak()` enqueues, so the whole dialogue is queued in one pass and existing
  `stopNarration()` stops it — the same cancel that narration needs anyway. Speakers are
  differentiated by pitch slot in order of first appearance (deterministic per character), and
  the skit crosses interop as a **string** pre-serialized by `AppJsonContext`, because a complex
  type as a JS-interop argument is serialized reflectively — the exact thing the source
  generator exists to avoid on a trim-analyzed client.
- **Graded moderation cues.** Hide / suppress / remove sound different because they are one mis-tap
  apart and a moderator working a queue should hear which one landed. Restore gets the plain
  confirmation cue — it is the only action there that puts something back.

> **Login cannot have audio, and this is not an oversight.** Both login paths navigate with
> `forceLoad: true`, so the AudioContext dies with the page. Unlock happens on the first real
> gesture *after* load — the discovery flow, the comic action bar, the reaction bar, the header
> switch.

### Modern CSS that costs no frame budget

Everything above spends GPU inside a 20ms budget. These do not, because the compositor evaluates
them off the main thread — which is the whole reason to prefer them over an IntersectionObserver
and a class toggle, that being an observer, a callback, a style recalculation and a DOM write per
card to produce the same fade.

- `@property` registrations sit at the **top level**, not in a layer: they are global, unaffected
  by layer order, and an *unregistered* custom property is untyped, so `transition: --x` does
  nothing and keyframes over it snap. Each declares an `initial-value` that renders correctly
  before JS writes anything — `--comic-reveal` initialises to `1`, meaning "fully developed", so a
  reveal that never runs shows the whole comic.
- `.scroll-enter` / `.scroll-enter-late` use `animation-timeline: view()`. `both` is required or
  cards below the fold sit at their authored visible state and never animate. The stagger comes
  from each card's own position in the scrollport, so it survives filtering and sorting — an
  `nth-child` stagger does not.
- `@starting-style` + `transition-behavior: allow-discrete` give the toast, the report dialog and
  the install nudge real entrances. This is the first way to animate an element being *added* to
  the DOM without a JS mount hook, which matters because Blazor adds and removes all three by
  re-rendering with no lifecycle point in between.
- **Freshness** on `/my-comics` is a `data-freshness` attribute computed in C#
  (`ComicHistoryEntry.Freshness`), not an inline style. Binary expired/not-expired says nothing
  until the link is already dead; `fresh` / `fading` (past half of the 24-hour window) / `expired`
  makes the list itself communicate the deadline. Hover restores a faded thumbnail — the decay is
  a status indicator, not a punishment.
- **Paper ageing** rides the same attribute. `paper.js` builds ONE tileable grain texture once and
  publishes it as `--paper-grain`; after that the ageing is `background-image` plus
  `mix-blend-mode`, which is painting the browser was doing anyway — no rAF task, no observer,
  nothing on the shared scheduler. An `feTurbulence` filter is the obvious alternative and is
  re-evaluated on every paint of the thumbnails, which are the heaviest elements on the page. The
  tile is sampled on a torus so it genuinely seams; the noise is in the **alpha** of black pixels,
  which is what lets one texture age a card in either theme. Every rule resolves
  `var(--paper-grain, none)`, so a browser that could not build it just keeps the desaturation.
- **Reading the strip plays it.** `panel-scrub.js` fires one note of the comic's own motif per
  panel as that panel crosses the middle of the screen, drawn from the *same* seeded sequence
  `signature()` uses — so it is the figure the score reveal played, re-heard at the reader's pace.
  The travelling highlight that goes with it is `animation-timeline: view()` in app.css and costs
  nothing; only the crossing needs JS, and that is an `IntersectionObserver` with a
  `-50% 0px -50% 0px` root margin (a 1px band across the viewport centre), which fires two or four
  times for the whole page. Continuous things belong on `view()`; discrete crossings do not.
- **Shared-element transitions need the name on BOTH halves.** `view-transitions.js` tags the
  tapped card on the click and the arriving `.comic-strip-container` in `settle()` — which is the
  only moment the destination exists and is still before the "after" snapshot. With the name on
  only the source, as it was, the browser has nothing to pair and the "morph" was really the card
  fading out while the comic cross-faded in from nowhere. Sources are every list a comic opens
  from: `[data-physics-card]`, `.leaderboard-card`, `.archive-entry`, `.history-card`.
  `data-nav-direction` (from a small route-depth table, not history length) decides which way the
  incoming page slides; a transition that always moves the same way is a cross-fade with extra
  steps.
- **Cached vs uncached is a material.** `data-cached="ready|new"` on a restaurant card: a hit opens
  instantly and free, a miss spends a paid image call and ten seconds, and the only place the app
  drew that distinction was pin colour on a map panel most people never open. "Ready" gets a
  compositor-side sheen on an 11s period (a `transform` sweep, not a `background-position` one,
  which would repaint every card in the grid every frame) under `overflow: clip` +
  `overflow-clip-margin: 6px` — `hidden` would clip the CTA's focus ring. "New" gets a dashed
  edge, not a warning colour: spending a generation is the product working.


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
- Audio defaults to **on** and still needs a real user gesture to unlock — `AudioContext` created
  outside a trusted event stays `suspended` forever. Unlock is hung off existing button handlers,
  which is why there is no switch to find. That is also why `unlock()` **races** the resume against
  a short deadline instead of awaiting it: Chrome does not reject a resume it will not honour, it
  leaves the promise pending, and the discovery flow calls unlock from its own async path (a
  remembered ZIP resumes the search on load, before any tap). Awaiting it there hung the whole
  search — no request, no error, no clue — the moment sound stopped defaulting to off.
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

**Funnel analytics (`Features/Diagnostics`, `FunnelEndpoints`).** The PRD sets targets the app never measured; the
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
were all on the startup critical path. Three more followed with the slices below —
`PoSeeReviewShareLinks`, `PoSeeReviewCollections`, `PoSeeReviewModeration` — for ten in total,
plus a **second blob container**, `comics-kept`, created the same way.

### Insights

`GET /api/insights` and `/insights`: four charts over every score the app has ever recorded —
strangeness against star rating, score distribution, region comparison, weekly trend. It spends
nothing; every number comes from rows already written, so there is no Maps call and no AI call.

- Reads are **cross-partition scans**, honest at current volume and bounded by
  `InsightsOptions.MaxRowsScanned` (5000). The response carries `Truncated` and the page says so
  — a chart drawn from a capped sample is a different claim.
- `CachedAt` is deliberately **not** consulted. The Restaurants slice treats an old row as stale;
  for a historical chart the rating as it stood when the comic was drawn is the correct value.
- **Per-place dedup differs by chart, on purpose.** The distribution and the scatter take each
  place's highest score once, so a restaurant somebody regenerates weekly cannot weight the
  population by how often they hit redraw. The weekly trend keeps every week a place appears in,
  because that chart is about activity over time and the dedup would erase what it measures.
- Empty histogram buckets are **emitted with count 0**; quiet weeks are **omitted**, never
  zero-filled. A gap in a histogram means "none scored here"; a zero on a trend line asserts an
  average strangeness of zero for restaurants nobody drew.
- Each chart declares its own minimum sample and is judged alone — a thin scatter must not blank
  a region comparison that has enough to say something.
- **Chart colours are resolved at runtime from the real tokens**, via `theme-tokens.js`. They
  cannot be `var(--color-brand)`: Radzen writes the value onto the SVG as a presentation
  attribute and SVG attributes do not resolve custom properties. Hardcoding hex is the other
  option and it freezes light mode into a page that also renders dark. A `prefers-color-scheme`
  listener re-resolves and re-keys the chart.

### The share card, and short links

`og:image` used to point straight at the comic's blob URL. That URL carries a SAS signature that
lapses in about a week, while the Hall of Fame row it came from is designed to outlive
everything — so **every share older than the signature unfurled as a blank card**.

`GET /share/{placeId}/card.png` (`ShareCardService`, ImageSharp) composes a 1200x630 card on
demand: the comic cropped to fill, a gradient scrim, the score in a ring, the wordmark. Notes:

- **Not under `/api`.** `UserAgentValidationMiddleware` only lets social crawlers through on
  non-`/api` paths, so an `og:image` under `/api` would be fetched by exactly the clients that
  get a 400 there. Anonymous for the same reason a 401 is useless on a preview fetch.
- Composed rather than stored. It is a deterministic function of a comic that already exists, and
  caching it would add a second blob lifecycle for takedown to know about. One hour of
  `Cache-Control`, short enough that a takedown stops being served within the hour.
- A **missing blob is not a failure**: the card still renders brand ground, score and name, which
  is precisely the case the old blob-URL `og:image` could not survive.

`POST /api/share/{placeId}` mints a seven-character code and `GET /s/{code}` resolves it.
Idempotent per place (a reverse row), minted with `RandomNumberGenerator` (a guessable sequence
would let anyone enumerate every shared comic), from an alphabet with no `0/O` or `1/I/l` — these
get retyped off screenshots. **302, never 301**: a permanent redirect is cached past a takedown.
Malformed codes are rejected without a storage read, since the resolver is public.

### Kept comics (Collections)

`ComicHistoryService` remembers what a browser has seen; comics expire in 24h, so that list is
dead links on a device the user may not be holding. `/api/collections` is the other half: **Keep**
copies the artwork into the `comics-kept` container — which `ExpiredComicCleanupService` never
visits — and files it against the principal.

- A separate container is the feature. Sharing `comics` and exempting individual blobs would mean
  the cleanup service had to understand collections.
- Capped (`CollectionsOptions.MaxKeptPerUser`, 50). Keeping is the one action that opts a blob out
  of cleanup, and an unbounded keep is an unbounded bill on a free feature.
- Served by `GET /api/collections/{placeId}/image` behind the owning session, never a SAS. The
  ownership check is the authorization.
- Blob paths use a **hash of the principal**, not the principal: a principal is often an email,
  and blob paths turn up in storage explorers and access logs.
- The copy happens at Keep time, while the source still exists. Deferring it would be a keep that
  kept nothing.
- `/my-comics` renders both lists, labelled — kept is server-side and cross-device, history never
  leaves the browser.

### Moderation

`/api/reports` wrote rows nothing read, and the only way to act was `/api/takedowns`: a shared
admin key, an unreviewed hard delete, and **nothing stopping the next visitor from regenerating
the same comic about the same named business**. `Features/Moderation` closes both gaps.

- Actions are graded. **Hide** is reversible and is what unreviewed reports get. **Suppress**
  blocks generation and is what makes a removal stick. **Remove** erases the comic, blob,
  leaderboard row, Hall of Fame entry and every kept copy — and suppresses in the same call,
  suppression **first**, so a part-way failure leaves the safe half-state.
- `/api/takedowns` now suppresses before it erases. That was the actual bug: a completed takedown
  undid itself on the next tap.
- Auto-hide at **three** distinct reporters (`ModerationOptions.AutoHideReportThreshold`), not
  one — a single report is a signal, and unilateral unpublishing is a griefing tool. Never
  overrides a human verdict, or the queue becomes a voting mechanism.
- Gated by a **role**, not a shared key: a key names nobody, and an audit trail whose actor column
  reads "whoever had the key" cannot be audited. The policy is registered by the slice
  (`AddModerationAuthorization`) so the Auth slice does not have to know it exists.
- The gate **fails open**. It is consulted on every comic read; failing closed would turn a
  transient storage error into a total outage. Deletion is the durable enforcement.
- A withheld comic is **451, not 404** — the comic is not missing, it is being withheld — except
  on the share card, where a crawler-facing image should simply not exist.
- A row exists only once something has happened, so the table stays proportional to the problem
  rather than to the catalogue.

**Pre-publish content screening.** `IContentSafetyScreener` runs on the generated narrative after
the chat call and **before** the paid image call, so a refusal costs nothing.
`LexicalContentSafetyScreener` is a lexical floor, not a classifier — Azure AI Content Safety
implements the same interface and slots in without a caller changing, which is a deployment
decision (resource, endpoint, Key Vault secret) rather than a code one.

> **Two outcomes, and that is the whole design.** Blocking every risky narrative would break the
> product: "rats", "food poisoning" and "shut down by the health department" are ordinary content
> in the one-star reviews this app exists to mine. But the same sentence, restated by a model as
> a claim about a named business, is defamation-shaped. So allegation language **flags** —
> publishes, and lands in the queue for a human — and only categories with no legitimate reading
> **block**. Terms are word-boundary matched so "ratatouille" is not "rat", and words English
> uses figuratively about food (`assault`, `stole`, `drugged`) are excluded outright, because a
> boundary does not help when the whole word is the metaphor and a queue full of false positives
> is a queue nobody reads.

### Map discovery

An always-on map pane on `/` (`map.js` + `MapService`), with pins coloured by whether a place
already has a live comic — read from `GET /api/comics/cached?placeIds=...`. That distinction is
the point: a cache hit is instant and free, a miss spends a paid image call and about ten
seconds, and the grid had no way to say which was which.

**It is a pane beside the list, not a panel above it, and there is no longer a button.** A map
nobody opens tells nobody anything, and the toggle was worse than useless to the layout: its row
plus the collapsed panel's own grid cell meant the results grid started one cell along, so every
card was pushed out of place by an empty box. `.results-layout` is now a two-column grid at
≥64rem — sticky map left, cards right — and one column below it, with `align-items: start` doing
the real work, because a stretched column has no room to stick.

**Why a library is back after three.js and Rapier were deleted.** Those were ~2.4 MB of vendored
decoration over a DOM list and a card grid that already worked — the scene said nothing the
markup did not. A map answers a question the grid physically cannot. So the `shelf.js` conditions
apply instead of the verdict, and they are load-bearing:

- **Lazy in the module, not in the moment.** MapLibre is a pinned dynamic `import()` from a CDN
  and `map.js` itself is a few KB, but the fetch now happens on the first result set rather than
  on a click. A CDN round trip and a WebGL context land on every search that returns places;
  that is the price of the map being on by default, and it is the one thing to weigh before
  adding a second always-on library.
- **The list stays.** The map is a pane *beside* the results, never a replacement. The list
  remains in the DOM, focusable and screen-reader-readable; the pane is `aria-hidden`.
- **Fails quiet.** No CDN, no WebGL, no network: `show()` returns false and the pane says so in
  one line. `MapService` mirrors `FxService` — nothing may throw into .NET, and `SafeAsync<T>`
  carries the same `[DynamicallyAccessedMembers]` annotation for the same `IL2091` reason.
- It gets its **own WebGL context**, outside `gl-pool.js`. That is a documented exception on one
  route, not a regression: MapLibre owns its context and cannot draw into a shared atlas.
- Pin colours resolve from tokens via `theme-tokens.js`, same as the Insights charts.

> **Tile provider.** The default style points at OpenStreetMap's own raster tiles, which have a
> usage policy that rules out heavy application use. It is there so the feature works with no key
> and no account; point it at a paid provider before real traffic, and update the attribution in
> the style to match.

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

`/moderation` has no nav entry either, for the same reason, and is additionally gated on the
`Moderator` role. `/insights` is linked from the **right-hand session zone**, never
`nav.nav-links` — `HeaderContractUiTests` asserts the primary nav is exactly two items. `/my-comics`
is linked from nowhere.

Public, unauthenticated, and outside `/api` on purpose: `/s/{code}` (short links) and
`/share/{placeId}/card.png` (link-preview card). Both are fetched by clients that `/api` is built
to turn away.

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
  in `app.css`, not in scoped page CSS. `.chip-toggle` (Hall of Fame scope) and
  `.toast` (comic actions + Hall of Fame share) are shared for exactly the reason this file already
  documents: two pages needing the same control is how `.btn-primary` forked last time — discovery's
  sort chips were `.chip-toggle`'s other caller and are gone, and the primitive stayed in `app.css`
  rather than moving into a scoped sheet for the sake of a single caller. Scoped sheets load after `app.css` and carry a `[b-*]` attribute, so a page-level
  redefinition silently wins — that is how Diagnostics and the Hall of Fame drifted apart.

## Working rules (NET_AGENTS)

These govern how the agent operates in this repo, not how the code is written.

- **`master` only.** Do all work on `master`. Use another branch only when explicitly asked to.
- **Restart and verify after every code change.** Stop the app, start it again
  (`dotnet run --project src/PoSeeReview.Api --launch-profile https`, or the
  `start-api-clean` VS Code task), and confirm it actually came up before reporting done.
  Config/appsettings/Key Vault changes need a full restart — `dotnet watch` will not pick them up.
- **Look for a root `docs/` folder first, and fall back when it is not there.** If one exists, read
  it for the overall project summary before exploring the code. It does not exist right now — the
  generated reports were cleared out and are due to be rebuilt — so [README.md](README.md) (the PRD)
  and this file are the authoritative overview, and `docs/index.html` is not worth hunting for.
- **No `dotnet user-secrets`.** Non-secret config goes in `appsettings*.json`; real secrets go in
  Key Vault `kv-poshared` under the `PoSeeReview--` prefix. The one existing exception is
  `Takedowns:ApiKey` for local dev — it is a live credential, so it must never land in an
  appsettings file that is committed.
- **Never push to remote unless asked.** Committing locally is fine; `git push` is not, until the
  user says so — or until they type "git sync".
- **On "git sync": stage everything, commit, push.** Commit *all* outstanding changes first — a
  sync leaves nothing dirty behind. Short American-slang message that reads like a human wrote it
  ("fixed the busted nav", "cleaned up that css mess"), then push.
- **Only run the tests that cover the change.** Pick the project and `--filter` that exercise what
  was touched; for a change with no test surface — a copy tweak, a CSS value — run none at all.
  Never reach for the full suite after a code change.
- **Run the commands yourself.** Don't hand the user a command to paste when the agent can execute
  it; only ask when it genuinely needs their machine, credentials, or a decision.
- **TL;DR any answer over 100 words** with a ~20-word summary at the end.
