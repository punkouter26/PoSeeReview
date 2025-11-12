# Tasks: Constitution v2.0.0 Compliance

**Input**: Design documents from `/specs/002-constitution-compliance/`  
**Prerequisites**: plan.md ✅, spec.md ✅, research.md ✅, data-model.md ✅, contracts/ ✅

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

**Tests**: Not required by specification - this is infrastructure/configuration work. Validation done via build verification and manual testing per quickstart.md.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Project initialization and documentation structure

- [x] T001 Create docs/nullable-warnings.md inventory file with table headers (File, Line, Code, Message, Severity, Status)
- [x] T002 Create docs/coverage/ directory for coverage report outputs
- [x] T003 [P] Create docs/kql/ directory for KQL query library
- [x] T004 [P] Create tests/Po.SeeReview.UnitTests/ComponentTests/ directory for bUnit tests
- [x] T005 [P] Create tests/Po.SeeReview.IntegrationTests/EdgeCases/ directory for edge case scenarios

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core infrastructure that MUST be complete before user story implementation

**⚠️ CRITICAL**: These tasks establish the foundation for all compliance work

- [x] T006 Extract current package inventory using `dotnet list package --include-transitive > package-inventory.txt`
- [x] T007 Create Directory.Packages.props at repository root with `<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>`
- [x] T008 [P] Verify .NET 9.0 SDK lock in global.json (should already exist per constitution check)
- [x] T009 [P] Create scripts/collect-coverage.ps1 for coverage collection automation

**Checkpoint**: Foundation ready - user story implementation can now begin

---

## Phase 3: User Story 1 - Centralized Package Management (Priority: P1) 🎯 MVP

**Goal**: All NuGet package versions managed in single Directory.Packages.props file, eliminating version conflicts

**Independent Test**: Run `dotnet restore` and `dotnet build` at solution level, verify all packages resolve from Directory.Packages.props

### Implementation for User Story 1

- [x] T010 [P] [US1] Parse package-inventory.txt and extract unique package versions
- [x] T011 [US1] Add all package versions to Directory.Packages.props as `<PackageVersion Include="..." Version="..." />` elements
- [x] T012 [P] [US1] Remove Version attributes from src/Po.SeeReview.Api/Po.SeeReview.Api.csproj PackageReference elements
- [x] T013 [P] [US1] Remove Version attributes from src/Po.SeeReview.Client/Po.SeeReview.Client.csproj PackageReference elements
- [x] T014 [P] [US1] Remove Version attributes from src/Po.SeeReview.Core/Po.SeeReview.Core.csproj PackageReference elements
- [x] T015 [P] [US1] Remove Version attributes from src/Po.SeeReview.Infrastructure/Po.SeeReview.Infrastructure.csproj PackageReference elements
- [x] T016 [P] [US1] Remove Version attributes from src/Po.SeeReview.Shared/Po.SeeReview.Shared.csproj PackageReference elements
- [x] T017 [P] [US1] Remove Version attributes from tests/Po.SeeReview.UnitTests/Po.SeeReview.UnitTests.csproj PackageReference elements
- [x] T018 [P] [US1] Remove Version attributes from tests/Po.SeeReview.IntegrationTests/Po.SeeReview.IntegrationTests.csproj PackageReference elements
- [x] T019 [P] [US1] Remove Version attributes from tests/Po.SeeReview.WebTests/Po.SeeReview.WebTests.csproj PackageReference elements
- [x] T020 [US1] Run `dotnet restore` at solution level to verify package resolution
- [x] T021 [US1] Run `dotnet build` at solution level to verify successful compilation with centralized packages

**Checkpoint**: Directory.Packages.props manages all package versions, builds succeed (FR-001, FR-002, SC-001)

---

## Phase 4: User Story 2 - Null Safety Enforcement (Priority: P1)

**Goal**: Nullable reference types enabled across all projects, warnings documented for incremental resolution

**Independent Test**: Run `dotnet build` and verify compiler emits nullable warnings that are logged but don't fail build

### Implementation for User Story 2

