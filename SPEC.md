# SPEC — PoSeeReview Insights

Cross-restaurant data visualization for PoSeeReview. **Additive only**: one new vertical slice
(`Features/Insights`) and one new client page (`/insights`). Nothing in the existing comic
pipeline, Hall of Fame, auth, PWA or graphics layer changes.

Status: awaiting approval. No code is written until the plan in `tasks/todo.md` is approved.

---

## 1. Objective

PoSeeReview turns restaurant reviews into comics one restaurant at a time. Every score it has ever
computed is already stored, and nothing looks across them. `/insights` is that view: four charts
that answer aggregate questions the per-comic experience cannot.

**It spends nothing.** Every number comes from Azure Table rows the app already wrote. No Google
Maps call, no AI call, no new external dependency.

### Questions each chart answers

| Chart | Question |
|---|---|
| Score vs star rating (bubble) | Are strange restaurants also badly rated, or is strangeness orthogonal to quality? |
| Score distribution (column) | Does the strangeness scorer actually discriminate, or cluster everything mid-range? |
| Region comparison (bar) | Which region produces the weirdest reviews? |
| Weekly trend (line) | Is peak strangeness rising or falling week over week? |

---

## 2. User journeys

### J1 — Signed-in user views insights (happy path)
1. User is signed in (real Entra session, or guest cookie in Dev/Test).
2. Clicks **Insights** in the right-hand session zone of the header, or navigates to `/insights`.
3. Page renders a loading state, issues one `GET /api/insights`.
4. Four charts render, each labeled with the sample size it was computed from.
5. A footer states the data scope: "All time, all regions. N scored restaurants across M regions."

### J2 — Not enough data yet
1. Same entry.
2. A chart whose minimum sample size is unmet renders an empty-state panel instead:
   "Needs at least 10 scored restaurants. You have 3." plus a button to `/` to generate a comic.
3. Charts are evaluated **independently** — the region chart can render while the scatter cannot.
4. If zero restaurants are scored, the whole page is one empty state, not four.

### J3 — Developer verifies the charts
1. App runs with the mock path active.
2. `/insights` renders every chart with clearly-labeled sample data.
3. The existing "USING MOCK DATA" banner is visible, driven by `/diag/mock-status` picking up the
   `IMockable` registration.

### J4 — Anonymous visitor
1. Navigates to `/insights` without a session.
2. Client `[Authorize]` sends them to the login view; `GET /api/insights` returns 401 regardless.

### J5 — Backend unreachable
1. `GET /api/insights` fails or times out.
2. Page renders a shared `.alert` error with a retry button. No partial or stale charts.

---

## 3. Pinned stack

Already pinned by the repo; this project adds **no new package**.

| Item | Version | Source |
|---|---|---|
| .NET SDK | 10.0.100, `rollForward: latestMinor` | `global.json` |
| ASP.NET Core / Blazor WASM | 10.0.7 | `Directory.Packages.props` |
| Radzen.Blazor | 11.2.7 | `Directory.Packages.props` |
| Azure.Data.Tables | as pinned | `Directory.Packages.props` |
| xUnit / Testcontainers.Azurite | as pinned (Azurite **4.14.0**, do not downgrade) | `Directory.Packages.props` |
| Microsoft.Playwright | as pinned | `tests/PoSeeReview.E2EUI` |

### Radzen components used (verified against the 11.2.7 API surface)

- `RadzenChart` with `RadzenBubbleSeries<T>`, `RadzenColumnSeries<T>`, `RadzenBarSeries<T>`,
  `RadzenLineSeries<T>`
- `RadzenValueAxis`, `RadzenCategoryAxis`, `RadzenAxisTitle`, `RadzenGridLines`
- `RadzenChartTooltip` / `RadzenChartTooltipOptions`
- `RadzenCard`, `RadzenStack`, `RadzenButton` for layout, matching existing pages

`RadzenBubbleSeries` is confirmed to exist and maps a third dimension to circle radius — that is
what carries total review count on the scatter.

---

## 4. Commands

