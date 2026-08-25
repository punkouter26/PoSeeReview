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
                           (Auth, Comics, Restaurants, Leaderboard, DevSessions, Diagnostics, Takedowns)
  Storage/                 cross-slice TableStorageRepository, BlobStorageService
  Identity/                ICurrentRequestIdentityAccessor + HttpContext impl
  Telemetry/               App Insights + OpenTelemetry, RoleNameTelemetryInitializer
  Testing/                 AiMockDelegatingHandler — never registered in Production
src/PoSeeReview.Client     mobile-first Blazor WASM, no tokens
src/PoSeeReview.Shared     wire DTOs, Ids/, Enums/, Contracts/, FluentValidation rules
```

**Slices must not reference each other.** Anything two slices need lives in
`PoSeeReview.Shared/Contracts/` (`Comic`, `Restaurant`, `Review`, `LeaderboardEntry`;
`IComicRepository`, `ILeaderboardRepository`, `IRestaurantService`, `ILeaderboardService`).
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
- AI backend is selected by the `AiImageProvider` enum (`Gemini` default, `HuggingFace`,
  `AzureOpenAI`), which replaced a `UseHuggingFace` boolean that could not express the third
  provider and coupled the chat and image paths together.

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

### Comic generation: receipts and streaming

`AnalyzeStrangenessAsync` returns a `StrangenessAnalysis` record (not a tuple) carrying the score,
panel count, narrative, and **unverified** receipts — model-claimed verbatim review fragments with
the points each contributed. `ComicGenerationService.VerifyReceipts` then drops any quote that is
not actually present in the reviews that were sent. Treat that gate as load-bearing: these strings
are displayed as quotations from real reviewers about a named restaurant, so a fabricated one is a
defamation problem, not a cosmetic bug. Receipts persist as a JSON string column (`ReceiptsJson`)
because Table Storage has no collection type. The HuggingFace provider returns none by design.

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
  `ComicView._phaseOrder`. Add a phase in both places or the stepper misreports.
- The client falls back to the plain POST **only** on 404/405 — a status that proves nothing ran.
  A mid-stream failure is surfaced, not retried, because a retry pays for the same comic twice.
- App Service's proxy may still buffer the whole response despite `X-Accel-Buffering: no`. That
  degrades to a correct comic with useless progress, which is why the fallback is not wired to it.

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

[ColorContrastTests.cs](tests/PoSeeReview.Unit/Utilities/ColorContrastTests.cs) parses the real
token values out of app.css and asserts WCAG ratios. It reads the stylesheet rather than restating
the hex codes on purpose — and it immediately caught a dark-mode border at 1.88:1 that hand
calculation had missed.

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
- **A 20ms frame budget with automatic downgrade.** ~1.5s of sustained overrun steps the tier
  down. This is what makes "sustains 60 FPS" a mechanism rather than a hope. The downgrade is
  deliberately *not* persisted — one heavy page shouldn't become a permanent setting.
- **Tiers** `off` / `lite` / `full`, stamped onto `<html data-fx-tier>` so CSS can respond.
  `off` is forced by `prefers-reduced-motion` or missing WebGL2 and cannot be overridden;
  Save-Data, low `deviceMemory`, or few cores default to `lite`.

Effect modules: `audio.js` (zero-asset Web Audio synthesis), `gradient.js`, `comic-fx.js`,
`particles.js`, `loading-ring.js`, `scroll-guard.js`, plus two lazily imported heavies —
`hall-shelf.js` (Three.js, ~171KB gz) and `grid-physics.js` (Rapier, **~580KB gz**, additionally
deferred to `requestIdleCallback` and skipped on Save-Data). Neither is on the first-load path;
`SCRIPTS/fx-perf-check.mjs` asserts that.

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

`ui-check.mjs` performs ONE real comic generation, which spends a paid image call.

Measured floor under forced software rendering (no GPU in headless Chromium): 60 FPS mean with
gradient + 400-particle burst + 3D shelf active. Worst-frame spikes of 380-630ms do occur, from
shader compilation and the Three.js module parse — one-off, not steady state.

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
"USING MOCK DATA" banner. `/diagnostics` has no nav entry; reach it by URL. Note `/diag` sits behind
`UserAgentValidationMiddleware`, so anonymous scripted fetches get a 400 — that is why the smoke
script no longer asserts on it.

## Conventions

- Minimal APIs only, no controllers: one `MapGroup()` extension per slice, registered via
  `MapFeatureEndpoints()`.
- Known-broken on `master`: `dotnet build PoSeeReview.sln` fails `NU1903` on `SSH.NET` 2025.1.0
  (transitive via `Testcontainers.Azurite`), which blocks the Integration and E2EAPI projects.
  It is a NuGet advisory, not a code change — audit warnings are never suppressed here, so the fix
  is a package bump.
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
- Shared `.btn`/`.btn-primary`/`.btn-secondary`/`.alert*` primitives belong in `app.css`, not in
  scoped page CSS. Scoped sheets load after `app.css` and carry a `[b-*]` attribute, so a page-level
  redefinition silently wins — that is how Diagnostics and the Hall of Fame drifted apart.
