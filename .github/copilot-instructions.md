
1. Project Identity & SDK Standards
Unified ID Strategy: Use PoSeeReview as the master identifier for Azure Resource Groups and resources. Use the short prefix PoSeeReview for project namespaces.
Modern SDK Standards: Target .NET 10 and C# 14 exclusively. Enforce Central Package Management (CPM) via Directory.Packages.props with CentralPackageTransitivePinning enabled.
Use context7 mcp to get latest version of .NET 10
Safety Standards: Use Directory.Build.props at the root to enforce <TreatWarningsAsErrors>true</TreatWarningsAsErrors> and <Nullable>enable</Nullable>.
Prohibition: Avoid reflection-based mappers. Use Mapperly for all DTO-to-Entity conversions.

2. Architecture: Flattened Vertical Slice (VSA)
Feature Folders: Keep Endpoints, DTOs, and Business Logic together within a single feature folder. Logic must be self-contained.
Result Pattern: Use the ErrorOr library. Minimal APIs must use the .Match() extension to return TypedResults.
Data Access: Use lightweight expression visitors to map DTO filters directly to the TableClient (Azure Table Storage), keeping slices storage-agnostic without a heavy ORM.

3. API & UI
API UI: Use OpenApi with Scalar for API documentation.
Ports: Use 5000 (HTTP) and 5001 (HTTPS) for local development.
Diagnostics: All apps expose /api/diag with masked secrets (first 3 + last 3 chars shown, middle replaced with ***).
Health Checks: Implement /api/health, /api/health/live, /api/health/ready endpoints to verify all backing services.
CORS: Configure for Blazor WASM client.
Blazor: .NET 10 Unified (SSR + WASM). Prioritize Static SSR; hydrate to Interactive WASM only when necessary.

4. Secret & Configuration Management
Zero-Trust Config: Use the Azure Key Vault Configuration Provider. Secrets are fetched at runtime via Managed Identity.
Local Secrets: Use dotnet user-secrets for local development.
Cloud: Use Azure Key Vault via Managed Identity within subscription Punkouter26 (Bbb8dfbe-9169-432f-9b7a-fbf861b51037).
Shared Resources: Locate common services and secrets in the PoShared resource group.

5. Resilience & Observability
Native Resilience: Apply .AddStandardResilienceHandler() (Polly) to all HttpClient configurations.
Logging: Use Serilog for structured logging. Use LoggerMessage Delegates (Source Generators) where performance is critical.
Telemetry: Enable OpenTelemetry for tracing and custom metrics, exported directly to Azure Monitor (Application Insights).
Health Probes: Use standard MapHealthChecks. Readiness checks include connectivity tests for all backing services.

6. Context Management
Use .copilotignore to exclude bin/, obj/, and node_modules/ from AI focus.
Tooling & Packages: Use Central Package Management (CPM) via Directory.Packages.props with transitive pinning.

7. Development Workflow
Create .http files for API debugging.
Implement robust server/browser logging for function calls.
Apply GoF/SOLID patterns + explanatory comments when possible.
For any major feature, create corresponding Unit/Integration/E2E tests.

8. Testing Strategy
Unit (C#): Pure logic and domain rules using xUnit + Moq.
Integration (C#): API/DB testing using Azurite emulator for Azure Storage.
E2E (Playwright/TS): Headless Chromium for critical paths.
Create http endpoints for all main functions so they can easily be tested via curl or .http files.

9. Infrastructure & Deployment
Provisioning: Use Azure Developer CLI (azd up) with Bicep modules.
CI/CD: GitHub Actions for build + deploy to Azure Container Apps.
Docker: Use multi-stage Dockerfile with sdk:10.0 build + aspnet:10.0 runtime.