```powershell
dotnet restore
dotnet build PoSeeReview.sln
dotnet run --project src/PoSeeReview.Api --launch-profile https   # HTTP 5000 / HTTPS 5001

dotnet test tests/PoSeeReview.Unit
dotnet test tests/PoSeeReview.Integration          # needs Docker (Testcontainers Azurite)
dotnet test tests/PoSeeReview.E2EAPI
$env:E2E_BASE_URL = "https://localhost:5001"; dotnet test tests/PoSeeReview.E2EUI

dotnet test tests/PoSeeReview.Unit --filter "FullyQualifiedName~InsightsAggregatorTests"

docker compose up -d azurite
$env:BASE_URL = "https://localhost:5001"; node SCRIPTS/ui-check.mjs   # spends one paid image call
```

Lint/analysis is the build: `TreatWarningsAsErrors` + `Nullable` + `EnableTrimAnalyzer` (Client,
Shared) are global via `Directory.Build.props`. A warning is a failed build.

---

## 5. Project structure

```
src/PoSeeReview.Api/Features/Insights/          NEW SLICE
  InsightsEndpoints.cs        MapGroup("/api/insights"), one GET
  InsightsService.cs          aggregation — the maths, no storage concerns
  InsightsRepository.cs       cross-partition reads of HallOfFame + Restaurants tables
  InsightsOptions.cs          minimum sample sizes, row cap (slices own their options)
  MockInsightsService.cs      IMockable sample data, Dev/Test only

src/PoSeeReview.Shared/Dtos/
  InsightsDto.cs              wire DTO + the four chart point records

src/PoSeeReview.Client/
  Pages/Insights.razor        the page
  Pages/Insights.razor.css    scoped, tokens only
  Services/InsightsClient.cs  typed HttpClient wrapper
  Services/AppJsonContext.cs  MODIFIED — register every new DTO
  Layout/NavMenu.razor        MODIFIED — session-zone link only
  Layout/NavMenu.razor.css    MODIFIED — .nav-insights, mirroring .nav-mycomics

src/PoSeeReview.Api/Features/FeatureEndpoints.cs   MODIFIED — MapInsightsEndpoints()
src/PoSeeReview.Api/.../InfrastructureServiceCollectionExtensions.cs  MODIFIED — DI registration

tests/PoSeeReview.Unit/Services/InsightsAggregatorTests.cs
tests/PoSeeReview.Integration/InsightsRepositoryTests.cs
tests/PoSeeReview.E2EAPI/InsightsEndpointTests.cs
tests/PoSeeReview.E2EUI/InsightsUiTests.cs
```

**Slice isolation (NET_RULES 2.2).** `Features/Insights` references no other slice. It reads the
`PoSeeReviewHallOfFame` and `PoSeeReviewRestaurants` tables directly through its own repository
using `AzureStorageOptions` table names, exactly as other slices do. If a type turns out to be
needed by two slices it moves to `PoSeeReview.Shared/Contracts/`. `FeatureEndpoints.cs` is the only
file that references every slice.

---

## 6. Data model

### Sources (both already written by the running app)

**`PoSeeReviewHallOfFame`** — `HallOfFameEntity`. PartitionKey `HOF_{Region}_{WeekKey}`,
RowKey `{InvertedScore}_{PlaceId}`. Carries strangeness score, region, ISO week key, restaurant
name, place id. Never deleted by `ExpiredComicCleanupService` — this is the copy that outlives the
24h comic.

**`PoSeeReviewRestaurants`** — `RestaurantEntity`. Carries `AverageRating`, `TotalReviews`,
`Region`, `Latitude`, `Longitude`, `CachedAt`.

### Two decisions about reading these

1. **Cache validity is ignored.** `RestaurantRepository.IsCacheValid` treats rows older than the
   cache window as stale, but rows are never deleted. For a historical chart the star rating as it
   stood when the comic was generated is the *correct* value, not a defect. Insights reads rows
   directly and does not consult `CachedAt`.
2. **Reads are cross-partition scans.** "All time, everywhere" cannot use the partition-per-
   region-week layout. This is honest at current volume and does not scale; it is bounded by
   `InsightsOptions.MaxRowsScanned` (default 5000) and the response reports `Truncated: true`
   when the cap is hit. Documented as a known limit, not hidden.

