<!--
SYNC IMPACT REPORT
==================
Version Change: 1.0.0 → 2.0.0
Rationale: Major revision with comprehensive principle expansion, new foundation and operations sections, and breaking changes to architecture requirements.

Modified Principles:
  🔄 Foundation Section (EXPANDED) - Added Solution Naming, Package Management, Null Safety
  🔄 Architecture Section (EXPANDED) - Added Code Organization, Design Philosophy, API Design, Repository Structure, Documentation Constraint
  🔄 Implementation Section (NEW MAJOR) - Separated into API/Backend and Frontend subsections
  🔄 Quality & Testing (EXPANDED) - Added Code Hygiene, Dependency Hygiene, Workflow, Test Naming, Code Coverage with 80% threshold
  🔄 Operations & Azure (NEW MAJOR) - Comprehensive Azure provisioning, CI/CD, logging, telemetry, and diagnostics

Removed Principles:
  ❌ Old Principle II (Vertical Slice Architecture) - Merged into Architecture section
  ❌ Old Principle III (Test-First Development) - Merged into Quality & Testing
  ❌ Old Principle IV (API Observability) - Merged into Implementation and Operations
  ❌ Old Principle V (Azure Table Storage Default) - Merged into Operations & Azure
  ❌ Old Principle VI (Mobile-First UI) - Merged into Implementation/Frontend
  ❌ Old Principle VII (Automation & Simplicity) - Merged into Architecture/Documentation Constraint

Added Sections:
  ✅ 1. Foundation - .NET version, solution naming, package management, null safety
  ✅ 2. Architecture - Vertical slices, SOLID, Minimal APIs, CQRS, repository structure
  ✅ 3. Implementation - Detailed API/Backend and Frontend requirements
  ✅ 4. Quality & Testing - TDD workflow, naming conventions, coverage requirements, test types
  ✅ 5. Operations & Azure - Bicep, azd, CI/CD, cost management, logging, telemetry, diagnostics

Template Updates:
  ✅ plan-template.md - UPDATED for expanded Constitution Check
  ⚠ spec-template.md - REQUIRES UPDATE for new architecture constraints
  ⚠ tasks-template.md - REQUIRES UPDATE for new task categories (coverage, telemetry, cost)

Deferred Items: None

Suggested Commit Message:
  docs: amend constitution to v2.0.0 (comprehensive governance expansion)
-->

# PoSeeReview Constitution

## 1. Foundation

### Solution Naming (REQUIRED)

The .sln file name (e.g., `PoProject.sln`) is the base identifier. It MUST be used as the
name for all Azure services/resource groups (e.g., PoProject) and the user-facing HTML
`<title>`. All project names MUST follow the `Po.AppName.*` pattern exactly.

**Rationale**: Consistent naming across solution, Azure resources, and UI creates clear
traceability and prevents naming conflicts. The Po.* prefix establishes clear project
ownership and branding.

### .NET Version (REQUIRED)

All projects MUST target .NET 9. The `global.json` file MUST be locked to a 9.0.xxx SDK
version. Builds MUST fail if a different SDK major version is detected. All project files
MUST target `net9.0`.

**Rationale**: Version consistency prevents runtime surprises, dependency conflicts, and
ensures teams leverage the latest performance, security, and language features.

### Package Management (REQUIRED)

All NuGet packages MUST be managed centrally in a `Directory.Packages.props` file at the
repository root. Individual project files MUST NOT specify package versions.

**Rationale**: Central package management eliminates version conflicts, simplifies
dependency updates, and provides a single source of truth for all package versions.

### Null Safety (REQUIRED)

Nullable Reference Types (`<Nullable>enable</Nullable>`) MUST be enabled in all .csproj
files. All code MUST handle nullability explicitly.

**Rationale**: Null reference exceptions are a major source of runtime errors. Enabling
nullable reference types catches potential null issues at compile time, improving code
quality and reducing production bugs.

## 2. Architecture

### Code Organization (REQUIRED)

The API MUST use Vertical Slice Architecture. All API logic (endpoints, CQRS handlers) MUST
be co-located by feature in `/src/Po.[AppName].Api/Features/`. Each file MUST NOT exceed
500 lines; enforce via linters or pre-commit checks.

**Rationale**: Vertical slices reduce coupling and enable independent feature development.
Co-location improves discoverability. File size limits enforce cohesion and prevent
monolithic classes.

### Design Philosophy (REQUIRED)