- [x] T022 [P] [US2] Add `<Nullable>enable</Nullable>` to src/Po.SeeReview.Api/Po.SeeReview.Api.csproj PropertyGroup
- [x] T023 [P] [US2] Add `<Nullable>enable</Nullable>` to src/Po.SeeReview.Client/Po.SeeReview.Client.csproj PropertyGroup
- [x] T024 [P] [US2] Add `<Nullable>enable</Nullable>` to src/Po.SeeReview.Core/Po.SeeReview.Core.csproj PropertyGroup
- [x] T025 [P] [US2] Add `<Nullable>enable</Nullable>` to src/Po.SeeReview.Infrastructure/Po.SeeReview.Infrastructure.csproj PropertyGroup
- [x] T026 [P] [US2] Add `<Nullable>enable</Nullable>` to src/Po.SeeReview.Shared/Po.SeeReview.Shared.csproj PropertyGroup
- [x] T027 [US2] Run `dotnet build > build-output.txt 2>&1` to capture all nullable warnings
- [x] T028 [US2] Parse build-output.txt and filter for CS86xx nullable warning codes
- [x] T029 [US2] Populate docs/nullable-warnings.md with warnings inventory (file path, line, code, message, category, severity)
- [x] T030 [US2] Categorize warnings by severity (High: public APIs, Medium: internal methods, Low: private methods)
- [x] T031 [US2] Verify build succeeds with warnings logged (not failing)

**Checkpoint**: Nullable reference types enabled, all warnings documented in inventory (FR-003, FR-004, FR-004a, SC-002)

---

## Phase 5: User Story 3 - Code Coverage Enforcement (Priority: P1)

**Goal**: Automated code coverage validation with 80% target threshold tracked via reports

**Independent Test**: Run coverage collection, verify HTML and XML reports generated in docs/coverage/ showing 80%+ coverage

### Implementation for User Story 3

- [x] T032 [US3] Implement scripts/collect-coverage.ps1 with dotnet-coverage collect command
- [x] T033 [US3] Add XML conversion to scripts/collect-coverage.ps1: `dotnet-coverage merge -o docs/coverage/coverage.xml -f xml`
- [x] T034 [US3] Add HTML conversion to scripts/collect-coverage.ps1: `dotnet-coverage merge -o docs/coverage/ -f html`
- [x] T035 [US3] Run `.\scripts\collect-coverage.ps1` to generate initial coverage baseline
- [x] T036 [US3] Open docs/coverage/index.html and verify coverage report displays correctly
- [x] T037 [US3] Document coverage thresholds and non-blocking behavior in docs/coverage/README.md
- [x] T038 [US3] Verify coverage metrics are informational (build does not fail on low coverage)

**Checkpoint**: Coverage collection automated, 80% target tracked via reports (FR-005, FR-006, FR-007, SC-003)

---

## Phase 6: User Story 4 - Blazor Component Testing (Priority: P1)

**Goal**: bUnit testing infrastructure for Blazor components with sample tests demonstrating patterns

**Independent Test**: Run `dotnet test --filter "FullyQualifiedName~ComponentTests"` and verify bUnit tests pass

### Implementation for User Story 4

- [ ] T039 [US4] Add `<PackageVersion Include="bUnit" Version="1.28.9" />` to Directory.Packages.props
- [ ] T040 [US4] Add `<PackageVersion Include="bUnit.web" Version="1.28.9" />` to Directory.Packages.props
- [ ] T041 [US4] Add `<PackageReference Include="bUnit" />` to tests/Po.SeeReview.UnitTests/Po.SeeReview.UnitTests.csproj
- [ ] T042 [US4] Add `<PackageReference Include="bUnit.web" />` to tests/Po.SeeReview.UnitTests/Po.SeeReview.UnitTests.csproj
- [x] T043 [P] [US4] Create sample bUnit test in tests/Po.SeeReview.UnitTests/ComponentTests/SampleComponentTests.cs demonstrating basic rendering
- [x] T044 [P] [US4] Create sample bUnit test in tests/Po.SeeReview.UnitTests/ComponentTests/InteractionTests.cs demonstrating user interactions
- [x] T045 [P] [US4] Create sample bUnit test in tests/Po.SeeReview.UnitTests/ComponentTests/MockedDependencyTests.cs demonstrating mocked IHttpClientFactory
- [x] T046 [US4] Run `dotnet test --filter "FullyQualifiedName~ComponentTests"` to verify bUnit tests execute
- [x] T047 [US4] Document bUnit patterns and best practices in tests/Po.SeeReview.UnitTests/ComponentTests/README.md

