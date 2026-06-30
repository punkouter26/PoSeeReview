# PoSeeReview

PoSeeReview turns unusual restaurant reviews into AI-generated four-panel comic strips and publishes ranked results to a global Hall of Fame leaderboard. The solution runs as a Blazor WebAssembly frontend and .NET 10 API in a single Azure App Service, backed by Azure Table and Blob Storage, Azure OpenAI, and Google Gemini for narrative and image generation.

---

## Product Requirements Document (PRD)

### Vision

Every neighborhood has restaurants with bizarre, unhinged, or strangely poetic Google Maps reviews. PoSeeReview mines that raw material, distills the strangest signal from real reviews, and turns it into shareable four-panel comic strips. The output is ranked on a global leaderboard by a computed "strangeness score" (0–100), creating a continuously refreshing artifact of collective absurdity.

The product has a single core loop: open app → share location → browse nearby restaurants → tap one → watch the AI pipeline run → receive a personalized comic strip → share it or check your restaurant's global rank. This loop must complete in under 12 seconds on a cold miss, and under 500ms on a cache hit.

### Problem

Restaurant review platforms index enormous volumes of user-generated content that is simultaneously hyper-local and culturally rich. Yet none of it is presented in a format optimized for entertainment, sharing, or rediscovery. Reviews are read once and forgotten. PoSeeReview recovers this latent value and transforms it into a new, ephemeral medium — the comic strip — that is inherently shareable and globally comparable.

The problem is not a lack of content; it is a lack of a lens that makes that content arresting. PoSeeReview is that lens.

### Target Users

**Primary persona — Curious Local:** A smartphone user who opens the app while waiting for food, while exploring a new neighborhood, or while bored. They want to be entertained by something real and local. They are not looking for utility — they are looking for surprise. The friction to the first surprise must be near-zero. No registration, no onboarding form, no paywall.

**Secondary persona — Restaurant Obsessive:** A food blogger, local guide contributor, or review-reading enthusiast who already consumes review content as entertainment. They engage deeply with the strangeness score and leaderboard, returning to see if their favorite weird restaurant has risen in global rank.

**Tertiary persona — Content Creator:** A person who shares the comic to social media. They need the share action to work with one tap and to produce a clean, standalone image.

### Core Features

**1. Proximity-first discovery.** The user grants location access and immediately sees a grid of restaurants within 5 km, sorted by distance. No search box, no category filters — the intent is serendipity, not utility. Manual location search is available as a fallback when geolocation is denied.

**2. One-tap comic generation.** A single tap on a restaurant card initiates the AI pipeline. The loading state is presented as numbered steps (Fetching reviews → Analyzing strangeness → Writing narrative → Generating artwork → Composing strip) so users understand the system is working rather than broken. The generation target is under 10 seconds.

**3. Strangeness scoring.** Azure OpenAI (GPT-4o-mini, temperature 0.3) reads the top reviews and returns a strangeness score (0–100) along with a one-paragraph narrative. The scoring model prioritizes one-star reviews as the highest-signal source. If no reviews of sufficient quality exist, the API returns a 422 with a clear user-facing message rather than generating a low-quality output.

**4. Comic image generation.** Google Gemini Imagen 4 renders the four-panel comic as a PNG. The `ComicTextOverlayService` then stamps the strangeness score and narrative directly onto the image, producing a single self-contained artifact. The image is stored in Azure Blob Storage with an 8-day SAS URL (outliving the 24-hour comic cache).

**5. Global leaderboard.** Every generated comic is projected to a regional leaderboard (`PoSeeReviewLeaderboard` table) using an inverted RowKey pattern for zero-cost descending sort. The leaderboard is queryable by ISO region code (e.g., `US`, `GB`, `US-CA-SF`) and returns the top N entries sorted by strangeness descending.

**6. Content takedown.** Restaurant owners or legal representatives can request removal of a specific comic via `POST /api/takedowns` authenticated with an `X-Api-Key` header. The controller atomically deletes the comic table row, the blob asset, and the leaderboard entry. Telemetry events are emitted for moderation audit trails.

**7. Ephemeral by design.** Comics expire after 24 hours. The `ExpiredComicCleanupService` runs on a 30-minute background loop and batch-purges expired rows and blobs (batch size 25, configurable 5–200). This keeps storage costs bounded and the leaderboard fresh. Users who want to regenerate a comic can force-refresh at any time.

### Non-Goals (v1)

- No user accounts, saved comics, or personalized history.
- No moderation queue (takedown is direct delete, not review-and-approve).
- No real-time collaborative features beyond optimistic concurrency on the same restaurant.
- No mobile native app — Blazor WASM in the browser is the delivery target.
- No paid tier or premium features.

