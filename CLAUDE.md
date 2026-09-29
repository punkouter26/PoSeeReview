# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

PoSeeReview turns Google Maps restaurant reviews into AI-generated comic strips with a 0–100
"strangeness score", ranked on a Hall of Fame leaderboard. One ASP.NET Core 10 host serves both
the API and the Blazor WASM client from the same origin (BFF pattern — no CORS, no tokens in the
browser). See [README.md](README.md) for the PRD.

## Commands

```powershell
dotnet restore
dotnet build PoSeeReview.sln
dotnet run   --project src/PoSeeReview.Api --launch-profile https   # HTTP 5000 / HTTPS 5001
dotnet watch --project src/PoSeeReview.Api --launch-profile https   # code-only changes
```

Config changes (appsettings, Key Vault) need a full restart — hot reload will not pick them up.
If another local app already holds 5000/5001, add `--urls "https://localhost:5101;http://localhost:5100"`.

VS Code tasks `start-api-clean` / `start-api-watch-clean` do the correct sequence:
stop the running API → start Azurite → start the API.

**Never kill every `dotnet` process.** The Bicep, C#/Roslyn, MSSQL and Unity language servers are
all `dotnet`-hosted, so a blanket `Get-Process dotnet | Stop-Process` takes the editor's tooling
down. The app runs as `PoSeeReview.Api.exe`; `kill-api-processes` stops that and only the `dotnet`
hosts whose command line names this project. A leftover instance is what causes MSB3021/MSB3027
DLL-lock failures on the next build.

### Tests

Four tiers, all xUnit. `dotnet test PoSeeReview.sln` runs everything, but E2EUI needs a live app.

```powershell
dotnet test tests/PoSeeReview.Unit          # pure unit
dotnet test tests/PoSeeReview.Integration   # Testcontainers Azurite (needs Docker), AI mocked
dotnet test tests/PoSeeReview.E2EAPI        # in-memory host + Testcontainers Azurite, AI mocked
$env:E2E_BASE_URL = "https://localhost:5001"; dotnet test tests/PoSeeReview.E2EUI  # C# Playwright

dotnet test tests/PoSeeReview.Unit --filter "FullyQualifiedName~GeoUtilsTests"   # one class
```

Tier quotas: Unit 100 / Integration 50 / E2EAPI 25 / E2EUI 25, counted in `[Fact]`/`[Theory]`
attributes and enforced in CI by `SCRIPTS/check-test-quota.mjs`. A new test in a full tier
replaces one. Playwright browsers install via
`pwsh tests/PoSeeReview.E2EUI/bin/Debug/net10.0/playwright.ps1 install chromium` after a build.

### Local storage

```powershell
docker compose up -d azurite    # official mcr.microsoft.com/azure-storage/azurite image, ports 10000-10002
```

Set `"Storage:UseAzurite": true` in `appsettings.Development.json` — without it local dev uses the
**real** Azure storage account via Key Vault.

Azurite blob URLs carry the account as their first path segment (`/devstoreaccount1/comics/{blob}`);
Azure's are `/comics/{blob}`. `BlobStorageService.ResolveBlobClient` anchors on the container
segment rather than skipping a fixed one.

### Deploy & smoke

`git push origin master` triggers [.github/workflows/deploy.yml](.github/workflows/deploy.yml):
`build` (test-quota check + unit tests + publish) → `deploy` (App Service `app-poseereview`, RG
`PoSeeReview`) → `smoke`. Integration and E2E suites run locally only. Infra under `infra/` is
applied by hand — see [infra/README.md](infra/README.md).

```powershell
$env:BASE_URL = "https://app-poseereview.azurewebsites.net"; node SCRIPTS/post-deploy-smoke.mjs
```

`SCRIPTS/setup.ps1` is the first-run bootstrap (WinGet/Docker/Azure CLI checks).

## Architecture

### Vertical slices