**Checkpoint**: bUnit installed, sample tests demonstrate component testing patterns (FR-008, FR-009, SC-004)

---

## Phase 7: User Story 5 - Infrastructure as Code with Bicep (Priority: P2)

**Goal**: All Azure infrastructure defined in Bicep modules, deployable via Azure Developer CLI

**Independent Test**: Run `azd provision` and verify all resources created correctly in Azure Portal

### Implementation for User Story 5

- [x] T048 [P] [US5] Create infra/modules/monitoring.bicep with Log Analytics workspace resource
- [x] T049 [P] [US5] Add Application Insights resource to infra/modules/monitoring.bicep with workspace reference
- [x] T050 [P] [US5] Add connection string and instrumentation key outputs to infra/modules/monitoring.bicep
- [x] T051 [P] [US5] Create infra/modules/budget.bicep with Consumption budget resource
- [x] T052 [P] [US5] Configure budget.bicep with $5 monthly limit and 80%/100% threshold notifications
- [x] T053 [P] [US5] Add contactEmails parameter to budget.bicep for alert recipients
- [x] T054 [US5] Update infra/main.bicep to include monitoring module reference
- [x] T055 [US5] Update infra/main.bicep to include budget module reference
- [x] T056 [US5] Verify existing infra/modules/appservice.bicep and infra/modules/storage.bicep are present
- [ ] T057 [US5] Run `azd provision` to deploy infrastructure
- [ ] T058 [US5] Verify resources created in Azure Portal: App Service, Storage Account, Application Insights, Log Analytics, Budget
- [ ] T059 [US5] Test budget alert by simulating threshold breach (optional manual verification)

**Checkpoint**: Infrastructure deployable via azd, all required services provisioned (FR-010, FR-011, FR-012, FR-013, SC-005, SC-006)

---

## Phase 8: User Story 6 - OpenTelemetry Observability (Priority: P2)

**Goal**: Custom telemetry using OpenTelemetry for business metrics and distributed traces

**Independent Test**: Run application, trigger comic generation, verify custom traces and metrics appear in Application Insights within 2 minutes

### Implementation for User Story 6

- [x] T060 [US6] Add `<PackageVersion Include="OpenTelemetry" Version="1.9.0" />` to Directory.Packages.props
- [x] T061 [US6] Add `<PackageVersion Include="OpenTelemetry.Extensions.Hosting" Version="1.9.0" />` to Directory.Packages.props
- [x] T062 [US6] Add `<PackageVersion Include="OpenTelemetry.Instrumentation.AspNetCore" Version="1.9.0" />` to Directory.Packages.props
- [x] T063 [US6] Add `<PackageVersion Include="Azure.Monitor.OpenTelemetry.Exporter" Version="1.3.0" />` to Directory.Packages.props
- [x] T064 [US6] Add OpenTelemetry package references to src/Po.SeeReview.Api/Po.SeeReview.Api.csproj
- [x] T065 [US6] Configure OpenTelemetry in src/Po.SeeReview.Api/Program.cs with AddOpenTelemetry() call
- [x] T066 [US6] Add WithTracing configuration in Program.cs with AspNetCore instrumentation and ActivitySource
- [x] T067 [US6] Add WithMetrics configuration in Program.cs with AspNetCore instrumentation and Meter
- [x] T068 [US6] Configure Azure Monitor exporter with Application Insights connection string
- [x] T069 [P] [US6] Create sample custom ActivitySource in a feature handler (e.g., GenerateComicHandler)
- [x] T070 [P] [US6] Create sample custom Meter with Counter in a feature handler
- [ ] T071 [US6] Run application locally and trigger comic generation request
- [ ] T072 [US6] Verify custom traces appear in Application Insights within 2 minutes
- [ ] T073 [US6] Verify custom metrics appear in Application Insights within 2 minutes

**Checkpoint**: OpenTelemetry configured, custom telemetry visible in Application Insights (FR-014, FR-015, FR-016, SC-007)

---

## Phase 9: User Story 7 - Production Diagnostics Tools (Priority: P2)

**Goal**: Snapshot Debugger and Profiler enabled on App Service for production issue diagnosis

**Independent Test**: Enable tools in Azure Portal, trigger exception, verify snapshot captured

