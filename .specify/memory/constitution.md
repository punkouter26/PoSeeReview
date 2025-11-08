<!--
SYNC IMPACT REPORT
==================
Version Change: N/A → 1.0.0 (Initial ratification)
Rationale: This is the initial constitution defining governance for Po.* .NET 9.0 projects.

Modified/Added Principles:
  ✅ I. .NET 9.0 Enforcement (NEW) - SDK version enforcement, build failure on mismatch
  ✅ II. Vertical Slice Architecture (NEW) - Clean Architecture boundaries, file size limits
  ✅ III. Test-First Development (NEW) - TDD with xUnit, isolated integration tests
  ✅ IV. API Observability (NEW) - Swagger, health endpoints, Problem Details, Serilog
  ✅ V. Azure Table Storage Default (NEW) - Azurite local, naming conventions
  ✅ VI. Mobile-First UI (NEW) - Responsive design, Blazor with optional Radzen
  ✅ VII. Automation & Simplicity (NEW) - CLI-only operations, minimal docs

Template Updates:
  ✅ plan-template.md - Constitution Check section aligns with principles
  ✅ spec-template.md - Scope/requirements match architecture constraints
  ✅ tasks-template.md - Task categories support testing discipline, observability

Deferred Items: None

Suggested Commit Message:
  docs: ratify constitution v1.0.0 (Po.* .NET 9.0 governance)
-->

# PoSeeReview Constitution

## Core Principles

### I. .NET 9.0 Enforcement (REQUIRED)

Projects MUST use .NET 9.0 SDK (latest patch). Builds MUST fail if a different SDK major
version is detected. All project files MUST target `net9.0`. CI/CD pipelines MUST validate
SDK version before build execution.

**Rationale**: Version consistency prevents runtime surprises, dependency conflicts, and
ensures teams leverage the latest performance, security, and language features.

### II. Vertical Slice Architecture (REQUIRED)

Use Vertical Slice Architecture with Clean Architecture boundaries where complexity requires
separation. Each file MUST NOT exceed 500 lines; enforce via linters or pre-commit checks.
Prefer small, well-factored code following SOLID principles and appropriate Gang of Four
patterns.

**Rationale**: Vertical slices reduce coupling and enable independent feature development.
File size limits enforce cohesion. SOLID and design patterns ensure maintainability.

### III. Test-First Development (REQUIRED)

Follow TDD: write a failing xUnit test first, then implement code. Maintain unit and
integration tests separately. Integration tests MUST include setup and teardown to leave no
lingering data. E2E tests use Playwright MCP with TypeScript and are executed manually (not
in CI).

**Rationale**: TDD catches regressions early, drives better design, and documents intent.
Isolated integration tests prevent side effects. Manual E2E avoids flaky CI builds.

### IV. API Observability (REQUIRED)

APIs MUST expose Swagger/OpenAPI from project start and document endpoints for manual
testing. APIs MUST expose `/api/health` with readiness and liveness semantics. Global
exception handling middleware MUST transform all errors into RFC 7807 Problem Details
responses; raw exceptions or stack traces MUST NOT be returned. Use Serilog for structured
logging with sensible local sinks following .NET best practices.

**Rationale**: Swagger enables self-service exploration. Health endpoints support
orchestration tooling. Problem Details standardize error contracts. Structured logging
enables queryable diagnostics.

### V. Azure Table Storage Default (REQUIRED)

Default persistence MUST be Azure Table Storage using Azurite for local development.
Alternative stores require explicit specification and approval. Table naming MUST follow
the pattern `PoAppName[TableName]`. Database-using tests MUST run against Azurite or
disposable test tables and clean up after themselves.

**Rationale**: Azure Table Storage provides cost-effective, scalable NoSQL persistence.
Azurite enables local-first development. Naming conventions prevent collisions. Test
isolation ensures repeatability.

### VI. Mobile-First UI (REQUIRED)

