# SPEC.md — PoSeeReview Modernization & Radzen Integration

## 1. Objective
Modernize the **PoSeeReview** user experience by embracing **Radzen Blazor** controls across the core user journeys (Hall of Fame leaderboard, comic generation progress stream, restaurant discovery cards, and insights analytics), while enforcing the **Ponytail** minimal-code ladder ([DietrichGebert/ponytail](https://github.com/DietrichGebert/ponytail)) to eliminate over-engineering, prune bespoke CSS/JS, and preserve rock-solid system performance and test coverage.

---

## 2. User Journeys

1. **Restaurant Discovery:**
   - User grants geolocation or accesses default location.
   - Proximity cards are presented using `RadzenCard` with `RadzenRating` (star rating), `RadzenBadge` (distance, price tier, cuisine), and `RadzenSelectBar` / `RadzenDropDown` for sorting by distance or rating.
   - Tapping "Generate Comic" initiates the AI pipeline and transitions to the comic generation view.

2. **Real-time Comic Generation:**
   - User observes the 5-phase generation process (`Fetching reviews` → `Analyzing strangeness` → `Writing narrative` → `Generating artwork` → `Composing strip`).
   - Progress is displayed via `RadzenSteps` / `RadzenTimeline` reflecting live Server-Sent Events (SSE) from `POST /api/comics/{placeId}/stream`.
   - Each phase displays its icon, title, and current state (pending, in-progress with spinner, or completed checkmark).

3. **Comic Viewer & Audio Skits:**
   - Once completed, user views the rendered 4-panel comic strip, strangeness score badge, and review narrative.
   - User can play the generated audio skit with audio controls, copy a share link, or save the comic to their collection.

4. **Hall of Fame Leaderboard:**
   - User navigates to the Hall of Fame.
   - Ranked entries load into an interactive `RadzenDataGrid` featuring sorting (by Rank, Strangeness, Restaurant Name), filtering, paging, and responsive column density.
   - Custom column templates render rank medals (gold, silver, bronze), strangeness badges, restaurant details, and quick view/share actions.
   - Clean empty state and refresh button.

5. **Insights & Analytics:**
   - User views aggregate statistics powered by `RadzenChart` (strangeness score distribution column series, strangeness vs. rating bubble series, regional comparisons) with audio sonification support.

---

## 3. Pinned Tech Stack & Versions

- **Runtime & SDK:** .NET 10.0 (pinned via `global.json`)
- **Frontend Framework:** Blazor WebAssembly 10.0.7
- **UI Library:** `Radzen.Blazor` 11.2.7 (centrally pinned in `Directory.Packages.props`)
- **Backend API:** ASP.NET Core 10 BFF host with Minimal APIs and vertical slices
- **Data Storage:** Azure Table Storage (`Azure.Data.Tables` 12.11.0) & Azure Blob Storage (`Azure.Storage.Blobs` 12.27.0), Azurite 4.14 for local dev
- **AI Services:** Azure OpenAI GPT-4o-mini (`Azure.AI.OpenAI` 2.1.0) / Ollama for chat & scoring; Google Gemini Imagen 4 / HuggingFace Flux for image generation
- **Minimal Code Engine:** Ponytail (`DietrichGebert/ponytail`) decision ladder
- **Testing:** xUnit 2.9.3, Moq 4.20.72, Testcontainers 4.14.0, Microsoft.Playwright 1.49.0

---

## 4. Build, Test, Lint, and Run Commands

```powershell
# Restore dependencies
dotnet restore

# Build solution (enforces TreatWarningsAsErrors)
dotnet build PoSeeReview.sln

# Run unit tests
dotnet test tests/PoSeeReview.Unit

# Run integration tests (requires Docker Azurite container)
dotnet test tests/PoSeeReview.Integration

# Run E2E API tests (in-memory WebApplicationFactory with mock AI)
dotnet test tests/PoSeeReview.E2EAPI

# Run local development host (HTTP 5000 / HTTPS 5001)
dotnet run --project src/PoSeeReview.Api --launch-profile https
```

---

## 5. Project Structure

```
PoSeeReview/
├── Directory.Build.props            # Global compiler options, TreatWarningsAsErrors, Nullable
├── Directory.Packages.props         # Central Package Management (CPM) pins
├── global.json                      # Pinned .NET 10 SDK
├── CAPABILITY-MAP.md                # Solution architecture & module responsibilities
├── SPEC.md                          # This specification
├── src/
│   ├── PoSeeReview.Shared/          # Domain primitive IDs, wire DTOs, Enums, Contracts
│   ├── PoSeeReview.Client/          # Blazor WASM, Radzen components, Pages, Styles
│   │   ├── Components/              # RestaurantCard, LoadingIndicator, ComicStrip, PageShell
│   │   ├── Pages/                   # Index, ComicView, Leaderboard, Insights, Diagnostics
│   │   └── wwwroot/                 # Design system CSS, JS interop
│   └── PoSeeReview.Api/             # ASP.NET Core 10 BFF Host & Vertical Slices
│       ├── Features/                # Comics, Restaurants, Leaderboard, Insights, Moderation
│       ├── Storage/                 # TableStorageRepository, BlobStorageService
│       └── Program.cs               # Pipeline configuration
└── tests/
    ├── PoSeeReview.Unit/            # Fast domain, contract, and utility unit tests
    ├── PoSeeReview.Integration/     # Azurite Table & Blob storage integration tests
    ├── PoSeeReview.E2EAPI/          # In-memory API endpoint tests
    └── PoSeeReview.E2EUI/           # Playwright browser end-to-end tests
```

---

## 6. Code Style & Conventions

- **C# 13 / .NET 10:** File-scoped namespaces, nullable reference types, pattern matching, records for DTOs and value objects.
- **Radzen First:** Use Radzen components for UI primitives (data tables, step indicators, cards, badges, buttons). Avoid manual HTML/CSS workarounds when a Radzen property or event callback exists.
- **Ponytail Rule:**
  ```csharp
  // Ponytail: Use RadzenDataGrid's native paging, sorting, and responsive column templates
  // instead of hand-crafting custom pagination math or DOM event handlers.
  <RadzenDataGrid AllowPaging="true" PageSize="10" AllowSorting="true"
                  Data="@_entries" TItem="LeaderboardEntryDto" Responsive="true">
      <Columns>
          <RadzenDataGridColumn Property="@nameof(LeaderboardEntryDto.Rank)" Title="Rank" Width="70px">
              <Template Context="entry">
                  <span class="medal">@GetMedal(entry.Rank)</span>
              </Template>
          </RadzenDataGridColumn>
          <RadzenDataGridColumn Property="@nameof(LeaderboardEntryDto.RestaurantName)" Title="Restaurant" />
          <RadzenDataGridColumn Property="@nameof(LeaderboardEntryDto.StrangenessScore)" Title="Strangeness" Width="120px">
              <Template Context="entry">
                  <RadzenBadge BadgeStyle="BadgeStyle.Primary" Text="@($"{entry.StrangenessScore}/100")" />
              </Template>
          </RadzenDataGridColumn>
      </Columns>
  </RadzenDataGrid>
  ```
- **Cascade Layer Discipline:** Keep vendor CSS in `@layer vendor`, design tokens in `@layer tokens`, shared primitives in `@layer shared`.

---

## 7. Testing Strategy & Quotas

- **Framework:** xUnit with Coverlet collector.
- **Tiers & Quotas:**
  - `PoSeeReview.Unit`: Fast pure tests (domain models, DTO mappings, calculations).
  - `PoSeeReview.Integration`: Azurite storage tests ensuring Table/Blob operations work.
  - `PoSeeReview.E2EAPI`: HTTP endpoints tested end-to-end using `CustomWebApplicationFactory` and `AiMockDelegatingHandler`.
  - `PoSeeReview.E2EUI`: C# Playwright testing full browser flows (Discovery, Leaderboard, Comic Generation).
- **Enforcement:** No failing tests allowed; zero tests deleted or weakened.

---

## 8. Boundaries

- **Always:**
  - Verify every code change against the Ponytail 7-step decision ladder before writing code.
  - Fully embrace `Radzen.Blazor` controls and styles.
  - Preserve backward-compatible API contracts and table schemas.
  - Keep secrets in environment variables or `dotnet user-secrets`.
- **Ask First:**
  - Adding new third-party NuGet packages.
  - Altering existing public API route signatures.
  - Significant layout changes that alter user navigation pathways.
- **Never:**
  - Commit secrets, tokens, or live API keys to git.
  - Mock away business rules or validation logic in production paths.
  - Introduce bloated JavaScript dependencies when Radzen or browser standards provide the capability.

---

## 9. Out-of-Scope Items

- Rewriting existing AI prompt engineering or image generation pipelines.
- Replacing Table Storage with relational SQL databases.
- Replacing the BFF cookie authentication architecture with token-in-browser flows.

---

## 10. Edge Cases & Error Handling

- **Empty State:** When no restaurants or leaderboard entries exist, display a clean Radzen card state with an actionable redirect or refresh button.
- **Generation SSE Interruptions:** If the SSE stream terminates prematurely or errors, `RadzenSteps` updates the active step to an error state with a clear user retry option without incurring duplicate costs.
- **Screen Resizing:** `RadzenDataGrid` and `RadzenCard` grids adapt gracefully across mobile viewports (320px - 768px) and desktop (>1024px).

---

## 11. Numbered Measurable Success Criteria

1. **Zero Compiler Warnings:** `dotnet build PoSeeReview.sln` completes with 0 warnings and 0 errors under `TreatWarningsAsErrors`.
2. **Leaderboard Grid:** `Pages/Leaderboard.razor` successfully renders ranked entries using `RadzenDataGrid` with sorting, filtering, and paging.
3. **Stream Progress Stepper:** `Pages/ComicView.razor` and `Components/LoadingIndicator.razor` render the 5 generation phases using `RadzenSteps` / `RadzenTimeline` reactive to server-sent events.
4. **Discovery Cards:** `Pages/Index.razor` and `Components/RestaurantCard.razor` utilize `RadzenCard`, `RadzenRating`, and `RadzenBadge`.
5. **Ponytail Minimal Code Verification:** Unnecessary custom CSS/JS rules rendered redundant by Radzen components are pruned, reducing client codebase bloat.
6. **Test Suite Integrity:** All Unit, Integration, and E2EAPI test suites pass cleanly.

---

## 12. Open Questions

*(All initial questions resolved in Phase 0 interview; no blockers remain.)*

