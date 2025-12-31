

1. Project Identity & SDK Standards
Unified ID Strategy: Use the Solution name (e.g., PoAppName) as the master identifier for Azure Resource Groups and ACA environments. Use the short prefix (PoApp) for project namespaces.
Modern SDK Standards: Target .NET 10 and C# 14 exclusively. Enforce Central Package Management (CPM) via Directory.Packages.props with CentralPackageTransitivePinning enabled.
Use context7 mcp to get latest version of .NET 10
AOT-First Syntax: * Enable <IsAotCompatible>true</IsAotCompatible> and <TreatWarningsAsErrors>true</TreatWarningsAsErrors>.
Use Primary Constructors, Collection Expressions, and the field keyword.
Use Extension Blocks (extension keyword) for domain logic.
Prohibition: Avoid reflection-based mappers. Use Mapperly for all DTO-to-Entity conversions.
2. Orchestration & Inner-Loop (The Aspire Way)
Aspire Ecosystem: Use dotnet new aspire-starter. The AppHost is the source of truth for local orchestration and service discovery.
Dynamic Endpoints: Avoid hardcoded ports in launchsettings.json. Rely on Aspire’s named references (e.g., builder.AddProject<Projects.MyApi>("api")) for internal networking.
Developer Command Center: Enhance the Aspire Dashboard by adding Custom Resource Actions to the AppHost (e.g., "Seed Database," "Clear Cache," "Reset Azurite").
Startup & Persistence: Use .WaitFor(resource) for sequencing and .WithLifetime(ContainerLifetime.Persistent) for infrastructure (databases/Redis) to eliminate cold-start delays.
3. Architecture: Flattened Vertical Slice (VSA)
Feature Folders: Keep Endpoints, DTOs, and Business Logic together within a single feature folder. Logic must be self-contained.
Result Pattern: Use the ErrorOr library. Minimal APIs must use the .Match() extension to return TypedResults.
Data Access: Use lightweight expression visitors to map DTO filters directly to the TableClient (Azure Table Storage), keeping slices storage-agnostic without a heavy ORM.
Logic Enforcement: Use NetArchTest to automatically fail builds if architectural rules (e.g., prohibited dependencies) are breached.
4. UI & Security (BFF Pattern)
Secure BFF: The API acts as the security proxy for the Blazor WASM client via YARP.
Cookie-Only Security: The WASM client handles Secure Cookies only; it never touches JWTs.
Rendering & Hydration: * Prioritize Static SSR for initial loads; hydrate to Interactive WASM only when necessary.
Use [PersistentComponentState] to eliminate flickering.
State Management: Use standard Component Parameters for parent-child flow. Use a Scoped StateContainer only for truly global cross-page state (e.g., User Preferences).
Dev Proxy: In development, let the AppHost proxy traffic to the Blazor dev server to preserve hot-reload functionality.
5. Secret & Configuration Management
Zero-Trust Config: Use the Azure Key Vault Configuration Provider. Secrets are fetched at runtime via Managed Identity.
No Hardcoded References: Remove @Microsoft.KeyVault(...) from App Service/ACA settings. Secrets should be transparent to the environment variables, managed via the DefaultAzureCredential in code.
Local Secrets: Use user-secrets locally and Aspire .RunAsEmulator() for storage.
6. Resilience & Observability
Native Resilience: Apply .AddStandardResilienceHandler() (Polly) to all HttpClient and Storage client configurations in ServiceDefaults.
Source-Generated Logging: Use LoggerMessage Delegates (Source Generators) instead of Serilog where performance is critical. Follow a "Context-First" policy.
Health Probes: Use standard MapHealthChecks("/health"). Ensure Readiness checks include connectivity tests for all backing services.
Telemetry: Enable OpenTelemetry for tracing and custom metrics, exported directly to Azure Monitor (Application Insights).
7. Infrastructure & Deployment
Provisioning: Use Azure Developer CLI (azd up) to generate Bicep modules from the Aspire model.