```
src/PoSeeReview.Api        ASP.NET Core host; also serves the WASM client
  Features/<Slice>/        endpoints + services + entities + repositories together:
                           Auth, Comics, Restaurants, Leaderboard, Diagnostics, Moderation
  Storage/                 cross-slice TableStorageRepository, BlobStorageService
  Identity/                ICurrentRequestIdentityAccessor + HttpContext impl
  Telemetry/               App Insights + OpenTelemetry, RoleNameTelemetryInitializer
  Testing/                 AiMockDelegatingHandler — never registered in Production
src/PoSeeReview.Client     mobile-first Blazor WASM, no tokens
src/PoSeeReview.Shared     wire DTOs, Ids/, Enums/, Contracts/, FluentValidation rules
```

Comics also owns share cards and short links (`/api/share`, `/s/{code}`); Moderation also owns
reports (`/api/reports`) and takedowns (`/api/takedowns`).

**Slices must not reference each other.** Anything two slices need lives in
`PoSeeReview.Shared/Contracts/` (`Comic`, `Restaurant`, `Review`, `LeaderboardEntry`;
`IComicRepository`, `ILeaderboardRepository`, `IRestaurantService`, `ILeaderboardService`,
`IHallOfFameArchive`, `IContentModerationGate`, `IContentSafetyScreener`). `IHallOfFameArchive`
exposes only the delete, so takedowns can erase archived entries without referencing Leaderboard.
`IContentModerationGate` is read by Comics and owned by Moderation; `IContentSafetyScreener` is
called by Comics and implemented by Moderation.

Moderation reads the reports **table** through its own read-only projection
(`ModerationReportRow`) rather than a repository — Table Storage is schemaless per row, so a POCO
with a subset of columns reads the same rows without owning them. It deliberately does not project
the reporter's `Details` or `ContactEmail`.

[Features/FeatureEndpoints.cs](src/PoSeeReview.Api/Features/FeatureEndpoints.cs) is the composition
root and the only file allowed to reference every slice. Folder == namespace == slice: every type is
`PoSeeReview.Api.<Folder>`. Interfaces exist where a slice boundary or a test fake needs them —
not for every service.

### Domain primitives

`Shared/Ids/` holds `readonly record struct` ids — `PlaceId`, `ComicId`, `UserId`, `RegionCode`.
Two boundaries still speak raw strings and convert at the edge: **table entities** (the Table SDK
persists primitives only; `FromDomain`/`ToDomain` wrap them) and **wire DTOs** (keeps the client's
source-generated `AppJsonContext` trim-safe).

### Authentication (BFF cookie proxy)

The WASM client never handles tokens. Session = `.PoSeeReview.Auth` cookie (HttpOnly,
SameSite=Strict, Secure). Routes: `/auth/login/microsoft` (Entra `/common` OIDC), `/auth/login/fake`
(guest cookie, 404 in Prod), `/auth/logout`, `/auth/me`.

**Authz is deny-by-default**: `AddBffAuthentication` sets a `FallbackPolicy` of
`RequireAuthenticatedUser`. Public endpoints opt out with `.AllowAnonymous()` — `/auth`, `/health*`,
`/diag`, `/api/takedowns` (own X-Api-Key filter), `/s/{code}`, `/share/{placeId}/card.png`,
OpenAPI/Scalar, and the SPA `index.html` fallback. Client `[Authorize]` is UI-only.

`FakeAuthHandler` maps `X-Fake-User`/`X-Fake-Roles` to a principal in Dev/Test and throws in
Production; Integration and E2EAPI clients authenticate that way. The login view renders the guest
button in **both** Development and Test — every E2E UI test clicks it.

### Configuration & secrets

- .NET 10 pinned by `global.json`; CPM via `Directory.Packages.props`; `TreatWarningsAsErrors` +
  `Nullable` via `Directory.Build.props`. MinVer versions from git tags (`v` prefix).
- Key Vault `kv-poshared` (RG `PoShared`), secrets prefix-scoped `PoSeeReview--` via
  `PrefixKeyVaultSecretManager`, `DefaultAzureCredential` throughout. The Bicep only grants read
  access to it; it never creates or writes a vault.
