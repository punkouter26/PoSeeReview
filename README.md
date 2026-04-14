# PoSeeReview

PoSeeReview turns unusual restaurant reviews into four-panel comics and publishes ranked results to a global Hall of Fame leaderboard. The solution runs as Blazor WebAssembly and .NET 10 API in one Azure App Service, with Azure Storage for persistence and external AI services for narrative and image generation.

## Consolidated Architecture Overview

- Edge Delivery: browser client, static WASM assets, and same-origin host.
- Compute Tier: API endpoints, middleware safeguards, background cleanup, telemetry.
- Data Persistence: Azure Table/Blob storage plus Key Vault-backed configuration.
- External Intelligence: Google Maps Places, Azure OpenAI, and Gemini image generation.

## Documentation Suite

### Master diagrams

- [docs/Architecture_MASTER.mmd](./docs/Architecture_MASTER.mmd)
- [docs/DataLifecycle_MASTER.mmd](./docs/DataLifecycle_MASTER.mmd)
- [docs/DataModel.mmd](./docs/DataModel.mmd)
- [docs/SystemFlow_MASTER.mmd](./docs/SystemFlow_MASTER.mmd)
- [docs/MultiplayerFlow.mmd](./docs/MultiplayerFlow.mmd)

### Simplicity variants

- [docs/Architecture_MASTER_SIMPLE.mmd](./docs/Architecture_MASTER_SIMPLE.mmd)
- [docs/DataLifecycle_MASTER_SIMPLE.mmd](./docs/DataLifecycle_MASTER_SIMPLE.mmd)
- [docs/DataModel_SIMPLE.mmd](./docs/DataModel_SIMPLE.mmd)
- [docs/SystemFlow_MASTER_SIMPLE.mmd](./docs/SystemFlow_MASTER_SIMPLE.mmd)
- [docs/MultiplayerFlow_SIMPLE.mmd](./docs/MultiplayerFlow_SIMPLE.mmd)

### Visual assets

- [docs/screenshots](./docs/screenshots) is reserved for UI screenshots and flow captures.

## Refactor Blast Radius Assessment

### Refactor: Replace direct provider clients with a single orchestration service

- Purpose: centralize Google Maps, LLM, and image-model execution with explicit retries, fallback policy, and correlation IDs.
- Blast radius:
  - API layer: changes to service registration and endpoint call paths.
  - Infrastructure layer: adapter wrappers around existing provider SDK clients.
  - Telemetry: metric names and traces may shift to orchestration-centric spans.
  - Tests: unit/integration tests mocking provider dependencies must be updated.
  - Downstream dependencies: provider quota behavior and timeout strategy become coupled to orchestrator policy.

### Refactor: Introduce projection worker for leaderboard materialization

- Purpose: decouple ranking updates from request path to reduce p95 latency.
- Blast radius:
  - API layer: request handlers move from synchronous projection writes to enqueue-only behavior.
  - Data layer: adds queue/table checkpoint records and idempotency keys.
  - UI layer: leaderboard eventual-consistency window must be communicated.
  - Operations: requires new health checks and backlog alerts.
  - Downstream dependencies: consumers reading immediate ranks may observe delayed updates.

### Refactor: Add versioned state machine for comic lifecycle

- Purpose: make status transitions explicit and enforceable across create, publish, expire, and takedown flows.
- Blast radius:
  - Core model: enum/state transition rules and validation constraints change.
  - API contracts: response fields may include state version and transition metadata.
  - Storage schema: migration required for lifecycle version and transition audit fields.
  - Background jobs: cleanup/takedown workers must honor state-machine gates.
  - Downstream dependencies: reporting and moderation tools must map to new state values.

### Refactor: Introduce anti-corruption DTO boundary between API and shared client contracts

- Purpose: reduce accidental coupling between persistence entities and external payloads.
- Blast radius:
  - API endpoints: mapping logic added for read/write DTO translations.
  - Client app: contract changes may require adapter updates in typed services.
  - Tests: snapshot and serialization tests need baseline refresh.
  - Performance: additional mapping allocations may require profiling.
  - Downstream dependencies: any external consumer of existing DTO shape may need migration coordination.

## Build and Run

```powershell
dotnet restore
dotnet build PoSeeReview.sln
dotnet run --project src/Po.SeeReview.Api --launch-profile https
```

