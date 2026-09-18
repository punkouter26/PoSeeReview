# tasks/todo.md — Implementation Checklist

Strict blast-radius tasks (≤5 files per task). Every task enforces: Red → Green → Full Suite → Build → Commit.

---

### Task 1: Hall of Fame RadzenDataGrid Integration
- **Description:** Replace manual div list in `Leaderboard.razor` with `RadzenDataGrid<LeaderboardEntryDto>` featuring sorting, filtering, paging, and custom column templates for ranks, medals, restaurant info, and strangeness badges.
- **Dependencies:** None
- **File Manifest (Max 5 files):**
  1. `src/PoSeeReview.Client/Pages/Leaderboard.razor`
  2. `src/PoSeeReview.Client/Pages/Leaderboard.razor.css`
  3. `src/PoSeeReview.Client/Layout/NavMenu.razor`
- **Acceptance Criteria:**
  - `RadzenDataGrid` displays ranked entries with page size 10 and sorting enabled.
  - Medals (Gold, Silver, Bronze) and Strangeness score badges render cleanly.
  - Empty state and loading states render with appropriate ARIA roles.
  - `.leaderboard-container` selector remains present for E2E tests.
- **Verification Command:**
  ```powershell
  dotnet build src/PoSeeReview.Client/PoSeeReview.Client.csproj
  ```

---

### Task 2: Comic Generation Stream RadzenSteps & Stepper Integration
- **Description:** Modernize the generation stream in `LoadingIndicator.razor` and `ComicView.razor` to use `RadzenSteps` / `RadzenTimeline` reactive to SSE event phases (`Fetching reviews` → `Analyzing strangeness` → `Writing narrative` → `Generating artwork` → `Composing strip`).
- **Dependencies:** Task 1
- **File Manifest (Max 5 files):**
  1. `src/PoSeeReview.Client/Components/LoadingIndicator.razor`
  2. `src/PoSeeReview.Client/Components/LoadingIndicator.razor.css`
  3. `src/PoSeeReview.Client/Pages/ComicView.razor`
  4. `src/PoSeeReview.Client/Pages/ComicView.razor.css`
- **Acceptance Criteria:**
  - `RadzenSteps` tracks the active step delivered via `ActiveStep` parameter from SSE stream.
  - Completed steps display checkmarks; current step shows indeterminate spinner or active badge.
  - Error state stops animation and shows failure alert with retry option.
- **Verification Command:**
  ```powershell
  dotnet test tests/PoSeeReview.Unit --filter "FullyQualifiedName~Comic"
  ```

---

### Task 3: Restaurant Discovery RadzenCard & Rating System
- **Description:** Modernize restaurant cards on `Index.razor` and `RestaurantCard.razor` using `RadzenCard`, `RadzenRating`, `RadzenBadge`, and `RadzenSelectBar` for sorting.
- **Dependencies:** Task 2
- **File Manifest (Max 5 files):**
  1. `src/PoSeeReview.Client/Components/RestaurantCard.razor`
  2. `src/PoSeeReview.Client/Components/RestaurantCard.razor.css`
  3. `src/PoSeeReview.Client/Pages/Index.razor`
  4. `src/PoSeeReview.Client/Pages/Index.razor.css`
- **Acceptance Criteria:**
  - Restaurant cards render inside `RadzenCard` with custom styling.
  - Star ratings display using read-only `RadzenRating`.
  - Proximity and price badges display using `RadzenBadge`.
  - "Generate Comic" button utilizes `RadzenButton`.
- **Verification Command:**
  ```powershell
  dotnet build src/PoSeeReview.Client/PoSeeReview.Client.csproj
  ```

---

### Task 4: Insights RadzenChart Theme Polish
- **Description:** Standardize chart styling in `Insights.razor` to leverage unified Radzen color schemes and design tokens.
- **Dependencies:** Task 3
- **File Manifest (Max 5 files):**
  1. `src/PoSeeReview.Client/Pages/Insights.razor`
  2. `src/PoSeeReview.Client/Pages/Insights.razor.css`
- **Acceptance Criteria:**
  - Scatter, column, and regional comparison charts render cleanly with Radzen theme tokens.
  - Sonification / audio playback button remains functional.
  - Mock and empty states render gracefully.
- **Verification Command:**
  ```powershell
  dotnet build src/PoSeeReview.Client/PoSeeReview.Client.csproj
  ```

---

### Task 5: Ponytail Cleanup & Dead Code Pruning
- **Description:** Apply Ponytail minimalist guidelines to prune obsolete CSS classes and redundant JS shims rendered unnecessary by native Radzen functionality.
- **Dependencies:** Task 4
- **File Manifest (Max 5 files):**
  1. `src/PoSeeReview.Client/wwwroot/css/app.css`
  2. `src/PoSeeReview.Client/Components/ComicActions.razor`
- **Acceptance Criteria:**
  - Redundant CSS rules for deleted custom tables/steppers pruned.
  - Zero unused style declarations conflicting with Radzen components.
  - No broken layout classes.
- **Verification Command:**
  ```powershell
  dotnet build PoSeeReview.sln
  ```

---

### Task 6: Full Verification Suite
- **Description:** Run the complete tiered test suite to guarantee zero regressions.
- **Dependencies:** Task 5
- **File Manifest (Max 5 files):**
  1. `tests/PoSeeReview.Unit/*`
  2. `tests/PoSeeReview.E2EAPI/*`
- **Acceptance Criteria:**
  - Zero build warnings/errors with `TreatWarningsAsErrors`.
  - All unit tests pass.
  - All E2E API tests pass.
- **Verification Command:**
  ```powershell
  dotnet test tests/PoSeeReview.Unit
  dotnet test tests/PoSeeReview.E2EAPI
  ```