### Join

Hall of Fame rows are the spine (a restaurant appears only if it has been scored). Restaurant rows
are joined on `PlaceId` to supply star rating and review count. **A Hall of Fame row with no
matching restaurant row is kept** for the distribution, region and weekly charts, and **excluded
from the scatter only**, because the scatter needs an x-value. Each chart reports its own `N`.

### Wire DTO

```csharp
public sealed record InsightsDto(
    InsightsSummaryDto Summary,
    IReadOnlyList<ScoreVsRatingPointDto> ScoreVsRating,
    IReadOnlyList<ScoreBucketDto> ScoreDistribution,
    IReadOnlyList<RegionStatsDto> Regions,
    IReadOnlyList<WeeklyPointDto> WeeklyTrend);

public sealed record InsightsSummaryDto(
    int ScoredRestaurants, int RegionCount, int WeekCount,
    bool Truncated, bool IsMockData);

public sealed record ScoreVsRatingPointDto(
    string RestaurantName, double StarRating, double StrangenessScore, int TotalReviews);

public sealed record ScoreBucketDto(int BucketStart, int BucketEnd, int Count);

public sealed record RegionStatsDto(
    string Region, double AverageScore, double PeakScore, int EntryCount);

public sealed record WeeklyPointDto(
    string WeekKey, DateTimeOffset WeekStart, double AverageScore, double PeakScore, int EntryCount);
```

Every one of these is registered in `AppJsonContext`. The Client is trim-analyzed; reflection-based
serialization fails the build, it does not fail at runtime.

### Aggregation rules

- **Buckets**: ten fixed 10-point buckets, `[0,10) … [90,100]`. The top bucket is closed so a
  score of exactly 100 lands somewhere. Empty buckets are **emitted with count 0**, not omitted —
  a gap in a histogram must read as zero, not as missing.
- **Region stats**: one entry per distinct `RegionCode`, sorted by average score descending.
- **Weekly trend**: grouped by `HallOfFameEntity.WeekKeyFor`, ordered by `WeekStartFor`. Weeks
  with no entries are **omitted**, not zero-filled — zero average strangeness is a false claim,
  where an absent point is honest.
- **Deduplication**: a place scored in several weeks contributes one point per week to the weekly
  trend, but only its **highest** score to the scatter and distribution, so a frequently
  regenerated restaurant cannot skew the distribution.

---

## 7. API

`GET /api/insights` → `200 InsightsDto`

- **Requires a session.** No `.AllowAnonymous()` — the deny-by-default `FallbackPolicy` of
  `RequireAuthenticatedUser` applies, matching Leaderboard and every other business slice.
- Under the global fixed-window limiter (240/min by IP). No dedicated named limiter: the endpoint
  is a pure read of stored rows and spends nothing.
- No parameters. Scope is fixed at all-time, all-regions.
- `500` on storage failure, as a ProblemDetails, matching the pattern in `AnalyticsEndpoints`.
- Registered in `FeatureEndpoints.MapFeatureEndpoints()` via `MapInsightsEndpoints()`.

---

## 8. Empty states

Each chart declares its own minimum sample size in `InsightsOptions`:

| Chart | Minimum | Why |
|---|---|---|
| Score vs rating | 10 points | Below this a scatter shows no relationship, only noise |
| Score distribution | 10 restaurants | Fewer than 10 across 10 buckets is a bar chart of ones |
| Region comparison | 2 regions | A one-bar comparison compares nothing |
| Weekly trend | 3 weeks | Two points is a line segment, not a trend |

Below the minimum, a chart renders a panel stating the requirement, the current count, and a link
to generate a comic. Charts are evaluated independently. Zero scored restaurants collapses the
whole page into a single empty state.

---

## 9. Mock data

`MockInsightsService : IInsightsService, IMockable`, registered only outside Production, following
the existing mock-registration convention so `/diag/mock-status` reports it and the client's
"USING MOCK DATA" banner lights up. Returns a fixed, deterministic dataset large enough to clear
every minimum above, with `IsMockData: true` in the summary so the page can label the charts.