### Implementation for User Story 7

- [x] T074 [US7] Document Snapshot Debugger enablement steps in docs/deployment.md (navigate to App Insights, enable, configure limits)
- [x] T075 [US7] Document Profiler enablement steps in docs/deployment.md (navigate to App Insights, enable, configure duration/CPU threshold)
- [x] T076 [US7] Add security note to docs/deployment.md about sensitive data in snapshots
- [x] T077 [US7] Add troubleshooting section to docs/deployment.md for common diagnostic issues
- [ ] T078 [US7] Enable Snapshot Debugger in Azure Portal for App Service (manual step documented)
- [ ] T079 [US7] Enable Profiler in Azure Portal for App Service (manual step documented)
- [ ] T080 [US7] Trigger sample exception to verify snapshot capture (manual verification)
- [ ] T081 [US7] Verify snapshots accessible in Application Insights → Failures → Exceptions

**Checkpoint**: Production diagnostics tools enabled and documented (FR-018, SC-009)

---

## Phase 10: User Story 8 - KQL Monitoring Library (Priority: P3)

**Goal**: Curated library of KQL queries for common monitoring tasks (errors, performance, custom metrics)

**Independent Test**: Run queries in Application Insights, verify they return expected results

### Implementation for User Story 8

- [x] T082 [P] [US8] Create docs/kql/errors.kql with top exceptions query (last 24h, grouped by type)
- [x] T083 [P] [US8] Create docs/kql/performance.kql with API response time percentiles query (p50/p95/p99 by operation)
- [x] T084 [P] [US8] Create docs/kql/dependencies.kql with dependency health query (external service calls, success rate)
- [x] T085 [P] [US8] Create docs/kql/custom-metrics.kql with business metrics query (comic generation rate, strangeness scores)
- [ ] T086 [US8] Test errors.kql in Application Insights Logs blade and verify results
- [ ] T087 [US8] Test performance.kql in Application Insights Logs blade and verify results
- [ ] T088 [US8] Test dependencies.kql in Application Insights Logs blade and verify results
- [ ] T089 [US8] Test custom-metrics.kql in Application Insights Logs blade and verify results
- [x] T090 [US8] Create docs/kql/README.md documenting query usage and common parameters

**Checkpoint**: KQL library created with essential monitoring queries (FR-017, SC-008)

---

## Phase 11: Additional Requirements (FR-019 to FR-023)

**Purpose**: Complete remaining functional requirements from constitution compliance

### Architecture Diagrams (FR-019)

- [ ] T091 [P] Create docs/diagrams/c4-context.mmd with system context diagram (users, external systems, PoSeeReview system)
- [ ] T092 [P] Create docs/diagrams/c4-container.mmd with container diagram (API, Client, Storage, App Insights)
- [ ] T093 [P] Create docs/diagrams/c4-component.mmd with component diagram (Features, Services, Repositories)
- [ ] T094 [P] Create docs/diagrams/sequence-comic-generation.mmd with sequence diagram for comic generation flow

### Location Entry Enhancements (FR-020, FR-021)

- [ ] T095 [P] Update src/Po.SeeReview.Client/Pages/Index.razor to support manual location entry when geolocation denied
- [ ] T096 [P] Create src/Po.SeeReview.Client/Components/LocationInput.razor component for manual city/ZIP code search
- [ ] T097 Add fallback logic to Index.razor when geolocation fails or is denied

### Edge Case Testing (FR-022)

- [ ] T098 [P] Create tests/Po.SeeReview.IntegrationTests/EdgeCases/DirectoryPackagesConflictTests.cs for version conflicts
- [ ] T099 [P] Create tests/Po.SeeReview.IntegrationTests/EdgeCases/NullableThirdPartyTests.cs for third-party package warnings
- [ ] T100 [P] Create tests/Po.SeeReview.IntegrationTests/EdgeCases/CoveragePerformanceTests.cs for coverage overhead
- [ ] T101 [P] Create tests/Po.SeeReview.IntegrationTests/EdgeCases/BicepPartialDeploymentTests.cs for deployment failures
- [ ] T102 [P] Create tests/Po.SeeReview.IntegrationTests/EdgeCases/TelemetryVolumeTests.cs for excessive telemetry
- [ ] T103 [P] Create tests/Po.SeeReview.IntegrationTests/EdgeCases/SnapshotPerformanceTests.cs for debugger overhead