- Azure storage access is **Managed Identity only** (`AzureStorage:TableEndpoint`/`BlobEndpoint`).
  Connection strings exist solely for local Azurite.
- `Takedowns:ApiKey` gates `/api/takedowns` (timing-safe compare, 503 when unset). Key Vault in
  Azure; locally `dotnet user-secrets` — the one exception to the no-user-secrets rule.
- `AzureAd:ClientId` / `AllowedTenants` stay in appsettings.json (public identifiers);
  `AzureAd:ClientSecret` is Key Vault only.
- `Ai:ImageProvider` (`Gemini` default, `AzureOpenAI` = gpt-image-1-mini, ~1/10 the price, rougher)
  and `Ai:ChatProvider` (`AzureOpenAI` default, `Ollama` for free local runs) are chosen separately,
  so an image-model experiment is not also a scorer experiment. An unparseable value fails startup.
  `StartupSecretValidator` requires only the secrets of the selected providers.

### Pipeline ordering that matters

`UseForwardedHeaders()` runs first so the rate limiter and logging see the real client IP behind
App Service. `MapFallbackToFile("index.html")` must be a real endpoint in the main pipeline (not a
`MapWhen` branch) or its `AllowAnonymous` is ignored and `/` 401s.

Rate limiting: global fixed window (`RateLimiting:GlobalPermitLimit`, 240/min) per IP, plus
`comics-post` at 3/min on the paid generation stream. The SPA document and `/auth` call
`.DisableRateLimiting()` — a 429 on `index.html` is a blank white page with no retry.

Tables (`PoSeeReview{Comics,Restaurants,Leaderboard,Reports,HallOfFame,Budget,ShareLinks,Moderation}`)
and the `comics` blob container are created concurrently at startup by `TableStorageInitializer`;
repositories never call `CreateIfNotExists`, and startup fails fast if storage is unreachable.

### Comic generation

**One endpoint: `POST /api/comics/{placeId}/stream`** (server-sent events, one JSON
`ComicGenerationEventDto` per `data:` line — `phase` / `complete` / `error`). There is no plain
POST; the host serves the client, so there is no older API to fall back to.

- The stream is a 200 before generation can fail, so the real status travels in `ErrorStatus`
  (`ComicsEndpoints.DescribeFailure`). A mid-stream failure is surfaced, never retried — a retry
  pays for the same comic twice.
- Phases reach the response through a `Channel`, never straight from `IProgress.Report` — BCL
  `Progress<T>` posts to the thread pool here and races the completion write.
- `ComicGenerationPhase` members map 1:1 to pipeline steps and are ordered by
  `ComicView._comicSteps`. Add a phase in both places.
- The client request must call `SetBrowserResponseStreamingEnabled(true)`; `ResponseHeadersRead`
  alone buffers the whole body on WASM and the stepper never moves.

**One chat call.** The analysis returns score inputs, `narrative` and `panels: [{scene, caption}]`
together (`ChatPrompts.ToAnalysis` makes the result total). `ComicTextOverlayService` is
text-in/pixels-out. The painter draws the scorer's shot list: each panel's `scene` is a numbered line
of `ComicImagePrompt`.

**The model rates, the code scores.** The model returns `absurdity`, `specificity`,
`storyPotential` (0–10); `ChatPrompts.ComputeScore` multiplies:
absurdity × (6 + 0.2·specificity + 0.2·story). The system message holds everything static and the
user message is only `<reviews>` — interleaving rules with reviews tripped Azure's jailbreak filter.
Judge a prompt change with the golden set:

```powershell
dotnet run SCRIPTS/prompt-eval.cs -- --model gemma3:4b      # Ollama, free
dotnet run SCRIPTS/prompt-eval.cs -- --provider azure        # needs AZURE_OPENAI_ENDPOINT/_API_KEY
```

