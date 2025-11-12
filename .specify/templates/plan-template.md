# Implementation Plan: [FEATURE]

**Branch**: `[###-feature-name]` | **Date**: [DATE] | **Spec**: [link]
**Input**: Feature specification from `/specs/[###-feature-name]/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. See `.specify/templates/commands/plan.md` for the execution workflow.

## Summary

[Extract from feature spec: primary requirement + technical approach from research]

## Technical Context

<!--
  ACTION REQUIRED: Replace the content in this section with the technical details
  for the project. The structure here is presented in advisory capacity to guide
  the iteration process.
-->

**Language/Version**: [e.g., Python 3.11, Swift 5.9, Rust 1.75 or NEEDS CLARIFICATION]  
**Primary Dependencies**: [e.g., FastAPI, UIKit, LLVM or NEEDS CLARIFICATION]  
**Storage**: [if applicable, e.g., PostgreSQL, CoreData, files or N/A]  
**Testing**: [e.g., pytest, XCTest, cargo test or NEEDS CLARIFICATION]  
**Target Platform**: [e.g., Linux server, iOS 15+, WASM or NEEDS CLARIFICATION]
**Project Type**: [single/web/mobile - determines source structure]  
**Performance Goals**: [domain-specific, e.g., 1000 req/s, 10k lines/sec, 60 fps or NEEDS CLARIFICATION]  
**Constraints**: [domain-specific, e.g., <200ms p95, <100MB memory, offline-capable or NEEDS CLARIFICATION]  
**Scale/Scope**: [domain-specific, e.g., 10k users, 1M LOC, 50 screens or NEEDS CLARIFICATION]

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### 1. Foundation
- [ ] Solution naming: .sln name matches Azure resources and HTML title
- [ ] .NET 9: global.json locked to 9.0.xxx SDK, all projects target net9.0
- [ ] Package management: Directory.Packages.props at root, no versions in .csproj
- [ ] Null safety: `<Nullable>enable</Nullable>` in all .csproj files

### 2. Architecture
- [ ] Code organization: Vertical Slice Architecture in /Features/, max 500 lines per file
- [ ] Design philosophy: SOLID principles and GoF patterns documented
- [ ] API design: Minimal APIs with CQRS pattern for all endpoints
- [ ] Repository structure: /src, /tests, /docs, /infra, /scripts layout followed
- [ ] Documentation constraint: Only README.md, PRD.md in /docs; diagrams in /docs/diagrams/

### 3. Implementation
- [ ] API documentation: Swagger enabled, .http files maintained
- [ ] Health checks: /api/health endpoint validates all dependencies
- [ ] Error handling: Problem Details middleware, structured logging in catch blocks
- [ ] UI framework: FluentUI primary, Radzen only if justified
- [ ] Responsive design: Mobile-first, tested on desktop + mobile emulation
- [ ] Debug launch: launch.json with serverReadyAction committed
- [ ] Local secrets: User Secrets manager for sensitive data
- [ ] Local storage: Azurite for Table/Blob emulation, tables follow PoAppName[Name]

### 4. Quality & Testing
- [ ] Code hygiene: No build warnings/errors, dotnet format enforced
- [ ] Dependency hygiene: Regular package updates via Directory.Packages.props
- [ ] Workflow: TDD (Red → Green → Refactor) strictly followed
- [ ] Test naming: MethodName_StateUnderTest_ExpectedBehavior convention
- [ ] Code coverage: 80% minimum threshold, report in docs/coverage/
- [ ] Unit tests: xUnit for business logic, all dependencies mocked
- [ ] Component tests: bUnit for Blazor components
- [ ] Integration tests: Happy path for all endpoints, Azurite emulator, cleanup
- [ ] E2E tests: Playwright with TypeScript, Chromium mobile + desktop, manual execution

### 5. Operations & Azure
- [ ] Provisioning: Bicep in /infra, deployed via azd
- [ ] CI/CD: GitHub Actions with OIDC, validates SDK/ports/naming/health
- [ ] Required services: App Insights, Log Analytics, App Service, Storage
- [ ] Cost management: $5 monthly budget with automated alerts/controls
- [ ] Logging: Serilog to Debug Console (Dev) and App Insights (Prod)
- [ ] Telemetry: OpenTelemetry with ActivitySource (traces) and Meter (metrics)
- [ ] Production diagnostics: Snapshot Debugger and Profiler enabled
- [ ] KQL library: Essential queries in docs/kql/

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
<!--
  ACTION REQUIRED: Replace the placeholder tree below with the concrete layout
  for this feature. Delete unused options and expand the chosen structure with
  real paths (e.g., apps/admin, packages/something). The delivered plan must
  not include Option labels.
-->

```text
# [REMOVE IF UNUSED] Option 1: Single project (DEFAULT)
src/
├── models/
├── services/
├── cli/
└── lib/

tests/
├── contract/
├── integration/
└── unit/

# [REMOVE IF UNUSED] Option 2: Web application (when "frontend" + "backend" detected)
backend/
├── src/
│   ├── models/
│   ├── services/
│   └── api/
└── tests/

frontend/
├── src/
│   ├── components/
│   ├── pages/
│   └── services/
└── tests/

# [REMOVE IF UNUSED] Option 3: Mobile + API (when "iOS/Android" detected)
api/
└── [same as backend above]

ios/ or android/
└── [platform-specific structure: feature modules, UI flows, platform tests]
```

**Structure Decision**: [Document the selected structure and reference the real
directories captured above]

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| [e.g., 4th project] | [current need] | [why 3 projects insufficient] |
| [e.g., Repository pattern] | [specific problem] | [why direct DB access insufficient] |