### Technical Constraints

- **Latency target:** p50 cold-miss generation < 10s. p95 cache hit < 500ms.
- **Caching:** Comics and restaurant data cache for 24 hours in Azure Table Storage. SAS URLs are issued for 8 days to avoid stale URL errors within the cache window.
- **Rate limiting:** The comic generation endpoint (`POST /api/comics/{placeId}`) is protected by a named rate limit policy to prevent quota exhaustion on external AI providers.
- **Bot protection:** All requests are evaluated by `UserAgentValidationMiddleware`. Requests with missing or blocked User-Agent patterns are rejected at 400 before hitting the application layer.
- **No authentication required for read operations.** All read endpoints (nearby restaurants, leaderboard, comic by ID) are public. Generation endpoints are public but rate-limited. Only `POST /api/takedowns` requires a credential.
- **Security:** All secrets are stored in Azure Key Vault and resolved at startup via Managed Identity. No credentials appear in `appsettings.json` or environment variables in production.

### Success Metrics

- Time-to-first-comic for a new user < 30 seconds end-to-end.
- Cache hit rate > 40% for restaurants in active urban areas.
- Zero 5xx errors due to unhandled exceptions (all paths covered by `GlobalExceptionHandler`).
- Leaderboard latency < 200ms p99.
- Takedown SLA < 5 minutes from submission to artifact removal.

---

## Architecture Overview

| Layer | Technologies |
|---|---|
| Edge Delivery | Browser (Blazor WASM), Azure App Service host |
| Compute | ASP.NET Core 10 API, `ExpiredComicCleanupService` |
| Data | Azure Table Storage, Azure Blob Storage, Azure Key Vault |
| External AI | Azure OpenAI GPT-4o-mini, Google Gemini Imagen 4, Google Maps Places |
| Observability | Application Insights, OpenTelemetry, Serilog |

## Documentation Suite

All diagrams are self-contained HTML files rendered by Mermaid.js with the cathrynlavery/diagram-design aesthetic. Open [docs/index.html](./docs/index.html) for the full gallery.

### 1 — Architecture & CI/CD Strategy

| Diagram | Full | Simple |
|---|---|---|
| Architecture Master (C4 Hybrid L1/L2) | [docs/Architecture_MASTER.html](./docs/Architecture_MASTER.html) | [docs/Architecture_MASTER_SIMPLE.html](./docs/Architecture_MASTER_SIMPLE.html) |
| Release Pipeline Master | [docs/ReleasePipeline_MASTER.html](./docs/ReleasePipeline_MASTER.html) | [docs/ReleasePipeline_MASTER_SIMPLE.html](./docs/ReleasePipeline_MASTER_SIMPLE.html) |

### 2 — User Usage & Behavioral Flowcharts

| Diagram | Full | Simple |
|---|---|---|
| Onboarding Journey | [docs/OnboardingJourney.html](./docs/OnboardingJourney.html) | [docs/OnboardingJourney_SIMPLE.html](./docs/OnboardingJourney_SIMPLE.html) |
| Primary Value Flow (Happy Path) | [docs/PrimaryValueFlow.html](./docs/PrimaryValueFlow.html) | [docs/PrimaryValueFlow_SIMPLE.html](./docs/PrimaryValueFlow_SIMPLE.html) |
| Exception User Flows | [docs/ExceptionUserFlows.html](./docs/ExceptionUserFlows.html) | [docs/ExceptionUserFlows_SIMPLE.html](./docs/ExceptionUserFlows_SIMPLE.html) |

### 3 — Logic & State Dynamics

| Diagram | Full | Simple |
|---|---|---|
| System Flow Master | [docs/SystemFlow_MASTER.html](./docs/SystemFlow_MASTER.html) | [docs/SystemFlow_MASTER_SIMPLE.html](./docs/SystemFlow_MASTER_SIMPLE.html) |
| State Dynamics Master | [docs/StateDynamics_MASTER.html](./docs/StateDynamics_MASTER.html) | [docs/StateDynamics_MASTER_SIMPLE.html](./docs/StateDynamics_MASTER_SIMPLE.html) |

### 4 — Data & Security Schema