gpt-5.4-nano defaults to no reasoning. Do not set `reasoning_effort`: any value but `none` rejects
the scorer's `temperature: 0.3`. Reasoning deployments also reject `max_tokens`, which OpenAI SDK
2.1.0 cannot avoid sending — `ChatTokenBudget.CanApplyCap` lets startup warn instead.

- **Single flight.** `ComicGenerationLock` is a per-place in-process `SemaphoreSlim`; the pipeline
  re-reads the cache under it so a double-tap pays once.
- **`Comics:PromptVersion` is part of the cache key.** A prompt tune misses the cache on its own;
  rows written before versioning read back as 0.
- **Cost.** `AiCostTracker` emits `Ai.Cost.Usd` / `Ai.Tokens` priced per input and output rate from
  `AiPricing:Models`; `AiPricing:FreeProviders` keeps Ollama at $0.
- **An image refusal is a refusal.** `ImageDeclinedException` → 422 and a moderation flag. Never
  redraw a substitute scene under the reviews' score. `ComicImagePrompt` blunts filter-tripping
  terms and reports them via `*.Image.PromptTermsBlunted`.
- **Profanity is masked, not dropped** (`[bleep]`, one-for-one leet normalisation); only explicit
  sexual terms drop a review.
- **Comics are lossy WebP.** Set `WebpEncoder.FileFormat` explicitly or ImageSharp inherits lossless.
  `DeleteComicImageAsync` removes every extension, since older rows are `.png`.
- **Receipts (quoted review fragments) were removed.** If they ever come back, the verbatim gate
  must come back with them — a fabricated quote about a named restaurant is a defamation problem.

### Spend control, reports, moderation

- **Daily budget** (`GenerationBudgetService`, table `PoSeeReviewBudget`): a UTC-day counter per
  principal and app-wide, reserved **before** the pipeline, refunded on a cache hit and on failures
  that provably preceded the image call. The service ceiling is checked first. It **fails open**
  after `MaxConcurrencyRetries`. `GET /api/comics/budget` is free so the UI greys out the button
  before a tap.
- **`GET /api/comics/{placeId}/image`** re-serves the blob from this origin so it can be saved (the
  `download` attribute is ignored cross-origin). Not used for display.
- **Reports** (`/api/reports`): session required, rate limited, deduped by reporter (RowKey is the
  principal), only ever writes a row.
- **Moderation** is graded: **Hide** (reversible, what auto-hide at 3 distinct reporters does),
  **Suppress** (blocks generation — what makes a removal stick), **Remove** (erases comic, blob,
  board row and Hall of Fame entry, suppressing **first** so a partial failure is safe).
  `/api/takedowns` also suppresses before it erases. Gated by the `Moderator` role, not a shared key.
  The gate **fails open** on read errors. A withheld comic is **451**, except on the share card (404).
- **Pre-publish screening.** `IContentSafetyScreener` runs on the narrative before the paid image
  call. Allegation language **flags** (publishes, queues for a human); only categories with no
  legitimate reading **block**. Word-boundary matching; figurative food words excluded.

### Hall of Fame, share cards, previews

- **Weekly Hall of Fame** keeps each place's **peak** and outlives the 24h comic (hence
  `ImageExpired`). The live board mirrors the live comic: a redraw below
  `MinimumStrangenessScore` deletes the row.
- **Share card** `GET /share/{placeId}/card.png` (`ShareCardService`) composes a 1200x630 PNG on
  demand. Not under `/api` (UA validation only admits crawlers off `/api`), anonymous, one hour of
  `Cache-Control`. A missing blob still renders a card.
- **Short links** `POST /api/share/{placeId}` → `GET /s/{code}`: idempotent per place,
  `RandomNumberGenerator`, no `0/O/1/I/l`, **302 never 301** (a cached 301 outlives a takedown).
- **Link previews**: `SocialPreviewMiddleware` answers crawlers on `/comic/{placeId}` with Open Graph
  HTML (everything `HtmlEncode`d — review text is third-party). Middleware, not an endpoint, and it
  runs before rate limiting and auth.