`FakeAuthHandler` already throws if constructed in Production; the mock service follows the same
posture — registration is environment-gated, not flag-gated at runtime.

---

## 10. UI conventions (inherited, non-negotiable)

- **No inline styles.** Scoped `.razor.css` + design tokens from `wwwroot/css/app.css`.
- **Never hardcode a colour.** Radzen chart series colours are set from design tokens so dark mode
  works. A literal `white` under token-driven text renders white-on-white in dark mode, and the
  theme tests assert token *values*, not rendered contrast.
- **Cascade layers.** Scoped CSS lands in the `page` layer, which sits before `shared` — a scoped
  sheet must not fork a shared primitive like `.btn`. Anything two pages need goes in `app.css`.
- Chart containers use `.cq-card` / container queries rather than media queries — the question is
  how much room the *component* has.
- **Elevation is a pair** (`--elevation-N-surface` + `--elevation-N-shadow`). Never reach for a
  bare `--shadow-md`; it is invisible in dark mode.
- Every new text/surface colour pairing must pass `ColorContrastTests`, which parses the real token
  values out of `app.css`. Tune against `--color-surface-alt` (light) and `--color-highlight`
  (dark) — those are the worst cases, not the card.
- The `/insights` link goes in `.nav-user-zone` beside `.nav-mycomics`. It must **not** become a
  `.nav-item` inside `nav.nav-links`: `HeaderContractUiTests` asserts exactly two.
- Mobile-first. Charts must not overflow at 320px — `ui-check.mjs`'s overflow walk is extended to
  include `/insights`.

---

## 11. Testing strategy

xUnit across four tiers. Quotas (NET_RULES 5.1) are per tier for the whole repo — Unit 100 /
Integration 50 / E2EAPI 25 / E2EUI 25 — so this feature's budget is what remains.

**Unit** (`InsightsAggregatorTests`) — pure aggregation, no storage:
bucket boundaries including exactly 0 and exactly 100; empty buckets emitted as zero; weeks with
no data omitted; per-place highest-score dedup; region ordering; scatter excludes rows with no
restaurant match while other charts keep them; each minimum-sample threshold; `Truncated` set when
the row cap is hit.

**Integration** (`InsightsRepositoryTests`) — Testcontainers Azurite:
cross-partition read returns rows from multiple region-week partitions; stale-by-`CachedAt`
restaurant rows are still read; row cap enforced; empty tables return an empty result rather than
throwing.

**E2EAPI** (`InsightsEndpointTests`) — in-memory host + Azurite, AI mocked:
`GET /api/insights` 401 without a session; 200 with `X-Fake-User`; DTO shape and JSON round-trip
through `AppJsonContext`; empty-data response has zero counts and does not 500.

**E2EUI** (`InsightsUiTests`) — C# Playwright against a live app:
page renders four chart regions; empty states appear when data is thin; the session-zone link is
present and `nav.nav-links` still has exactly two items; no horizontal overflow at 320px.

**Coverage target**: the `InsightsService` aggregation is the logic worth covering — ≥90% line
coverage on that file. Repository and endpoint are covered by behaviour, not by a percentage.

**Rules**: strict TDD, Red → Green → Full Suite → Build → Commit, one commit per task. Never skip,
weaken, or delete a failing test. Never mock away validation logic. Failing tests are reported
faithfully.

---

## 12. Boundaries

**Always (proceed without asking)**
- Create/modify files inside the manifest in `tasks/todo.md`
- Run builds and all four test tiers
- Restart the app and verify it came up (NET_AGENTS: restart and verify after every code change)
- Commit locally on `master`, one commit per completed task

**Ask first**
- Any change outside the file manifest
- Adding a NuGet package
- Touching the comic pipeline, auth, rate limiting, or storage initialization
- Running `SCRIPTS/ui-check.mjs` — it performs one real comic generation and spends a paid image call
- Changing an existing test's assertions