| Diagram | Full | Simple |
|---|---|---|
| Data Model (ERD) | [docs/DataModel.html](./docs/DataModel.html) | [docs/DataModel_SIMPLE.html](./docs/DataModel_SIMPLE.html) |
| Access Control Matrix | [docs/AccessControl_MATRIX.html](./docs/AccessControl_MATRIX.html) | [docs/AccessControl_MATRIX_SIMPLE.html](./docs/AccessControl_MATRIX_SIMPLE.html) |
| Data Lifecycle Master | [docs/DataLifecycle_MASTER.html](./docs/DataLifecycle_MASTER.html) | [docs/DataLifecycle_MASTER_SIMPLE.html](./docs/DataLifecycle_MASTER_SIMPLE.html) |

### 5 — Dependency & UI Hierarchy

| Diagram | Full | Simple |
|---|---|---|
| System Interaction Flow (Sequence) | [docs/SystemInteractionFlow.html](./docs/SystemInteractionFlow.html) | [docs/SystemInteractionFlow_SIMPLE.html](./docs/SystemInteractionFlow_SIMPLE.html) |
| Service Map Master | [docs/ServiceMap_MASTER.html](./docs/ServiceMap_MASTER.html) | [docs/ServiceMap_MASTER_SIMPLE.html](./docs/ServiceMap_MASTER_SIMPLE.html) |
| Interface Hierarchy Master | [docs/InterfaceHierarchy_MASTER.html](./docs/InterfaceHierarchy_MASTER.html) | [docs/InterfaceHierarchy_MASTER_SIMPLE.html](./docs/InterfaceHierarchy_MASTER_SIMPLE.html) |

### Visual assets

- [docs/screenshots](./docs/screenshots) — reserved for UI screenshots and flow captures.

## Refactor Blast Radius Assessment

### Refactor: Replace direct provider clients with a single orchestration service

- Purpose: centralize Google Maps, LLM, and image-model execution with explicit retries, fallback policy, and correlation IDs.
- Blast radius:
  - API layer: changes to service registration and endpoint call paths.
  - Infrastructure layer: adapter wrappers around existing provider SDK clients.
  - Telemetry: metric names and traces may shift to orchestration-centric spans.
  - Tests: unit/integration tests mocking provider dependencies must be updated.
  - Downstream dependencies: provider quota behavior and timeout strategy become coupled to orchestrator policy.

### Refactor: Introduce projection worker for leaderboard materialization

- Purpose: decouple ranking updates from request path to reduce p95 latency.
- Blast radius:
  - API layer: request handlers move from synchronous projection writes to enqueue-only behavior.
  - Data layer: adds queue/table checkpoint records and idempotency keys.
  - UI layer: leaderboard eventual-consistency window must be communicated.
  - Operations: requires new health checks and backlog alerts.
  - Downstream dependencies: consumers reading immediate ranks may observe delayed updates.

### Refactor: Add versioned state machine for comic lifecycle

- Purpose: make status transitions explicit and enforceable across create, publish, expire, and takedown flows.
- Blast radius:
  - Core model: enum/state transition rules and validation constraints change.
  - API contracts: response fields may include state version and transition metadata.
  - Storage schema: migration required for lifecycle version and transition audit fields.
  - Background jobs: cleanup/takedown workers must honor state-machine gates.
  - Downstream dependencies: reporting and moderation tools must map to new state values.

### Refactor: Introduce anti-corruption DTO boundary between API and shared client contracts

- Purpose: reduce accidental coupling between persistence entities and external payloads.
- Blast radius:
  - API endpoints: mapping logic added for read/write DTO translations.
  - Client app: contract changes may require adapter updates in typed services.
  - Tests: snapshot and serialization tests need baseline refresh.
  - Performance: additional mapping allocations may require profiling.
  - Downstream dependencies: any external consumer of existing DTO shape may need migration coordination.

## Build and Run

```powershell
dotnet restore
dotnet build PoSeeReview.sln
dotnet run --project src/PoSeeReview.Api --launch-profile https
```

### Hot Reload (code-only changes)

Configuration changes (appsettings, Key Vault secrets) require a full API restart. For code-only changes use hot reload to avoid the full startup delay:

```powershell
dotnet watch --project src/PoSeeReview.Api --launch-profile https
```

## Cloud Smoke Testing

After deploying with `azd up`, discover the live URL:

```powershell
# Show all environment values including the deployed URL
azd env show

# Or list Container App endpoints via Azure CLI
az containerapp list --query "[].{name:name, fqdn:properties.configuration.ingress.fqdn}" -o table
```

The base URL will be printed as `SERVICE_WEB_URL` or `SERVICE_API_URL` in the `azd env show` output. Visit `https://<fqdn>/health` to verify the deployment is healthy before running the Playwright smoke suite:

```powershell
# Run smoke tests against the deployed environment
$env:PLAYWRIGHT_BASE_URL = "https://<fqdn>"
npx playwright test --grep @smoke
```

