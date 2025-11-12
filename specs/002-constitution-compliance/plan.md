# Implementation Plan: Constitution v2.0.0 Compliance

**Branch**: `002-constitution-compliance` | **Date**: 2025-11-12 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/002-constitution-compliance/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. See `.specify/templates/commands/plan.md` for the execution workflow.

## Summary

This feature implements comprehensive constitution v2.0.0 compliance across all governance areas: foundational infrastructure (Directory.Packages.props, nullable types), quality enforcement (80% coverage target, bUnit testing), operations (Bicep IaC, OpenTelemetry, KQL library), and production diagnostics. The technical approach involves systematic retrofitting of existing codebase with new governance tools and patterns, ensuring all 38 constitution checklist items are satisfied without breaking existing functionality.

## Technical Context

**Language/Version**: C# / .NET 9.0 SDK (locked via global.json)  
**Primary Dependencies**: ASP.NET Core 9.0, Blazor WebAssembly, Azure.Data.Tables, Azure.Storage.Blobs, Azure.AI.OpenAI, OpenTelemetry packages, bUnit, dotnet-coverage  
**Storage**: Azure Table Storage (Azurite local emulation), Azure Blob Storage  
**Testing**: xUnit (unit/integration), bUnit (Blazor components), Playwright (E2E), dotnet-coverage (coverage collection)  
**Target Platform**: Azure App Service (Linux), modern browsers (Chromium-based)  
**Project Type**: Web application (ASP.NET Core API + Blazor WASM client)  
**Performance Goals**: <200ms API response time p95, <2min telemetry ingestion, <5min KQL query execution  
**Constraints**: Build warnings do not fail builds (informational), coverage is tracked but non-blocking, $5 monthly Azure budget  
**Scale/Scope**: Single developer initially, 5 backend projects, 20+ Blazor components, ~10k LOC baseline, 155 total tasks across features

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### 1. Foundation
- [ ] **TODO (T136)**: Package management - Create Directory.Packages.props at root, migrate versions from .csproj files
- [x] Solution naming: PoSeeReview.sln matches Azure resources and HTML title (already compliant)
- [x] .NET 9: global.json exists and locked to 9.0.xxx SDK, all projects target net9.0 (already compliant)
- [ ] **TODO (T137)**: Null safety - Enable `<Nullable>enable</Nullable>` in all .csproj files, create warnings inventory

### 2. Architecture
- [x] Code organization: Vertical Slice Architecture in /Features/, max 500 lines per file (already compliant)
- [x] Design philosophy: SOLID principles and GoF patterns documented (already compliant)
- [x] API design: Minimal APIs with CQRS pattern for all endpoints (already compliant)
- [x] Repository structure: /src, /tests, /docs, /infra, /scripts layout followed (already compliant)
- [ ] **TODO (T152-T153)**: Documentation constraint - Create diagrams in /docs/diagrams/ (c4-context, c4-container, component, sequence)

### 3. Implementation
- [x] API documentation: Swagger enabled, .http files maintained (already compliant)
- [x] Health checks: /api/health endpoint validates all dependencies (already compliant)
- [x] Error handling: Problem Details middleware, structured logging in catch blocks (already compliant)
- [x] UI framework: FluentUI primary, Radzen only if justified (already compliant)
- [x] Responsive design: Mobile-first, tested on desktop + mobile emulation (already compliant)
- [x] Debug launch: launch.json with serverReadyAction committed (already compliant)
- [x] Local secrets: User Secrets manager for sensitive data (already compliant)
- [x] Local storage: Azurite for Table/Blob emulation, tables follow PoAppName[Name] (already compliant)

### 4. Quality & Testing
- [x] Code hygiene: No build warnings/errors, dotnet format enforced (already compliant)
- [ ] **TODO (T136)**: Dependency hygiene - Centralize package updates via Directory.Packages.props
- [x] Workflow: TDD (Red → Green → Refactor) strictly followed (already compliant)
- [x] Test naming: MethodName_StateUnderTest_ExpectedBehavior convention (already compliant)
- [ ] **TODO (T138)**: Code coverage - Configure dotnet-coverage, 80% target threshold, report in docs/coverage/
- [x] Unit tests: xUnit for business logic, all dependencies mocked (already compliant)
- [ ] **TODO (T139-T141)**: Component tests - Install bUnit, create sample tests, establish patterns
- [x] Integration tests: Happy path for all endpoints, Azurite emulator, cleanup (already compliant)
- [x] E2E tests: Playwright with TypeScript, Chromium mobile + desktop, manual execution (already compliant)

### 5. Operations & Azure
- [ ] **TODO (T142-T145)**: Provisioning - Create comprehensive Bicep modules in /infra, deploy via azd
- [x] CI/CD: GitHub Actions with OIDC, validates SDK/ports/naming/health (already compliant)
- [ ] **TODO (T142)**: Required services - Ensure App Insights, Log Analytics, App Service, Storage via Bicep
- [ ] **TODO (T146)**: Cost management - Implement $5 monthly budget with automated alerts
- [x] Logging: Serilog to Debug Console (Dev) and App Insights (Prod) (already compliant)
- [ ] **TODO (T147-T148)**: Telemetry - Implement OpenTelemetry with ActivitySource (traces) and Meter (metrics)
- [ ] **TODO (T149)**: Production diagnostics - Enable Snapshot Debugger and Profiler on App Service
- [ ] **TODO (T150)**: KQL library - Create essential queries in docs/kql/