Blazor Client MUST start with built-in components; adopt Radzen.Blazor only for advanced
scenarios justified by UX need. Responsive design MUST prioritize mobile portrait
experience: fluid grid, touch-friendly controls, readable typography, appropriate
breakpoints. Main flows MUST be tested on desktop and narrow-screen mobile emulation to
validate layout and interactions.

**Rationale**: Mobile traffic dominates modern web usage. Built-in components reduce
dependencies. Portrait-first ensures usability on the most constrained screens. Testing
validates responsive behavior.

### VII. Automation & Simplicity (REQUIRED)

Automate operations using CLI commands only; one-line commands at a time for human
execution. Do NOT create extra markdown or PowerShell files during conversations; Azure
deployment files are the only exception. Documentation files created MUST reside in `/docs`
at repository root. Start simple and follow YAGNI principles.

**Rationale**: CLI commands are composable, versionable, and automatable. Avoiding script
proliferation reduces maintenance burden. Centralized docs improve discoverability. YAGNI
prevents over-engineering.

## Repository Structure (REQUIRED)

All Po.* projects MUST follow this repository layout at root:

```
/src/Po.AppName.Api
/src/Po.AppName.Client       (Blazor WebAssembly)
/src/Po.AppName.Shared
/tests/Po.AppName.UnitTests
/tests/Po.AppName.IntegrationTests
/docs                        (PRD.MD, STEPS.MD, README.MD only)
/scripts                     (CLI helpers only)
```

Project and table names MUST follow the `Po.AppName.*` pattern exactly. APIs MUST bind to
HTTP 5000 and HTTPS 5001 only.

**Rationale**: Standardized layout accelerates onboarding and enables tooling reuse. Naming
conventions enforce branding and prevent conflicts. Fixed ports simplify local orchestration.

## Tooling & Enforcement (REQUIRED)

Enforce formatting with `dotnet format` as a pre-commit or CI gate; fail builds on format
errors. CI checks MUST validate: .NET SDK major version is 9, required ports are
configured, project prefix conforms to `Po.AppName`, `/api/health` exists, and Problem
Details middleware is present. Provide one-line CLI commands in comments for required tasks
only; avoid multi-step scripts in conversation.

**Rationale**: Automated enforcement removes human judgment variance. Formatting consistency
aids code review. CI gates prevent non-compliant code from merging.

## Testing Workflow (REQUIRED)

xUnit for unit and integration tests. Integration tests MUST include setup and teardown to
leave no lingering data. E2E: Playwright MCP with TypeScript; E2E tests are executed
manually and are NOT included in CI. Create a minimal, easy-to-invoke set of API methods
surfaced in Swagger for manual verification during development and QA.

**Rationale**: xUnit is idiomatic for .NET. Isolated integration tests ensure determinism.
Manual E2E avoids CI flakiness. Swagger methods accelerate manual testing.

## Rule Classification (INFORMATIONAL)

Rules are tagged as REQUIRED, PREFERRED, or INFORMATIONAL. REQUIRED rules MUST be enforced
and violations block merges. PREFERRED rules are defaults that can be overridden with
justification. INFORMATIONAL rules provide guidance but are not enforced.

**Rationale**: Explicit classification clarifies which rules are negotiable and which are
non-negotiable, reducing ambiguity during reviews.

## Governance

This constitution supersedes all other practices. Amendments require documentation,
approval, and a migration plan. All code reviews MUST verify compliance with REQUIRED
principles. Complexity introduced MUST be justified against principles II (simplicity) and
VII (YAGNI). Use `.specify/memory/constitution.md` for runtime development guidance.

Versioning follows semantic versioning:
- **MAJOR**: Backward incompatible governance/principle removals or redefinitions
- **MINOR**: New principle/section added or materially expanded guidance
- **PATCH**: Clarifications, wording, typo fixes, non-semantic refinements

Amendments MUST increment the version number and update the Last Amended date.

**Version**: 1.0.0 | **Ratified**: 2025-10-27 | **Last Amended**: 2025-10-27