### Client

- **History.** `ComicHistoryService` keeps seen comics in `localStorage` (`posee_comic_history`) for
  `/my-comics`; `BoardMemoryService` keeps last-seen ranks per region. Both degrade to no-ops when
  storage throws, and both types are in `AppJsonContext`.
- **Account menu.** `/my-comics` is linked from the identity badge's native `popover`, never from
  `nav.nav-links`, which `HeaderContractUiTests` pins at exactly two items. `/diagnostics` and
  `/moderation` have no nav entry.
- **Components.** `ComicRow` is every list of comics; `StateCard` is every empty/error/404 screen;
  `PageShell` is the one-row page header. Add to these rather than hand-rolling a layout.
- **Radzen** is still referenced (package, `material-base.css` in `@layer vendor`, `--rz-*` token
  mapping) but no component renders a Radzen control any more.
- **`AnalyticsService`** only writes debug logs; nothing is sent anywhere.

### Design system (`wwwroot/css/app.css`)

Read its header comment before adding CSS.

- **Cascade layers**: `reset, vendor, tokens, base, page, shared, utilities`. `page` (the scoped-CSS
  bundle, pulled in via `@import … layer(page)`) sits **before** `shared`, so a scoped sheet cannot
  fork `.btn`. Element selectors go in `base`; class components in `shared` — layer order beats
  specificity. Never add a plain `<link>` for vendor or scoped CSS: unlayered CSS beats every layer.
- `page` cannot change a `.btn`'s padding; `.btn` reads `--btn-pad-x` for that.
- **Tokens**: surface/ink split (`--color-accent` background vs `--color-accent-ink` text; fix a
  failing colour by pairing it, not darkening it). `--color-border` decorative vs
  `--color-border-strong` (≥3:1). `--surface-inverse` / `--color-on-dark-*` for the always-dark nav
  and hero. 8-step spacing, 7-step fluid type, `--tap-target` 44px, four breakpoints
  (`40/48/64/80rem`); prefer a container query when the question is a component's room.
- **Motion** scale `--duration-*` / `--ease-*`; reduced motion zeroes durations at `:root`.
- **Elevation is a pair** `--elevation-N-surface` + `--elevation-N-shadow` (dark mode rises by
  getting lighter). Redefine every token in **both** the dark media query and
  `:root[data-theme="dark"]`.
- **Boot splash** is centred with `min-height: 100dvh`; a top offset caused 0.20 CLS on every load.
- [ColorContrastTests.cs](tests/PoSeeReview.Unit/Utilities/ColorContrastTests.cs) parses the real
  tokens and asserts WCAG ratios for every text token against **every** surface token. Tune text
  tokens against `--color-surface-alt` (light) and `--color-highlight` (dark) — the worst cases.

### Graphics and audio (`wwwroot/js/`, fronted by `FxService`)

`fx.js` is the only module entry point, publishes `window.poseeFx`, and is the composition root:
`audio.js` does not know haptics exist; deciding that a cue also buzzes is made there.
`propagateAudioState()` fans a sound-preference change out to haptics.

Live modules: `gfx-core.js`, `gradient.js`, `audio.js`, `haptics.js`, `comic-reveal.js`,
`panel-scrub.js`, `view-transitions.js`, `scroll-guard.js`, `modal.js` (+ `pwa.js`, `theme-tokens.js`
loaded by `index.html`).

- **`gfx-core.js`**: one shared `requestAnimationFrame` loop (never start a private rAF), a 20ms
  frame budget that steps the tier down after **1500ms** of sustained overrun (milliseconds, not
  frames; not persisted), tiers `off`/`lite`/`full` stamped on `<html data-fx-tier>`. `off` is forced
  by reduced motion or no WebGL2. `createSurface` gives one WebGL2 context per canvas.
  `poseeFx.stats()` in the console is the frame-budget readout.
