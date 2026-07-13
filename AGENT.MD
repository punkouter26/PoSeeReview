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
  via docker-compose). `Storage:UseAzurite=true` (appsettings.Development.json)
  short-circuits Key Vault connection strings and pins both clients to the local
  Azurite emulator — without it local dev hits the real storage account.
- Tables and the comics blob container are provisioned once at startup by
  `TableStorageInitializer` (`IHostedService`) — repositories no longer call
  `CreateIfNotExists` per request; startup fails fast if storage is unreachable (§5.6).
- `UseForwardedHeaders` (X-Forwarded-For/Proto, App Service proxy trusted) runs first in
  the pipeline so rate-limiting and request logging see the real client IP.
- Typed HttpClients (`GoogleMaps`, `GeminiApi`) use `AddStandardResilienceHandler`;
  external AI/Maps calls propagate the request `CancellationToken` end to end.
- `Takedowns:ApiKey` gates `/api/takedowns` (503 when unset, timing-safe compare);
  Development uses the placeholder `local-dev-takedown-key` from appsettings.Development.json.

## Authentication (BFF cookie proxy)

- The WASM client never handles tokens. Session = `.PoSeeReview.Auth` cookie
  (HttpOnly, SameSite=Strict, Secure), issued by the Api.
- Routes: `/auth/login/microsoft` (OIDC challenge), `/auth/login/fake` (guest cookie,
  404 in Prod), `/auth/logout`, `/auth/me` (state + sanitized returnUrl).
- OIDC: Entra `/common` authority (work/school + personal accounts), shape-based
  issuer validator against `AzureAd:AllowedTenants`. Callback `/auth/callback/microsoft`
  must be registered in the App Registration web redirect list.
- `FakeAuthHandler` maps `X-Fake-User`/`X-Fake-Roles` headers to a ClaimsPrincipal
  (Dev/Test only; its constructor throws in Production). Integration/E2EAPI test clients
  authenticate by sending `X-Fake-User` (set in their `CustomWebApplicationFactory` /
  test ctor) so they satisfy the fallback policy below.
- Server-side authz is DENY BY DEFAULT (NET_RULES 4.5): a `FallbackPolicy` of
  `RequireAuthenticatedUser` is set in `AddBffAuthentication`. Public endpoints opt out
  with `.AllowAnonymous()`: `/auth`, `/health*`, `/diag`, `/api/devsession`,
  `/api/takedowns` (own X-Api-Key filter), OpenAPI/Scalar, and the SPA `index.html`
  fallback. Business slices (comics/restaurants/leaderboard) require an authenticated
  session. Client `[Authorize]` is UI-only.
- Identity: `HttpContextRequestIdentityAccessor` derives the user id from the cookie
  principal. The `X-Dev-User-Id`/`X-Dev-UserId` header override is honored ONLY outside
  Production (client stamps it only in Dev/Test too) — never trust client headers for
  identity in Prod.
- Client: `BffAuthenticationStateProvider` polls `/auth/me` (memoized; invalidated on
  `NotifyStateChanged`); every page except `/login` carries `[Authorize]` behind
  `AuthorizeRouteView`. Login views: Prod = Microsoft only; Dev = Microsoft + guest;
  Test = forced guest bypass. Client stamps `X-Session-ID` + a per-request
  `X-Correlation-ID` on every API call.

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