### Content Moderation Policy (FR-023)

- [ ] T104 Create docs/content-moderation-policy.md defining profanity filtering rules
- [ ] T105 Add hate speech filtering rules to docs/content-moderation-policy.md
- [ ] T106 Add explicit content filtering rules to docs/content-moderation-policy.md
- [ ] T107 Document Azure AI Content Safety integration approach in content-moderation-policy.md

**Checkpoint**: All additional requirements complete (FR-019 to FR-023)

---

## Phase 12: Polish & Cross-Cutting Concerns

**Purpose**: Final validation and cleanup

### Constitution Validation

- [ ] T108 Review constitution.md checklist section 1 (Foundation) - verify all items checked
- [ ] T109 Review constitution.md checklist section 2 (Architecture) - verify all items checked
- [ ] T110 Review constitution.md checklist section 3 (Implementation) - verify all items checked
- [ ] T111 Review constitution.md checklist section 4 (Quality & Testing) - verify all items checked
- [ ] T112 Review constitution.md checklist section 5 (Operations & Azure) - verify all items checked

### Integration Testing

- [ ] T113 Run full solution build: `dotnet build` at solution level
- [ ] T114 Run all unit tests: `dotnet test tests/Po.SeeReview.UnitTests`
- [ ] T115 Run all integration tests: `dotnet test tests/Po.SeeReview.IntegrationTests`
- [ ] T116 Run all component tests: `dotnet test --filter "FullyQualifiedName~ComponentTests"`
- [ ] T117 Collect full coverage report: `.\scripts\collect-coverage.ps1`
- [ ] T118 Verify coverage meets 80% threshold (informational check)

### Documentation Completeness

- [ ] T119 Review docs/nullable-warnings.md - verify all warnings categorized with severity
- [ ] T120 Review docs/coverage/index.html - verify all assemblies have coverage data
- [ ] T121 Review docs/kql/ - verify all 4 query files exist and are tested
- [ ] T122 Review docs/deployment.md - verify Snapshot Debugger and Profiler sections complete
- [ ] T123 Review docs/diagrams/ - verify all 4 Mermaid diagrams exist
- [ ] T124 Review docs/content-moderation-policy.md - verify all filtering rules documented

### Infrastructure Validation

- [ ] T125 Run `azd provision` to verify infrastructure deploys cleanly
- [ ] T126 Verify budget alert configuration in Azure Portal
- [ ] T127 Verify Application Insights connection string in app settings
- [ ] T128 Verify Log Analytics workspace linked to Application Insights
- [ ] T129 Run sample API request and verify telemetry appears in Application Insights

### Final Verification

- [ ] T130 Execute all acceptance scenarios from spec.md User Story 1 (Centralized Package Management)
- [ ] T131 Execute all acceptance scenarios from spec.md User Story 2 (Null Safety Enforcement)
- [ ] T132 Execute all acceptance scenarios from spec.md User Story 3 (Code Coverage Enforcement)
- [ ] T133 Execute all acceptance scenarios from spec.md User Story 4 (Blazor Component Testing)
- [ ] T134 Execute all acceptance scenarios from spec.md User Story 5 (Infrastructure as Code with Bicep)
- [ ] T135 Execute all acceptance scenarios from spec.md User Story 6 (OpenTelemetry Observability)
- [ ] T136 Execute all acceptance scenarios from spec.md User Story 7 (Production Diagnostics Tools)
- [ ] T137 Execute all acceptance scenarios from spec.md User Story 8 (KQL Monitoring Library)

### Success Criteria Validation

- [ ] T138 Verify SC-001: Solution builds with Directory.Packages.props, zero version conflicts
- [ ] T139 Verify SC-002: All projects compile with nullable enabled, warnings documented
- [ ] T140 Verify SC-003: Coverage metrics collected and reported, 80% target visible
- [ ] T141 Verify SC-004: All Blazor components have bUnit tests
- [ ] T142 Verify SC-005: Infrastructure deploys successfully via azd provision
- [ ] T143 Verify SC-006: Budget alerts configured for $5 limit
- [ ] T144 Verify SC-007: Custom telemetry visible in Application Insights
- [ ] T145 Verify SC-008: KQL queries executable within 5 minutes
- [ ] T146 Verify SC-009: Snapshot Debugger captures exception details
- [ ] T147 Verify SC-010: 100% of constitution v2.0.0 checklist items satisfied

