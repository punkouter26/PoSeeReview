# AGENT.MD — PoSeeReview

Context layer for AI agents (NET_RULES 6.8). Update this file whenever architectural
boundaries or project configuration change.

## Solution layout (Vertical Slice Architecture)

```
/src
  PoSeeReview.Api      ASP.NET Core host: serves the Blazor WASM client (same origin, no CORS)
    Features/          One folder per slice: Auth, Comics, Restaurants, Leaderboard,
                       DevSessions, Diagnostics, Takedowns — endpoints + handlers +
                       entities + repositories + services live together per feature
    Storage/           Cross-slice Table/Blob plumbing (TableStorageRepository, BlobStorageService)
    Identity/          ICurrentRequestIdentityAccessor + HttpContext implementation
    Telemetry/         App Insights + OpenTelemetry wiring, RoleNameTelemetryInitializer
    Testing/           AiMockDelegatingHandler boundary (never registered in Production)
  PoSeeReview.Client   Mobile-first Blazor WASM (trimmable, no tokens, Web Audio API)
  PoSeeReview.Shared   DTOs + FluentValidation rules ONLY
/tests
  PoSeeReview.UnitTests           pure unit tests
  PoSeeReview.IntegrationTests    Testcontainers Azurite (ephemeral per factory)
  PoSeeReview.E2EAPI              in-memory host + Testcontainers Azurite, AI mocked
  PoSeeReview.E2EUI               C# Playwright against a live app (E2E_BASE_URL)
```

The former Application/Core/Infrastructure projects were collapsed into the Api project
(2026-07-01). Namespaces were intentionally preserved (`PoSeeReview.Core.*`,
`PoSeeReview.Application.*`, `PoSeeReview.Infrastructure.*`) to keep the move purely
structural; prefer `PoSeeReview.Api.*` namespaces for new code.

## Runtime & configuration

- .NET 10 pinned by `global.json`; CPM via `Directory.Packages.props`;
  `TreatWarningsAsErrors` + `Nullable` global via `Directory.Build.props`.
- Local ports: HTTP 5000 / HTTPS 5001. OpenAPI via `AddOpenApi()` + Scalar UI (Dev).
- `/health` (+ `/live`, `/ready`) and `/diag` (masked keys + integration statuses,
  active in Dev AND Prod; `/diag/mock-status` reports active `IMockable` registrations
  and drives the client's "USING MOCK DATA" banner).
- Secrets: Azure Key Vault `kv-poshared` with `PoSeeReview--` prefix-scoped names via
  `PrefixKeyVaultSecretManager`; `DefaultAzureCredential` everywhere.
- Storage: `AzureStorage:TableEndpoint`/`BlobEndpoint` set → Managed Identity clients
  (Prod); otherwise connection strings (Azurite in Docker, container name `PoSeeReview`
  via docker-compose).

## Authentication (BFF cookie proxy)

- The WASM client never handles tokens. Session = `.PoSeeReview.Auth` cookie
  (HttpOnly, SameSite=Strict, Secure), issued by the Api.
- Routes: `/auth/login/microsoft` (OIDC challenge), `/auth/login/fake` (guest cookie,
  404 in Prod), `/auth/logout`, `/auth/me` (state + sanitized returnUrl).
- OIDC: Entra `/common` authority (work/school + personal accounts), shape-based
  issuer validator against `AzureAd:AllowedTenants`. Callback `/auth/callback/microsoft`
  must be registered in the App Registration web redirect list.
- `FakeAuthHandler` maps `X-Fake-User`/`X-Fake-Roles` headers to a ClaimsPrincipal
  (Dev/Test only; its constructor throws in Production).
- Client: `BffAuthenticationStateProvider` polls `/auth/me`; every page except
  `/login` carries `[Authorize]` behind `AuthorizeRouteView`. Login views: Prod =
  Microsoft only; Dev = Microsoft + guest; Test = forced guest bypass.

## Observability

- Serilog → Console + File + Application Insights (PoShared resource); source-generated
  `[LoggerMessage]` on hot paths.
- `RoleNameTelemetryInitializer` stamps `cloud_RoleName` from the executing assembly
  (Api host only, never in WASM).
- Sampling: full fidelity outside Production; Prod adaptive sampling capped at
  10 items/sec with Exceptions excluded from sampling; Live Metrics (QuickPulse) on.

## Deployment

- GitHub Actions (`.github/workflows/deploy.yml`) builds and deploys to Azure App
  Service `app-poseereview` (resource group `PoSeeReview`, F1 plan via
  `infra/modules/appservice.bicep`). Tests do NOT run in the pipeline.
- System-assigned Managed Identity: Key Vault Secrets User on `kv-poshared` (PoShared
  RG) + Storage RBAC.
- First run: `SCRIPTS/setup.ps1` (WinGet/Docker/Azure CLI checks, build verification).

## Conventions

- Minimal API slices registered through `MapFeatureEndpoints()` →
  `MapGroup()` extension per feature. No controllers.
- C# 14 syntactic density: primary constructors, collection expressions, pattern
  matching; minimal comments.
- Client + Shared: `IsTrimmable` + `EnableTrimAnalyzer`; JSON is source-generated via
  `AppJsonContext` (add new DTOs there). No AOT.