Apply SOLID principles and standard Gang of Four design patterns. Document their use in
code comments or the PRD. Prefer small, well-factored code following SOLID principles.

**Rationale**: SOLID principles and design patterns ensure maintainability, testability,
and extensibility. Documentation helps future developers understand architectural decisions.

### API Design (REQUIRED)

Use Minimal APIs and the CQRS pattern for all new endpoints. Commands and queries MUST be
separated. Use MediatR or similar libraries to implement CQRS handlers.

**Rationale**: Minimal APIs reduce boilerplate and improve readability. CQRS separates
read and write concerns, improving clarity and enabling independent optimization of each
operation type.

### Repository Structure (REQUIRED)

Adhere to the standard root folder structure: `/src`, `/tests`, `/docs`, `/infra`, and
`/scripts`. The `/src` projects MUST follow separation of concerns: `...Api`, `...Client`,
and `...Shared` (for DTOs/contracts). The `/docs` folder will contain `README.md`,
`PRD.md`, diagrams, and KQL query library.

Repository layout at root:

```
/src/Po.AppName.Api          (ASP.NET Core Web API)
/src/Po.AppName.Client       (Blazor WebAssembly)
/src/Po.AppName.Shared       (DTOs and contracts)
/src/Po.AppName.Core         (Domain entities and interfaces)
/src/Po.AppName.Infrastructure (Implementations)
/tests/Po.AppName.UnitTests
/tests/Po.AppName.IntegrationTests
/tests/Po.AppName.WebTests
/tests/e2e                   (Playwright tests)
/docs                        (Documentation only)
/docs/diagrams               (Mermaid and SVG files)
/infra                       (Bicep infrastructure code)
/scripts                     (CLI helpers and automation)
```

**Rationale**: Standardized layout accelerates onboarding and enables tooling reuse. Clear
separation of concerns improves maintainability and testability.

### Documentation Constraint (REQUIRED)

No .md files shall be created outside of `README.md` and `PRD.md` in `/docs`. Mermaid
diagram files (`.mmd`) and SVG files go in `/docs/diagrams/`. Do NOT create extra markdown
or PowerShell files during conversations; Azure deployment files are the only exception.

**Rationale**: Limiting documentation files prevents documentation sprawl and ensures
consistency. Centralized docs improve discoverability and reduce maintenance burden.

## 3. Implementation

### API & Backend

#### API Documentation (REQUIRED)

All API endpoints MUST have Swagger (OpenAPI) generation enabled from project start. `.http`
files MUST be maintained for manual verification. Create a minimal, easy-to-invoke set of
API methods surfaced in Swagger for manual verification during development and QA.

**Rationale**: Swagger enables self-service API exploration and testing. .http files
provide quick manual verification without external tools.

#### Health Checks (REQUIRED)

Implement a health check endpoint at `/api/health` that validates connectivity to all
external dependencies. The endpoint MUST support readiness and liveness semantics.

**Rationale**: Health endpoints support orchestration tooling (Kubernetes, App Service) and
enable automated monitoring of service dependencies.

#### Error Handling (REQUIRED)

All API calls MUST return robust, structured error details. Use structured `ILogger.LogError`
within all catch blocks. Global exception handling middleware MUST transform all errors into
RFC 7807 Problem Details responses; raw exceptions or stack traces MUST NOT be returned to
clients.

**Rationale**: Problem Details standardize error contracts across APIs. Structured logging
enables queryable diagnostics. Preventing stack trace exposure improves security.

### Frontend (Blazor)

#### UI Framework Principle (REQUIRED)

`Microsoft.FluentUI.AspNetCore.Components` is the primary component library.
`Radzen.Blazor` may only be used for complex requirements not met by FluentUI, and usage
MUST be justified by UX need in the PRD or code comments.

**Rationale**: FluentUI provides modern, accessible components aligned with Microsoft design
language. Limiting external dependencies reduces bundle size and maintenance burden.

#### Responsive Design (REQUIRED)

The UI MUST be mobile-first (portrait mode), responsive, fluid, and touch-friendly.
Responsive design MUST prioritize mobile portrait experience: fluid grid, touch-friendly
controls, readable typography, appropriate breakpoints. Main flows MUST be tested on
desktop and narrow-screen mobile emulation to validate layout and interactions.

**Rationale**: Mobile traffic dominates modern web usage. Portrait-first ensures usability
on the most constrained screens. Testing validates responsive behavior and prevents
regressions.

### Development Environment

#### Debug Launch (REQUIRED)