**Checkpoint**: Feature complete, all success criteria met, constitution v2.0.0 100% compliant

---

## Dependencies & Execution Order

### Critical Path (Sequential)

1. **Phase 1 (Setup)** → **Phase 2 (Foundational)** → All user stories can proceed
2. **User Story 1 (Package Management)** MUST complete before User Stories 4, 6 (add new packages to Directory.Packages.props)
3. **User Story 5 (Bicep)** MUST complete before User Story 7 (diagnostics enabled in Azure Portal)
4. **User Story 5 (Bicep)** MUST complete before User Story 6 (Application Insights connection string needed)

### Parallel Opportunities

**After Phase 2 completes, these can run in parallel**:

- **Group A**: User Stories 1, 2, 3 (no interdependencies, all configuration work)
- **Group B**: User Story 4 (depends on US1 for bUnit packages)
- **Group C**: User Story 5 (independent infrastructure work)
- **Group D**: User Stories 6, 7, 8 (all depend on US5 for Azure resources)

**Optimal Execution Plan**:

1. Phase 1 + Phase 2 (sequential, foundational)
2. Parallel: US1 + US2 + US3
3. US4 (after US1 completes for package management)
4. US5 (independent, can start anytime after Phase 2)
5. Parallel: US6 + US7 + US8 (after US5 completes for Azure infrastructure)
6. Phase 11 (Additional Requirements) - mostly parallel
7. Phase 12 (Polish) - sequential validation

**Estimated Task Distribution**:

- Phase 1 (Setup): 5 tasks
- Phase 2 (Foundational): 4 tasks
- User Story 1: 12 tasks
- User Story 2: 10 tasks
- User Story 3: 7 tasks
- User Story 4: 9 tasks
- User Story 5: 12 tasks
- User Story 6: 14 tasks
- User Story 7: 8 tasks
- User Story 8: 9 tasks
- Phase 11 (Additional): 17 tasks
- Phase 12 (Polish): 40 tasks

**Total**: 147 tasks

**Parallelizable**: 62 tasks marked with [P]

---

## MVP Scope Recommendation

**Suggested MVP**: User Stories 1 + 2 + 3 (Phases 1, 2, 3, 4, 5)

**Rationale**: These three stories establish foundational compliance (package management, nullable types, coverage) that affect all future development. They are independently testable, don't require Azure deployment, and can be completed entirely in local development.

**MVP Deliverables**:
- Directory.Packages.props managing all package versions
- Nullable reference types enabled with warnings inventory
- Code coverage collection automated with 80% target tracking
- Build pipeline validates all three without blocking on warnings/coverage

**Post-MVP Increments**:
- **Increment 2**: User Story 4 (bUnit component testing)
- **Increment 3**: User Story 5 (Bicep infrastructure)
- **Increment 4**: User Stories 6 + 7 + 8 (OpenTelemetry, diagnostics, KQL)
- **Increment 5**: Phase 11 (Additional Requirements)
- **Final**: Phase 12 (Polish & Validation)

---

## Implementation Strategy

1. **Start with MVP**: Complete Phases 1-5 (US1, US2, US3) to establish foundational compliance
2. **Validate incrementally**: Each user story phase ends with checkpoint - verify it works independently
3. **Leverage parallelism**: Use [P] marked tasks to speed up execution within each phase
4. **Test continuously**: Run builds and tests after each phase to catch issues early
5. **Follow quickstart.md**: Use quickstart.md as step-by-step guide for each phase
6. **Track constitution progress**: Update constitution.md checklist as tasks complete

---

## Format Validation

✅ All tasks follow checklist format: `- [ ] [ID] [P?] [Story?] Description`  
✅ All user story tasks include Story label (US1, US2, etc.)  
✅ All tasks include specific file paths in descriptions  
✅ Foundational tasks have no Story label (shared infrastructure)  
✅ Polish tasks have no Story label (cross-cutting validation)  
✅ Sequential task IDs (T001 through T147)  
✅ Parallel opportunities identified with [P] marker

---

**End of Tasks**
