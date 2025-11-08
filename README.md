# SeeReview

**Turning Real Reviews into Surreal Stories** - A playful, story-driven app that transforms restaurant reviews into whimsical visual comic strips.

## What is SeeReview?

SeeReview converts restaurant reviews into entertaining four-panel comic strips using AI. Discover nearby restaurants, analyze their strangest customer experiences, and generate unique comics that capture the essence of dining adventures. Share hilarious stories and discover the weirdest dining experiences from around the world on the global leaderboard.

## Why SeeReview?

Reading restaurant reviews can be entertaining, but SeeReview makes it an immersive visual experience. Instead of scrolling through text, users see real customer experiences transformed into memorable comic strips, making restaurant discovery fun and shareable.

## Quick Start

### Prerequisites

- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) or later
- [Node.js](https://nodejs.org/) (LTS version) for Playwright E2E tests
- [Azurite](https://learn.microsoft.com/azure/storage/common/storage-use-azurite) for local storage emulation
- Google Maps API Key (for restaurant search and reviews)
- Azure OpenAI API Key and Endpoint (for comic generation)

### How the App Works

```
┌─────────────┐      ┌──────────────┐      ┌────────────┐
│   Blazor    │─────▶│  ASP.NET     │─────▶│   Azure    │
│   WASM      │      │  Core API    │      │  Services  │
│  (Frontend) │◀─────│  (Backend)   │◀─────│  (Storage, │
└─────────────┘      └──────────────┘      │  OpenAI)   │
                                            └────────────┘
```

**Frontend**: Blazor WebAssembly app running in the browser  
**Backend**: ASP.NET Core 9.0 Web API serving data and orchestrating services  
**Storage**: Azure Table Storage (comics, leaderboard) + Blob Storage (images)  
**AI**: Azure OpenAI (GPT-4o-mini for narrative, DALL-E 3 for images)  
**Data Source**: Google Maps Places API (New) for restaurant search and reviews

### Running the Application

#### 1. Clone and Navigate to the Project

```powershell
git clone <repository-url>
cd PoSeeReview
```

#### 2. Start Azurite (Local Storage Emulator)

Open a new terminal and run:

```powershell
# Start Azurite with configuration
azurite --location ./AzuriteConfig --debug ./AzuriteConfig/debug.log
```

Keep this terminal running. Azurite provides local Azure Table Storage and Blob Storage.

#### 3. Configure User Secrets

Set up required API keys and connection strings using .NET User Secrets (keeps sensitive data out of source control):

```powershell
cd src/Po.SeeReview.Api

# Azure Storage Connection (local Azurite)
dotnet user-secrets set "AzureStorage:ConnectionString" "UseDevelopmentStorage=true"

# Google Maps API
dotnet user-secrets set "GoogleMaps:ApiKey" "YOUR_GOOGLE_MAPS_API_KEY"

# Azure OpenAI
dotnet user-secrets set "AzureOpenAI:Endpoint" "https://YOUR_RESOURCE.openai.azure.com/"
dotnet user-secrets set "AzureOpenAI:ApiKey" "YOUR_AZURE_OPENAI_KEY"
dotnet user-secrets set "AzureOpenAI:DeploymentName" "gpt-4o-mini"
dotnet user-secrets set "AzureOpenAI:DalleDeploymentName" "dall-e-3"

# Application Insights (optional for local dev)
dotnet user-secrets set "ApplicationInsights:ConnectionString" "YOUR_APP_INSIGHTS_CONNECTION_STRING"

cd ../..
```

**Getting API Keys:**
- **Google Maps API**: [Get started with Google Maps Platform](https://developers.google.com/maps/get-started)
- **Azure OpenAI**: [Create an Azure OpenAI resource](https://learn.microsoft.com/azure/ai-services/openai/how-to/create-resource)

#### 4. Run the Application

```powershell
# From the solution root
dotnet run --project src/Po.SeeReview.Api
```

The application will start:
- **API**: `https://localhost:5001`
- **Blazor WASM**: Same URL (served by API)
- **Swagger UI**: `https://localhost:5001/swagger`

Open your browser to the URL displayed in the console.

#### 5. Verify Application Health

Navigate to `/diag` to see the health status of all application dependencies.

#### 6. Request Requirements & Limits

- Attach a browser-style `User-Agent` header when exercising APIs (non-browser clients without one receive `400`).
- Rate limiting allows 60 requests per minute per client IP; exceeding the limit returns `429` with `Retry-After: 60` seconds.
- Health check and takedown endpoints are subject to the same safeguards.

### Development Workflow

```powershell
# Restore all packages
dotnet restore

# Build entire solution
dotnet build

# Run all tests (locally only)
dotnet test

# Format code
dotnet format

# Run E2E tests (Playwright)
cd tests/e2e
npm install
npx playwright test
```

## Azure Deployment

Deploy to Azure using **Azure Developer CLI (azd)**:

```powershell
# Install Azure Developer CLI
# https://learn.microsoft.com/azure/developer/azure-developer-cli/install-azd

# Login to Azure
azd auth login

# Provision + Deploy (first time)
azd up

# Deploy code changes only
azd deploy

# View logs
azd monitor --logs

# Delete all resources
azd down
```

**Detailed Infrastructure Documentation**: See [infra/README.md](./infra/README.md)
**App Service Deployment Playbook**: See [docs/deployment.md](./docs/deployment.md)

The `azd up` command provisions:
- Application Insights + Log Analytics Workspace
- Azure Storage (Table + Blob)
- Azure Key Vault (with Managed Identity)
- App Service Plan (F1 for dev, S1 for prod)
- App Service (API) with health monitoring
- Static Web App (Blazor Client)

All secrets (Azure OpenAI, Google Maps API keys) are stored in Key Vault and referenced via App Service configuration.


### App Structure Summary

```
src/
├── Po.SeeReview.Api/            # ASP.NET Core 9.0 Web API
│   ├── Controllers/             # API endpoints (incl. takedowns)
│   ├── Middleware/              # Request logging, error handling, UA filtering
│   └── Program.cs               # App configuration, rate limits & hosted jobs
│
├── Po.SeeReview.Client/         # Blazor WebAssembly frontend
│   ├── Components/              # Reusable Blazor components
│   ├── Pages/                   # Page components (routes)
│   ├── Services/                # Client-side services
│   └── wwwroot/                 # Static files, CSS, JS
│
├── Po.SeeReview.Core/           # Domain entities and interfaces
│   ├── Entities/                # Core business entities
│   └── Interfaces/              # Repository/service contracts
│
├── Po.SeeReview.Infrastructure/ # External service implementations
│   ├── Repositories/            # Azure Table/Blob storage
│   ├── Services/                # Google Maps, OpenAI integrations
│   └── ServiceCollectionExtensions.cs
│
└── Po.SeeReview.Shared/         # Shared DTOs between Client and API
    └── Dtos/                    # Data Transfer Objects

tests/
├── Po.SeeReview.UnitTests/      # Unit tests (XUnit + Moq)
├── Po.SeeReview.IntegrationTests/ # Integration tests (WebApplicationFactory)
└── e2e/                         # End-to-end tests (Playwright + TypeScript)
```

### Project Descriptions

#### Source Projects (`src/`)

- **Po.SeeReview.Api**: The ASP.NET Core Web API that hosts the Blazor WASM app and provides REST endpoints. Includes health checks, logging middleware (Serilog), Application Insights integration, rate limiting, user-agent filtering, background cleanup jobs, and Swagger/OpenAPI documentation.

- **Po.SeeReview.Client**: The Blazor WebAssembly frontend application. Provides the user interface for discovering restaurants, viewing comics, and interacting with the global leaderboard. Runs entirely in the browser.

- **Po.SeeReview.Core**: Domain layer containing core business entities (Restaurant, Review, Comic, LeaderboardEntry) and interfaces. No external dependencies—pure business logic.

- **Po.SeeReview.Infrastructure**: Infrastructure layer implementing external integrations. Includes repositories for Azure Table/Blob Storage, services for Google Maps API and Azure OpenAI, and dependency injection configuration.

- **Po.SeeReview.Shared**: Shared Data Transfer Objects (DTOs) used for communication between the Blazor Client and the API. Ensures type-safe serialization.

#### Test Projects (`tests/`)

- **Po.SeeReview.UnitTests**: XUnit-based unit tests with Moq for mocking. Tests business logic in isolation. Categorized with `[Trait]` attributes. Runs fast (<1s per test).

- **Po.SeeReview.IntegrationTests**: Integration tests using `WebApplicationFactory` and in-memory Azurite persistence. Tests the full API pipeline including middleware, controllers, and data access.

- **e2e/**: Playwright-based end-to-end tests written in TypeScript. Tests complete user workflows in Chromium (desktop and mobile views). Includes accessibility (a11y) and visual regression testing.

### Operational Notes

- **Expired comic cleanup**: Background service purges cached comics and blobs every 30 minutes by default. Override cadence via `Cleanup:ExpiredComicIntervalMinutes` and batch size with `Cleanup:ExpiredComicBatchSize`.
- **Resiliency policies**: Azure OpenAI REST and image generation clients run with Polly retries and Application Insights metrics (`AzureOpenAI.Requests`, `Comics.Generation.DurationMs`, etc.).
- **Takedown workflow**: `POST /api/takedowns` accepts a `TakedownRequestDto`, immediately removes cached assets, and emits `TakedownRequestReceived` telemetry for follow-up.
- **Request telemetry**: `RequestLoggingMiddleware` enriches Application Insights with correlation IDs and rate-limit events so you can diagnose client spikes quickly.

## Documentation

- **[Product Requirements (PRD.md)](./docs/PRD.md)**: Detailed features, user stories, and business goals
- **[Development Steps](./docs/STEPS.md)**: Phase-by-phase implementation guide
- **[Feature Specification](./specs/001-review-comic-app/spec.md)**: Complete technical specification
- **[Implementation Plan](./specs/001-review-comic-app/plan.md)**: Detailed implementation roadmap
- **[Architecture Decision Records (ADRs)](./docs/adr/)**: Key architectural decisions and rationale
- **[Diagrams](./docs/diagrams/)**: C4 Context, Container, and Sequence diagrams

## Technology Stack

### Backend
- **.NET 9.0 SDK** (latest patch): ASP.NET Core Web API
- **Serilog**: Structured logging with Application Insights sink
- **Swashbuckle**: OpenAPI 3.0 / Swagger documentation
- **Azure.Data.Tables**: Table Storage client
- **Azure.Storage.Blobs**: Blob Storage client
- **Azure.AI.OpenAI**: Azure OpenAI integration

### Frontend
- **Blazor WebAssembly**: Client-side SPA framework
- **Microsoft Fluent UI**: Modern, accessible component library
- **Application Insights JS SDK**: Client-side telemetry

### Testing
- **XUnit**: Unit and integration testing
- **Moq**: Mocking framework
- **WebApplicationFactory**: In-memory API testing
- **bUnit**: Blazor component testing
- **Playwright**: End-to-end browser automation
- **Coverlet**: Code coverage

### Infrastructure
- **Azure Table Storage**: Comics and leaderboard data
- **Azure Blob Storage**: Comic strip images
- **Azure OpenAI**: GPT-4o-mini (narrative) + DALL-E 3 (images)
- **Google Maps Places API**: Restaurant search and reviews
- **Application Insights**: Telemetry and monitoring
- **Azurite**: Local storage emulation for development

## Development Standards

- **Nullable Reference Types**: Enabled in all projects (`<Nullable>enable</Nullable>`)
- **Central Package Management**: All package versions managed in `Directory.Packages.props`
- **.NET SDK Version**: Locked to 9.0.x (latest patch) via `global.json`
- **Code Formatting**: Enforced via `dotnet format` (must pass before commit)
- **API Documentation**: XML comments + Swagger UI
- **Logging**: Serilog with structured logging, Application Insights integration
- **Health Checks**: `/api/health` endpoint with custom dependency checks
- **User Secrets**: Sensitive configuration kept out of source control

## License

MIT

---

**Questions?** Check the [PRD](./docs/PRD.md) for detailed requirements or the [spec](./specs/001-review-comic-app/spec.md) for technical details.