**Never**
- `git push` (NET_AGENTS: never push unless asked)
- Deploy, or trigger `.github/workflows/deploy.yml`
- `dotnet user-secrets` (NET_AGENTS), or committing any secret
- Add a third item to `nav.nav-links`
- Add a Google Maps or AI call to this feature
- Delete or migrate existing table data
- Work on a branch other than `master`

---

## 13. Out of scope

- Per-restaurant review charts on the comic page
- Region or week filters, drill-down, click-through, CSV export
- Sentiment analysis, keyword extraction, topic modelling, word clouds
- A map or geographic heatmap (coordinates are stored but unused here)
- Any change to the comic pipeline, strangeness scoring, Hall of Fame, PWA, or graphics/audio layer
- Precomputed/materialized aggregates or a scheduled rollup job
- Deploying, or adding these tests to CI (the tiered suites deliberately do not run in CI)

---

## 14. Edge cases and error states

| Case | Behaviour |
|---|---|
| Zero Hall of Fame rows | Single page-level empty state; 200 with zero counts, not 500 |
| Hall of Fame row, no matching restaurant row | Kept in distribution/region/weekly; excluded from scatter |
| Restaurant row stale past cache window | Used anyway; `CachedAt` is not consulted |
| Score exactly 0 or exactly 100 | Lands in `[0,10)` and `[90,100]` respectively |
| Star rating 0 or absent | Excluded from the scatter, counted elsewhere |
| `TotalReviews` = 0 | Bubble clamped to a minimum visible radius, not radius 0 |
| Single region | Region chart shows its empty state (minimum 2) |
| Weeks with a gap | Missing weeks omitted, never zero-filled |
| Malformed week key | `WeekStartFor` returns `MinValue`; row dropped from weekly trend, logged |
| Row cap hit | `Truncated: true`; page shows a "based on the most recent 5000 rows" note |
| Storage unreachable | 500 ProblemDetails; page shows shared `.alert` + retry |
| Anonymous request | 401 from the fallback policy; client `[Authorize]` redirects to login |
| 320px viewport | No horizontal overflow; charts stack to one column |
| Dark mode | Every chart colour resolves from a token; contrast tests pass |

---

## 15. Success criteria

Each is measurable and will be proven with command output or a screenshot in Phase 5.

1. `dotnet build PoSeeReview.sln` succeeds with **zero warnings** (`TreatWarningsAsErrors` is on,
   so this is binary).
2. `GET /api/insights` returns **401** without a session and **200** with one.
3. The 200 response deserializes into `InsightsDto` through the source-generated `AppJsonContext`
   with no reflection fallback.
4. `/insights` renders **four** chart regions when data clears every minimum.
5. Each chart independently renders its empty state when below its minimum, stating the required
   and current counts.
6. With mock data active, all four charts render and the "USING MOCK DATA" banner is visible.
7. `nav.nav-links` still contains **exactly two** `.nav-item` elements; `HeaderContractUiTests`
   passes unchanged.
8. `/insights` has **no horizontal overflow** at 320px and 390px viewport widths.
9. `ColorContrastTests` passes with any new tokens, against every surface token.
10. All four test tiers pass: Unit, Integration, E2EAPI, E2EUI.
11. `InsightsService` aggregation has ≥90% line coverage.
12. The feature issues **zero** Google Maps and **zero** AI calls — proven by the absence of any
    such client in the Insights slice and by the unchanged generation-budget counters.
13. `Features/Insights` references **no other slice** — provable by its `using` declarations.
14. The app starts cleanly (`dotnet run --project src/PoSeeReview.Api --launch-profile https`) and
    `/health` returns healthy after the change.
15. Every existing test that passed before this work still passes.

---

## 16. Open questions

1. **Session-zone label.** `.nav-mycomics` uses an emoji + label ("🗂️ My Comics"). Insights will
   mirror it ("📊 Insights") unless you prefer something else. Cosmetic; I will proceed with 📊.
2. **Row cap default.** 5000 is a guess at "generous but bounded". Tunable via `InsightsOptions`
   with no code change.
3. **Top-bucket labelling.** The distribution's last bucket is `[90,100]` and will be labelled
   "90–100" while others read "0–9", "10–19". Slight asymmetry, deliberate, avoids losing 100.