- **Backdrop** (`gradient.js`, `full` tier): built from `--color-surface` / `--color-brand-surface`
  / `--color-brand`, re-read on a theme flip. `.page` stands aside only while
  `:root[data-fx-backdrop="on"]`, set only while the shader is genuinely drawing — every failure path
  keeps the CSS background.
- **Nothing may throw into .NET.** `FxService` swallows `JSException` and returns a benign default
  (0 handle = not running). `SafeAsync<T>` needs its `[DynamicallyAccessedMembers]` annotation or
  the trimmed client fails `IL2091`.
- **Audio** is synthesised (no assets), defaults **on**, and cannot start before a trusted gesture;
  `unlock()` **races** the resume against a short deadline — Chrome leaves an unhonoured resume
  pending forever, which once hung the whole discovery search. Reduced motion forces silence.
  `signature(placeId, score)` seeds a per-restaurant motif (seed by place id, never name);
  `panel-scrub.js` replays its notes as panels cross mid-screen. Narration (`speechSynthesis`) must
  `cancel()` before `speak()` and on teardown — the queue is browser-global.
- **Haptics** follow the sound preference and can be switched off on their own, never on on their
  own. Three fixed patterns (tap / confirm / error). Switches for tier, sound and haptics live on
  `/diagnostics`.
- **Ink development** (`comic-reveal.js`): a freshly drawn comic develops top-down via a
  `mask-image` driven by the registered `--comic-reveal` property (initial value 1 = fully visible).
  **Every exit path must clear `data-comic-reveal`**, which is why `ComicStrip.DisposeAsync`
  finishes the reveal first.
- **View transitions**: never await rAF inside the update callback (rendering is suppressed while it
  is pending); the click is taken from Blazor in a capture-phase listener and `Blazor.navigateTo` is
  called inside the update callback, or the "old" snapshot is already the new page. The morph needs
  the view-transition name on **both** halves — `[data-comic-card]` / `.comic-row` and the arriving
  `.comic-strip-container`.
- `@property` registrations sit at the top level of app.css (not in a layer). Scroll-driven effects
  use `animation-timeline: view()` with `both`; `@starting-style` animates the toast and report
  dialog in.

### PWA

`manifest.webmanifest`, `service-worker.js`, `offline.html`. The worker is **network-first** because
Blazor verifies `_framework/*` against `blazor.boot.json` integrity hashes and those files are not
fingerprinted — cache-first means a white screen after a deploy. `/api`, `/auth`, `/diag`, `/health`
are never cached. `pwa.js` still captures `beforeinstallprompt`, but nothing renders an install
prompt any more.

### Endpoints worth knowing

`/health` (+ `/live`, `/ready`); `/diag` (masked keys, integration status, Dev **and** Prod — behind
`UserAgentValidationMiddleware`, so anonymous scripted fetches get a 400); `/diag/mock-status`
drives the client's "USING MOCK DATA" banner.

## Conventions

- Minimal APIs only: one `MapGroup()` extension per slice, registered via `MapFeatureEndpoints()`.
- C# 14 density — primary constructors, collection expressions, pattern matching; minimal comments.
- Client + Shared are `EnableTrimAnalyzer`; JSON is source-generated — **add every new DTO to
  `AppJsonContext`**. The client does not set `IsTrimmable` (Router/LayoutView need reflection).
- NuGet audit warnings (NU1901/2/3) are never suppressed. `Microsoft.OpenApi` carries a direct pin;
  `Testcontainers.Azurite` is pinned at 4.14.0 to clear an `SSH.NET` advisory — do not downgrade.
- UI: no inline styles; scoped `.razor.css` + tokens. **Never hardcode a colour in scoped CSS.**
  Shared primitives (`.btn*`, `.alert*`, `.chip-toggle`, `.toast`) live in `app.css`.
- After deleting a JS module, grep `fx.js` **and** `FxService.cs` for its names — `guard()` swallows
  a `ReferenceError`, so a dangling reference fails silently.

## Working rules (NET_AGENTS)

They live in [AGENTS.md](AGENTS.md) so every agent reads the same list:

@AGENTS.md