**Status**: 21/38 items compliant, 17 items require implementation via tasks T136-T155

## Project Structure

### Documentation (this feature)

```text
specs/002-constitution-compliance/
├── spec.md              # Feature specification (complete)
├── plan.md              # This file (/speckit.plan command output)
├── research.md          # Phase 0 output (generated below)
├── data-model.md        # Phase 1 output (generated below)
├── quickstart.md        # Phase 1 output (generated below)
├── contracts/           # Phase 1 output (minimal - this is infrastructure work)
│   └── nullable-warnings-inventory.schema.json
└── checklists/
    └── requirements.md  # Quality validation (complete)
```

### Source Code (repository root)

```text
PoSeeReview/
├── Directory.Packages.props         # NEW: Centralized package management
├── global.json                      # EXISTS: .NET 9.0 SDK lock
├── PoSeeReview.sln                  # EXISTS: Solution file
├── src/
│   ├── Po.SeeReview.Api/            # EXISTS: API project
│   │   ├── Po.SeeReview.Api.csproj  # MODIFY: Enable nullable, remove versions
│   │   ├── Program.cs               # MODIFY: Add OpenTelemetry configuration
│   │   └── Features/                # EXISTS: Vertical slices
│   ├── Po.SeeReview.Client/         # EXISTS: Blazor WASM
│   │   ├── Po.SeeReview.Client.csproj # MODIFY: Enable nullable, remove versions
│   │   └── Components/              # EXISTS: Blazor components
│   ├── Po.SeeReview.Core/           # EXISTS: Domain layer
│   │   └── Po.SeeReview.Core.csproj # MODIFY: Enable nullable, remove versions
│   ├── Po.SeeReview.Infrastructure/ # EXISTS: Infrastructure layer
│   │   └── Po.SeeReview.Infrastructure.csproj # MODIFY: Enable nullable, remove versions
│   └── Po.SeeReview.Shared/         # EXISTS: Shared DTOs
│       └── Po.SeeReview.Shared.csproj # MODIFY: Enable nullable, remove versions
├── tests/
│   ├── Po.SeeReview.UnitTests/      # EXISTS: Unit tests
│   │   ├── Po.SeeReview.UnitTests.csproj # MODIFY: Add bUnit, configure coverage
│   │   └── ComponentTests/          # NEW: bUnit component tests
│   ├── Po.SeeReview.IntegrationTests/ # EXISTS: Integration tests
│   │   ├── Po.SeeReview.IntegrationTests.csproj # MODIFY: Configure coverage
│   │   └── EdgeCases/               # NEW: Edge case test scenarios
│   ├── Po.SeeReview.WebTests/       # EXISTS: Web tests
│   └── e2e/                         # EXISTS: Playwright tests
├── docs/
│   ├── README.md                    # EXISTS
│   ├── PRD.md                       # EXISTS
│   ├── deployment.md                # MODIFY: Add Snapshot Debugger/Profiler docs
│   ├── nullable-warnings.md         # NEW: Nullable warnings inventory
│   ├── content-moderation-policy.md # NEW: Content filtering rules
│   ├── coverage/                    # NEW: Coverage reports (generated)
│   ├── diagrams/                    # EXISTS: Mermaid diagrams
│   │   ├── c4-context.mmd           # NEW: C4 context diagram
│   │   ├── c4-container.mmd         # NEW: C4 container diagram
│   │   ├── c4-component.mmd         # NEW: C4 component diagram
│   │   └── sequence-comic-generation.mmd # NEW: Sequence diagram
│   └── kql/                         # NEW: KQL query library
│       ├── errors.kql               # NEW: Error tracking queries
│       ├── performance.kql          # NEW: Performance analysis
│       ├── dependencies.kql         # NEW: Dependency health
│       └── custom-metrics.kql       # NEW: Business metrics
├── infra/                           # EXISTS: Infrastructure
│   ├── main.bicep                   # MODIFY: Enhance with all required services
│   └── modules/                     # EXISTS/ENHANCE: Bicep modules
│       ├── appservice.bicep         # EXISTS
│       ├── storage.bicep            # EXISTS
│       ├── monitoring.bicep         # NEW: App Insights + Log Analytics
│       └── budget.bicep             # NEW: $5 budget with alerts
└── scripts/                         # EXISTS: Automation scripts
```

**Structure Decision**: Web application structure (API + Blazor client) is already established. This feature enhances existing structure with new governance artifacts (Directory.Packages.props, nullable warnings inventory, coverage reports, KQL library, additional Bicep modules) and configuration changes (nullable reference types, OpenTelemetry, coverage collection).

## Complexity Tracking

**No constitution violations requiring justification**. This feature implements compliance improvements, not violations. All work aligns with constitution v2.0.0 requirements.