The environment MUST support a one-step 'F5' debug launch for the API and browser.
Implementation: Commit a `launch.json` with a `serverReadyAction` to the repository.

**Rationale**: One-step debugging improves developer productivity and reduces friction when
starting work. Standardized launch configuration ensures consistent experience across team
members.

#### Local Secrets (REQUIRED)

Use the .NET User Secrets manager for all sensitive keys during local development. Secrets
MUST NOT be committed to the repository.

**Rationale**: User Secrets prevents accidental exposure of sensitive data in version
control while maintaining developer convenience.

#### Local Storage (REQUIRED)

Emulate all required Azure Storage (Table, Blob) services locally. Implementation: Use
Azurite for local development and integration testing. Azure Table Storage is the default
persistence layer. Alternative stores require explicit specification and approval. Table
naming MUST follow the pattern `PoAppName[TableName]`.

**Rationale**: Azurite enables local-first development without Azure dependencies. Azure
Table Storage provides cost-effective, scalable NoSQL persistence. Naming conventions
prevent collisions across projects.

## 4. Quality & Testing

### Code Hygiene (REQUIRED)

All build warnings/errors MUST be resolved before pushing changes to GitHub. Run
`dotnet format` to ensure style consistency. Enforce formatting with `dotnet format` as a
pre-commit or CI gate; fail builds on format errors.

**Rationale**: Clean builds prevent technical debt accumulation. Automated formatting
removes human judgment variance and aids code review.

### Dependency Hygiene (REQUIRED)

Regularly check for and apply updates to all packages via `Directory.Packages.props`. Review
and apply security updates promptly.

**Rationale**: Keeping dependencies current reduces security vulnerabilities and ensures
access to bug fixes and performance improvements.

### Workflow (REQUIRED)

Strictly follow a Test-Driven Development (TDD) workflow (Red → Green → Refactor). Write a
failing test first, then implement code to make it pass, then refactor.

**Rationale**: TDD catches regressions early, drives better design, and documents intent.
The red-green-refactor cycle ensures code is testable from inception.

### Test Naming (REQUIRED)

Test methods MUST follow the `MethodName_StateUnderTest_ExpectedBehavior` convention. Test
class names MUST end with `Tests` suffix.

**Rationale**: Consistent naming makes test intent clear and improves test discoverability.
Self-documenting test names serve as executable specifications.

### Code Coverage (REQUIRED)

Enforce a minimum 80% line coverage threshold for all new business logic. A combined
coverage report MUST be generated in `docs/coverage/`. CI pipelines MUST validate coverage
thresholds and fail builds that don't meet the minimum.

**Rationale**: High coverage ensures critical paths are tested. Coverage reports provide
visibility into untested code. Automated enforcement prevents coverage erosion over time.

### Unit Tests (xUnit) (REQUIRED)

MUST cover all backend business logic (e.g., MediatR handlers, services, repositories) with
all external dependencies mocked. Tests MUST be fast (< 100ms each) and isolated.

**Rationale**: Unit tests validate business logic in isolation. Fast tests enable rapid
feedback cycles. Mocking dependencies ensures tests are deterministic and don't rely on
external systems.

### Component Tests (bUnit) (REQUIRED)

MUST cover all new Blazor components (rendering, user interactions, state changes), mocking
dependencies like `IHttpClientFactory`. Validate component behavior, not implementation
details.

**Rationale**: Component tests ensure UI components behave correctly in isolation. Mocking
HTTP dependencies enables testing without backend dependencies.

### Integration Tests (xUnit) (REQUIRED)

A "happy path" test MUST be created for every new API endpoint, running against a test host
and Azurite emulator. Realistic test data should be generated. Integration tests MUST
include setup and teardown to leave no lingering data. Database-using tests MUST run
against Azurite or disposable test tables and clean up after themselves.

**Rationale**: Integration tests validate end-to-end API behavior. Testing against Azurite
ensures storage logic works correctly. Cleanup prevents test pollution and ensures
repeatability.

### E2E Tests (Playwright) (REQUIRED)

Tests MUST target Chromium (mobile and desktop views). Use network interception to mock API
responses for stable testing. Integrate automated accessibility and visual regression
checks. E2E tests use Playwright with TypeScript and are executed manually (NOT in CI).

**Rationale**: E2E tests validate user workflows across the full stack. Mobile and desktop
testing ensures responsive behavior. Manual execution avoids CI flakiness while providing
comprehensive pre-release validation.

