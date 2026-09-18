# Capability Map — PoSeeReview

This document maps all operational capabilities across the solution's modules, enforcing vertical slice isolation, dependency direction, and technology assignments.

```
┌─────────────────────────────────────────────────────────────┐
│                    PoSeeReview.Client                       │
│  (Blazor WASM 10, Radzen.Blazor 11.2, Ponytail minimal code)│
└──────────────────────────────┬──────────────────────────────┘
                               │ HTTP / SSE / BFF Cookie
                               ▼
┌─────────────────────────────────────────────────────────────┐
│                      PoSeeReview.Api                        │
│ (ASP.NET Core 10 BFF Host, Table & Blob Storage, AI Pipes)  │
└──────────────────────────────┬──────────────────────────────┘
                               │ Project Reference
                               ▼
┌─────────────────────────────────────────────────────────────┐
│                     PoSeeReview.Shared                      │
│ (Domain Primitives, Wire DTOs, Enums, Contracts, Validators)│
└─────────────────────────────────────────────────────────────┘
```

---

## 1. Module Capability Matrix

| Capability Area | `PoSeeReview.Shared` | `PoSeeReview.Client` | `PoSeeReview.Api` | `tests/*` |
|---|---|---|---|---|
| **Domain Primitives & Wire Contracts** | `readonly record struct` IDs (`PlaceId`, `ComicId`, `UserId`, `RegionCode`), DTOs, System.Text.Json contexts | Consumes DTOs via `ApiClient` | Maps Entities ↔ DTOs at slice edges | Unit test coverage for serialization/equality |
| **Restaurant Discovery** | `RestaurantDto`, Search/Filter request contracts | `Index.razor`, `RestaurantCard.razor` (`RadzenCard`, `RadzenBadge`, `RadzenRating`, `RadzenSelectBar`) | `Features/Restaurants` (`GoogleMapsService`) | E2E API & Playwright Discovery UI tests |
| **Comic Generation & SSE Streaming** | `ComicDto`, `ComicGenerationEventDto`, `ComicGenerationPhase` enum | `ComicView.razor`, `LoadingIndicator.razor` (`RadzenSteps` / `RadzenTimeline`, `RadzenProgressBarCircular`) | `Features/Comics` (`ComicGenerationService`, SSE endpoint `/stream`) | Stream contract tests & in-memory SSE integration tests |
| **Hall of Fame & Leaderboard** | `LeaderboardEntryDto`, `ILeaderboardRepository`, `ILeaderboardService` | `Leaderboard.razor` (`RadzenDataGrid` with sort, filter, paging, responsive density) | `Features/Leaderboard` (`LeaderboardRepository`, `HallOfFameRepository`) | Unit & Azurite table integration tests |
| **Strangeness Analytics & Insights** | `InsightsSummaryDto`, `ScoreBucketDto` | `Insights.razor` (`RadzenChart` bubble series, column charts, audio playback) | `Features/Insights` (read-only projection over `PoSeeReviewHallOfFame`) | Unit projection tests |
| **Audio Skits & Narrative Playback** | `ComicAudioSkit`, audio manifest contracts | `ComicView.razor` audio playback controls (`RadzenButton`), `audio.js` | Audio skit generation via `IChatCompletionService` | Audio skit serialization contract tests |
| **Content Moderation & Takedowns** | `IContentModerationGate`, `IContentSafetyScreener` | Report comic dialogue, Takedown request view | `Features/Moderation` (`X-Api-Key` authenticated atomicity) | Security tests, moderation gate tests |
| **Anti-Over-Engineering / Minimal Code** | Clean, trim-safe POCOs | Ponytail ladder: prune bespoke CSS/JS, leverage native Radzen capabilities | Minimal vertical slices, no unrequested abstractions | Linting, dead-code checks, test quotas |

---

## 2. Strict Boundary Rules

1. **Client Isolation:** The client never imports Azure SDKs, storage packages, or AI provider libraries. All operations flow through the ASP.NET Core BFF API over HTTP/HTTPS with SameSite cookies.
2. **Shared Purity:** `PoSeeReview.Shared` must remain free of framework dependencies (`Microsoft.AspNetCore.App`, UI libraries). It contains only primitive types, wire contracts, and validation rules.
3. **Slice Independence:** Slices in `PoSeeReview.Api/Features/<Slice>` must never reference one another directly. Inter-slice contracts must live in `PoSeeReview.Shared/Contracts/` or use read-only table projections.
4. **Radzen First UI:** Components must leverage `Radzen.Blazor` components (`RadzenDataGrid`, `RadzenSteps`, `RadzenCard`, `RadzenRating`, `RadzenBadge`, `RadzenChart`) before writing custom CSS/JS.
5. **Ponytail Ladder:** Every change evaluates:
   1. *Does this need to exist?* (YAGNI)
   2. *Already in this codebase?*
   3. *Stdlib / Native platform feature does it?*
   4. *Installed dependency (`Radzen.Blazor`) handles it?*
   5. *One line / minimum code that works.*

