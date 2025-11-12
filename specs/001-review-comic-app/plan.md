# Implementation Plan: PoSeeReview - Review-to-Comic Storytelling App

**Branch**: `001-review-comic-app` | **Date**: 2025-10-27 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/001-review-comic-app/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. See `.specify/templates/commands/plan.md` for the execution workflow.

## Summary

PoSeeReview transforms real restaurant reviews from Google Maps into surreal four-panel comic strips. Users discover nearby restaurants via browser geolocation, select a venue, and the system scrapes reviews, analyzes them for strangeness using Azure OpenAI, generates a narrative paragraph and comic strip via DALL·E 3, and displays the result with a strangeness score. Comics are cached for 24 hours, stored in Azure Blob Storage, and featured on a global leaderboard. The app is fully anonymous, responsive, and built with Blazor WebAssembly hosted within a .NET Core Web API backend following Onion Architecture with Azure Table Storage for persistence.

## Technical Context

**Language/Version**: C# / .NET 9.0 SDK (latest patch)  
**Primary Dependencies**: ASP.NET Core 9.0, Blazor WebAssembly, Azure.Data.Tables, Azure.Storage.Blobs, Azure.AI.OpenAI  
**Storage**: Azure Table Storage (Azurite for local development), Azure Blob Storage for comic images  
**Testing**: xUnit for unit and integration tests, Playwright MCP with TypeScript for manual E2E  
**Target Platform**: Responsive web application (mobile browsers primary, desktop secondary)  
**Project Type**: Web application (Blazor WASM frontend + ASP.NET Core API backend)  
**Performance Goals**: <3s restaurant discovery, <10s comic generation, <1s leaderboard load, <500ms API responses  
**Constraints**: Anonymous (no authentication), 24-hour comic cache per restaurant, 5-review minimum for generation, HTTP 5000 / HTTPS 5001 ports  
**Scale/Scope**: Support 1000+ concurrent users, handle restaurants with 5-500+ reviews, maintain top-10 global leaderboard with real-time updates

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### 1. Foundation
- [x] Solution naming: .sln name (PoSeeReview.sln) matches Azure resources and HTML title
- [x] .NET 9: global.json locked to 9.0.xxx SDK, all projects target net9.0
- [ ] Package management: Directory.Packages.props at root, no versions in .csproj **TODO: T136**
- [ ] Null safety: `<Nullable>enable</Nullable>` in all .csproj files **TODO: T137**

### 2. Architecture
- [x] Code organization: Vertical Slice Architecture in /Features/, max 500 lines per file
- [x] Design philosophy: SOLID principles and GoF patterns documented
- [x] API design: Minimal APIs with CQRS pattern for all endpoints
- [x] Repository structure: /src, /tests, /docs, /infra, /scripts layout followed
- [x] Documentation constraint: Only README.md, PRD.md in /docs; diagrams in /docs/diagrams/

### 3. Implementation
- [x] API documentation: Swagger enabled, .http files maintained
- [x] Health checks: /api/health endpoint validates all dependencies
- [x] Error handling: Problem Details middleware, structured logging in catch blocks
- [x] UI framework: FluentUI primary, Radzen only if justified
- [x] Responsive design: Mobile-first, tested on desktop + mobile emulation
- [ ] Debug launch: launch.json with serverReadyAction committed **TODO: Verify exists**
- [x] Local secrets: User Secrets manager for sensitive data
- [x] Local storage: Azurite for Table/Blob emulation, tables follow PoSeeReview[Name]

### 4. Quality & Testing
- [x] Code hygiene: No build warnings/errors, dotnet format enforced
- [ ] Dependency hygiene: Regular package updates via Directory.Packages.props **TODO: T136**
- [x] Workflow: TDD (Red → Green → Refactor) strictly followed
- [x] Test naming: MethodName_StateUnderTest_ExpectedBehavior convention
- [ ] Code coverage: 80% minimum threshold, report in docs/coverage/ **TODO: T138**
- [x] Unit tests: xUnit for business logic, all dependencies mocked
- [ ] Component tests: bUnit for Blazor components **TODO: T139-T141**
- [x] Integration tests: Happy path for all endpoints, Azurite emulator, cleanup
- [x] E2E tests: Playwright with TypeScript, Chromium mobile + desktop, manual execution

### 5. Operations & Azure
- [ ] Provisioning: Bicep in /infra, deployed via azd **TODO: T142-T145**
- [ ] CI/CD: GitHub Actions with OIDC, validates SDK/ports/naming/health **TODO: T129**
- [ ] Required services: App Insights, Log Analytics, App Service, Storage **TODO: T142-T145**
- [ ] Cost management: $5 monthly budget with automated alerts/controls **TODO: T146**
- [x] Logging: Serilog to Debug Console (Dev) and App Insights (Prod)
- [ ] Telemetry: OpenTelemetry with ActivitySource (traces) and Meter (metrics) **TODO: T147-T148**
- [ ] Production diagnostics: Snapshot Debugger and Profiler enabled **TODO: T149**
- [ ] KQL library: Essential queries in docs/kql/ **TODO: T150**

