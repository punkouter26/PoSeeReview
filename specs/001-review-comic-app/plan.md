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

- [x] .NET 9.0 SDK enforced (build fails on version mismatch)
- [x] Vertical Slice Architecture planned; no file exceeds 500 lines
- [x] TDD workflow defined (xUnit unit + integration tests, manual Playwright E2E)
- [x] API exposes Swagger, /api/health, Problem Details middleware, Serilog logging
- [x] Azure Table Storage default (Azurite local); tables follow PoSeeReview[Name] pattern
- [x] Blazor uses built-in components (Radzen only if UX-justified)
- [x] Mobile-first responsive design validated (desktop + narrow emulation)
- [x] Repository follows /src, /tests, /docs, /scripts layout
- [x] Project names follow Po.SeeReview.* pattern
- [x] API binds to HTTP 5000 and HTTPS 5001 only
- [x] dotnet format enforced; CI validates SDK version, ports, naming, health endpoint
- [x] Operations use one-line CLI commands; docs in /docs only

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