## 5. Operations & Azure

### Provisioning (REQUIRED)

All Azure infrastructure MUST be provisioned using Bicep (from the `/infra` folder) and
deployed via Azure Developer CLI (azd). Infrastructure as Code MUST be the single source of
truth for all Azure resources.

**Rationale**: Bicep provides type-safe, declarative infrastructure definitions. azd
simplifies deployment workflows. IaC ensures reproducible, version-controlled
infrastructure.

### CI/CD (REQUIRED)

The GitHub Actions workflow MUST use Federated Credentials (OIDC) for secure, secret-less
connection to Azure. The YML file MUST build the code and deploy it to the resource group
(same name as .sln) as an App Service (same name as .sln). CI checks MUST validate: .NET
SDK major version is 9, required ports are configured, project prefix conforms to
`Po.AppName`, `/api/health` exists, and Problem Details middleware is present.

**Rationale**: OIDC eliminates secret management burden and improves security. Automated
validation prevents non-compliant code from merging. Naming consistency simplifies
deployment automation.

### Required Services (REQUIRED)

Bicep scripts MUST provision, at minimum: Application Insights & Log Analytics, App Service,
and Azure Storage (Table and Blob). All services MUST use appropriate SKUs for cost
optimization.

**Rationale**: These services provide essential functionality for modern cloud applications:
monitoring, hosting, and storage. Standardizing required services ensures consistent
operational capabilities.

### Cost Management (REQUIRED)

A $5 monthly cost limit MUST be created for the application's resource group. If costs
exceed this amount, the resource group MUST be disabled or resources scaled down
automatically. Implement budget alerts and automated cost controls.

**Rationale**: Cost limits prevent unexpected charges and enforce fiscal discipline.
Automated controls ensure budgets are respected without manual intervention.

### Logging (REQUIRED)

Use Serilog for all structured logging. Configuration MUST be driven by `appsettings.json`
to write to the Debug Console (in Development) and Application Insights (in Production). Use
structured `ILogger` with sensible local sinks following .NET best practices.

**Rationale**: Serilog provides powerful structured logging with flexible sinks. Environment-
specific configuration ensures appropriate logging targets. Structured logs enable rich
querying and analysis.

### Telemetry (REQUIRED)

Use modern OpenTelemetry abstractions for all custom telemetry. Traces: Use `ActivitySource`
to create custom spans for key business actions. Metrics: Use `Meter` to create custom
metrics for business-critical values.

**Rationale**: OpenTelemetry provides vendor-neutral, standardized telemetry. Custom spans
enable distributed tracing. Custom metrics capture business KPIs alongside infrastructure
metrics.

### Production Diagnostics (REQUIRED)

Enable the Application Insights Snapshot Debugger on the App Service. Enable the
Application Insights Profiler on the App Service.

**Rationale**: Snapshot Debugger captures application state during exceptions, enabling
post-mortem debugging. Profiler identifies performance bottlenecks in production without
code changes.

### KQL Library (REQUIRED)

The `docs/kql/` folder MUST be populated with essential queries for monitoring health,
performance, and custom business metrics. Include queries for: error rates, response times,
dependency health, custom events, and business metrics.

**Rationale**: Predefined KQL queries accelerate incident response and enable consistent
monitoring practices. Shared queries improve team knowledge and reduce learning curve.

## Rule Classification (INFORMATIONAL)

Rules are tagged as REQUIRED, PREFERRED, or INFORMATIONAL. REQUIRED rules MUST be enforced
and violations block merges. PREFERRED rules are defaults that can be overridden with
justification. INFORMATIONAL rules provide guidance but are not enforced.

**Rationale**: Explicit classification clarifies which rules are negotiable and which are
non-negotiable, reducing ambiguity during reviews.

## Governance

This constitution supersedes all other practices. Amendments require documentation,
approval, and a migration plan. All code reviews MUST verify compliance with REQUIRED
principles. Complexity introduced MUST be justified against simplicity and YAGNI principles.
Use `.specify/memory/constitution.md` for runtime development guidance.

Versioning follows semantic versioning:
- **MAJOR**: Backward incompatible governance/principle removals or redefinitions
- **MINOR**: New principle/section added or materially expanded guidance
- **PATCH**: Clarifications, wording, typo fixes, non-semantic refinements

Amendments MUST increment the version number and update the Last Amended date.

**Version**: 2.0.0 | **Ratified**: 2025-10-27 | **Last Amended**: 2025-11-12