## Project Structure

### Documentation (this feature)

```text
specs/[###-feature]/
├── plan.md              # This file (/speckit.plan command output)
├── research.md          # Phase 0 output (/speckit.plan command)
├── data-model.md        # Phase 1 output (/speckit.plan command)
├── quickstart.md        # Phase 1 output (/speckit.plan command)
├── contracts/           # Phase 1 output (/speckit.plan command)
└── tasks.md             # Phase 2 output (/speckit.tasks command - NOT created by /speckit.plan)
```

### Source Code (repository root)

```text
src/
├── Po.SeeReview.Api/
│   ├── Controllers/
│   │   ├── RestaurantsController.cs
│   │   ├── ComicsController.cs
│   │   └── LeaderboardController.cs
│   ├── Middleware/
│   │   ├── ProblemDetailsMiddleware.cs
│   │   └── RequestLoggingMiddleware.cs
│   ├── Program.cs
│   ├── appsettings.json
│   └── appsettings.Development.json
├── Po.SeeReview.Client/
│   ├── Pages/
│   │   ├── Index.razor
│   │   ├── ComicView.razor
│   │   └── Leaderboard.razor
│   ├── Components/
│   │   ├── RestaurantCard.razor
│   │   ├── ComicStrip.razor
│   │   └── LoadingIndicator.razor
│   ├── Services/
│   │   ├── GeolocationService.cs
│   │   └── ApiClient.cs
│   ├── wwwroot/
│   ├── Program.cs
│   └── App.razor
├── Po.SeeReview.Core/
│   ├── Entities/
│   │   ├── Restaurant.cs
│   │   ├── Comic.cs
│   │   └── LeaderboardEntry.cs
│   ├── Interfaces/
│   │   ├── IRestaurantService.cs
│   │   ├── IComicGenerationService.cs
│   │   ├── IReviewScraperService.cs
│   │   └── ILeaderboardService.cs
│   └── DTOs/
│       ├── RestaurantDto.cs
│       ├── ComicDto.cs
│       └── ReviewDto.cs
├── Po.SeeReview.Infrastructure/
│   ├── Services/
│   │   ├── GoogleMapsService.cs
│   │   ├── AzureOpenAIService.cs
│   │   ├── DalleComicService.cs
│   │   └── BlobStorageService.cs
│   ├── Repositories/
│   │   ├── ComicRepository.cs
│   │   └── LeaderboardRepository.cs
│   └── Configuration/
│       └── AzureStorageOptions.cs
└── Po.SeeReview.Shared/
    ├── Models/
    │   ├── RestaurantModel.cs
    │   ├── ComicModel.cs
    │   └── LeaderboardEntryModel.cs
    └── Constants/
        └── ApiRoutes.cs

tests/
├── Po.SeeReview.UnitTests/
│   ├── Services/
│   │   ├── ComicGenerationServiceTests.cs
│   │   ├── ReviewAnalysisTests.cs
│   │   └── LeaderboardServiceTests.cs
│   └── Repositories/
│       └── ComicRepositoryTests.cs
└── Po.SeeReview.IntegrationTests/
    ├── Api/
    │   ├── RestaurantsEndpointTests.cs
    │   ├── ComicsEndpointTests.cs
    │   └── LeaderboardEndpointTests.cs
    ├── Storage/
    │   ├── TableStorageTests.cs
    │   └── BlobStorageTests.cs
    └── TestFixtures/
        └── AzuriteFixture.cs

docs/
├── PRD.MD
├── STEPS.MD
└── README.MD

scripts/
└── setup-azurite.ps1
```

**Structure Decision**: Web application with Onion Architecture - Po.SeeReview.Core (domain entities/interfaces), Po.SeeReview.Infrastructure (external service implementations), Po.SeeReview.Api (ASP.NET Core Web API backend), Po.SeeReview.Client (Blazor WASM frontend), and Po.SeeReview.Shared (DTOs shared between client/server). This structure supports Vertical Slice Architecture within each project while maintaining clean separation of concerns. Each slice (Restaurant Discovery, Comic Generation, Leaderboard) will have its own controller, service, and repository implementations.
directories captured above]

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| [e.g., 4th project] | [current need] | [why 3 projects insufficient] |
| [e.g., Repository pattern] | [specific problem] | [why direct DB access insufficient] |
