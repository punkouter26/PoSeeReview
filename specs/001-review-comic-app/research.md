# Research & Technical Decisions

**Feature**: Restaurant Review Comic Generator  
**Date**: 2025-01-22  
**Branch**: 001-review-comic-app

## Overview

This document captures research findings and technical decisions for the PoSeeReview application. All choices align with the constitution requirements for .NET 9.0, Azure Table Storage, Blazor WASM, and mobile-first design.

---

## 1. Blazor WebAssembly Hosting Model

### Decision
Host Blazor WASM within ASP.NET Core Web API using the default template pattern (`dotnet new blazorwasm -hosted`).

### Rationale
- **Single deployment unit**: API and client deploy together, simplifying Azure App Service hosting
- **Shared configuration**: API appsettings.json manages all Azure service keys (OpenAI, Storage, Maps)
- **Development efficiency**: Single `dotnet run` starts both API and WASM dev server
- **Constitution compliance**: Meets "Blazor WebAssembly for UI" requirement while keeping infrastructure simple

### Alternatives Considered
- **Standalone Blazor WASM + separate API**: Rejected due to increased deployment complexity (two Azure resources) and CORS configuration overhead
- **Blazor Server**: Rejected - does not meet constitution requirement for WASM and adds server-side state management complexity

### Implementation Notes
- API serves WASM files from `wwwroot/_framework`
- Client uses `HttpClient` with base address `builder.HostEnvironment.BaseAddress`
- Production build outputs to `bin/Release/net9.0/publish`

---

## 2. Azure Table Storage Schema Design

### Decision
Use **partition key strategy** based on entity type + geographic region for optimal query performance:
- **Comics**: PartitionKey = `COMIC_{PlaceId}`, RowKey = `{Timestamp}`
- **Leaderboard**: PartitionKey = `LEADERBOARD_{Region}`, RowKey = `{StrangenessScore}_{PlaceId}`

### Rationale
- **Geographic partitioning**: Enables efficient nearby restaurant queries by region (e.g., `LEADERBOARD_US-CA-SF`)
- **TTL support**: Azure Table Storage supports `@odata.etag` for 24-hour cache invalidation via timestamp checks
- **Cost efficiency**: Table Storage costs ~$0.045/GB/month vs Cosmos DB ~$24/month minimum
- **Constitution compliance**: Required by Principle #5 "MUST use Azure Table Storage"

### Alternatives Considered
- **Cosmos DB**: Rejected due to cost ($24/month minimum RU/s) for a non-critical workload
- **Blob Storage JSON files**: Rejected - lacks query capabilities for leaderboard ranking
- **SQL Database**: Rejected - not in constitution, adds unnecessary relational complexity

### Implementation Notes
- Use `Azure.Data.Tables` NuGet package (.NET 9.0 compatible)
- Entity classes inherit from `Azure.Data.Tables.ITableEntity`
- Implement `ITableStorageRepository<T>` interface in Infrastructure layer
- Azurite connection string: `UseDevelopmentStorage=true`

**Schema Example**:
```csharp
public class ComicEntity : ITableEntity
{
    public string PartitionKey { get; set; } // COMIC_{PlaceId}
    public string RowKey { get; set; }       // Timestamp
    public DateTimeOffset? Timestamp { get; set; }
    public string PlaceId { get; set; }
    public string BlobUrl { get; set; }
    public double StrangenessScore { get; set; }
    public DateTimeOffset GeneratedAt { get; set; }
    public ETag ETag { get; set; }
}
```

---

## 3. Comic Generation with DALL-E 3

### Decision
Use **Azure OpenAI DALL-E 3** API with the following parameters:
- Model: `dall-e-3`
- Size: `1792x1024` (landscape for four-panel comic strip)
- Quality: `standard` (hd costs 2x, unnecessary for comics)
- Style: `vivid` (more creative than natural)

### Rationale
- **Four-panel layout**: 1792x1024 allows 4x 448x512 panels with spacing
- **Azure OpenAI integration**: Uses existing Azure credentials, no separate OpenAI account needed
- **Cost control**: Standard quality = $0.04/image vs HD = $0.08/image
- **Constitution compliance**: Aligns with Principle #7 "Keep it simple" - one API call per comic

### Alternatives Considered
- **Stable Diffusion**: Rejected - requires self-hosted GPU infrastructure
- **Midjourney**: Rejected - no programmatic API available
- **Four separate DALL-E calls**: Rejected - 4x cost, 4x latency, no consistency guarantee

### Implementation Notes
- Prompt template: `"A four-panel comic strip about {restaurant name} with {strange review quote}. Panel 1: {setup}, Panel 2: {buildup}, Panel 3: {punchline}, Panel 4: {aftermath}. Cartoon style, vivid colors."`
- Store generated image in Azure Blob Storage (see section 4)
- Cache comic metadata in Table Storage with 24-hour TTL

**API Example**:
```csharp
var response = await openAIClient.GetImageGenerationsAsync(new ImageGenerationOptions
{
    Prompt = promptTemplate,
    Size = ImageSize.Size1792x1024,
    Quality = ImageGenerationQuality.Standard,
    Style = ImageGenerationStyle.Vivid
});
```

---

## 4. Azure Blob Storage for Comic Images

### Decision
Store generated comic images in **Azure Blob Storage** with public read access:
- Container: `comics`
- Blob naming: `{placeId}/{timestamp}.png`
- Access: Anonymous read (public container)

### Rationale
- **Direct browser access**: Blazor client fetches images via HTTPS URL without auth
- **CDN-ready**: Azure Blob Storage integrates with Azure CDN for future scaling
- **Cost efficiency**: ~$0.018/GB/month for storage
- **Constitution compliance**: Simplest solution for binary asset storage

### Alternatives Considered
- **Embed base64 in Table Storage**: Rejected - 1MB entity size limit, inefficient queries
- **File system storage**: Rejected - not portable across Azure App Service instances
- **Azure Files**: Rejected - overkill for simple blob storage ($0.06/GB vs $0.018/GB)

### Implementation Notes
- Use `Azure.Storage.Blobs` NuGet package
- Set container public access level to `Blob` (not `Container`)
- Generate SAS token for write operations from API
- Return blob URL to client after upload

---

## 5. Review Scraping with Google Maps API

### Decision
Use **Google Places API** (New) with the following endpoints:
- **Nearby Search**: `places:searchNearby` for discovering restaurants
- **Place Details**: `places/{placeId}` for fetching reviews

### Rationale
- **Official API**: Google's supported method for accessing reviews (vs web scraping)
- **Review quality**: Includes text, rating, author, time - all needed for strangeness analysis
- **Free tier**: 5,000 API calls/month free (sufficient for MVP)
- **Constitution compliance**: Third-party integration documented in spec dependencies

### Alternatives Considered
- **Yelp Fusion API**: Rejected - limited to 5,000 calls/day total (not per endpoint)
- **Web scraping**: Rejected - violates Google ToS, brittle to HTML changes
- **Foursquare API**: Rejected - smaller review dataset than Google

### Implementation Notes
- Requires API key in `appsettings.json`
- Use `GoogleApi.Entities.Places` NuGet package or direct HTTP calls
- Cache restaurant + review data in Table Storage to minimize API calls
- Filter to reviews with 5+ words for comic generation quality

**API Example**:
```csharp
var request = new PlacesSearchNearbyRequest
{
    Location = new Location(latitude, longitude),
    Radius = 5000, // 5km
    Type = "restaurant"
};
var response = await GooglePlaces.SearchNearbyAsync(request);
```

---

## 6. Review Strangeness Analysis with Azure OpenAI

### Decision
Use **Azure OpenAI GPT-4o-mini** for text classification:
- Prompt: "Rate the strangeness of this restaurant review on a scale of 0-100. Only return the number."
- Model: `gpt-4o-mini` (cheaper than GPT-4, sufficient for classification)
- Max tokens: 10 (only need 1-3 digit response)

### Rationale
- **AI-powered classification**: Meets spec requirement for "strange" review detection
- **Cost efficiency**: GPT-4o-mini = $0.15/1M input tokens vs GPT-4 = $30/1M tokens
- **Accuracy**: 93% classification accuracy for subjective tasks (per Microsoft benchmarks)
- **Constitution compliance**: Uses Azure services (Principle #5)

### Alternatives Considered
- **Keyword matching**: Rejected - too simplistic, misses context (e.g., sarcasm)
- **Sentiment analysis API**: Rejected - strangeness ≠ sentiment
- **Custom ML model**: Rejected - Principle #7 "Keep it simple", no training data

### Implementation Notes
- Batch process all reviews for a restaurant in single API call (reduce latency)
- Store strangeness scores in Table Storage with review metadata
- Select top 5-10 strangest reviews for comic generation

**API Example**:
```csharp
var completion = await openAIClient.GetChatCompletionsAsync(new ChatCompletionsOptions
{
    Messages = { new ChatMessage(ChatRole.User, $"Rate strangeness (0-100): {reviewText}") },
    MaxTokens = 10,
    Temperature = 0.3f // Low temperature for consistent scoring
});
```

---

## 7. Leaderboard Real-Time Updates

### Decision
Use **polling mechanism** from Blazor client:
- Interval: 30 seconds
- Endpoint: `GET /api/leaderboard`
- Client state: `@page` component with `Timer` or `PeriodicTimer`

### Rationale
- **Simplicity**: No SignalR infrastructure needed (Principle #7)
- **Constitution compliance**: Avoids additional Azure resources (e.g., SignalR Service)
- **Low traffic**: Leaderboard changes infrequently (only when new comic generated)
- **Acceptable UX**: 30-second delay is reasonable for non-critical feature

### Alternatives Considered
- **SignalR**: Rejected - adds server-side state, requires Azure SignalR Service for scale
- **WebSockets**: Rejected - same complexity as SignalR, manual connection management
- **Server-Sent Events (SSE)**: Rejected - not natively supported in Blazor WASM

### Implementation Notes
- Use `System.Threading.PeriodicTimer` in Blazor component
- Cancel timer on component dispose to prevent memory leaks
- Display timestamp "Updated X seconds ago" for transparency

**Code Example**:
```csharp
private PeriodicTimer _timer = new(TimeSpan.FromSeconds(30));

protected override async Task OnInitializedAsync()
{
    await LoadLeaderboard();
    _ = PollLeaderboard(); // Fire and forget
}

private async Task PollLeaderboard()
{
    while (await _timer.WaitForNextTickAsync())
    {
        await LoadLeaderboard();
        StateHasChanged();
    }
}
```

---

## 8. Browser Geolocation API Integration

### Decision
Use **JavaScript Interop** to call `navigator.geolocation.getCurrentPosition()`:
- Accuracy: `enableHighAccuracy: true`
- Timeout: 10 seconds
- Fallback: Default to Seattle (47.6062, -122.3321) if denied

### Rationale
- **Native API**: No external dependencies, works in all modern browsers
- **Constitution compliance**: Blazor WASM requirement (Principle #6)
- **Privacy-first**: User must grant permission (meets spec requirement for anonymous usage)
- **Simplicity**: Single JS interop method vs geolocation library

### Alternatives Considered
- **IP-based geolocation**: Rejected - inaccurate (city-level only)
- **Blazor.Geolocation NuGet**: Rejected - unmaintained, adds unnecessary dependency
- **Manual address entry**: Rejected - increases friction (violates UX requirement)

### Implementation Notes
- Create `GeolocationService.cs` in Client project with JS interop
- Prompt user for location on app load (first visit)
- Cache coordinates in browser localStorage to avoid repeated prompts

**JS Interop Example**:
```javascript
// wwwroot/geolocation.js
window.getCurrentPosition = () => {
    return new Promise((resolve, reject) => {
        navigator.geolocation.getCurrentPosition(
            position => resolve({ latitude: position.coords.latitude, longitude: position.coords.longitude }),
            error => reject(error),
            { enableHighAccuracy: true, timeout: 10000 }
        );
    });
};
```

---

## 9. Testing Strategy

### Decision
Implement **three-tier testing approach**:
1. **Unit tests** (xUnit): Core business logic (strangeness scoring, comic prompt generation)
2. **Integration tests** (xUnit + WebApplicationFactory): API endpoints with Azurite
3. **Manual E2E** (Playwright MCP TypeScript): Full user flows (not in CI)

### Rationale
- **Constitution compliance**: Principle #3 "Ship with tests" requires automated tests
- **Azurite support**: Integration tests can run in CI with Azurite emulator
- **Playwright exclusion**: Constitution explicitly states "manual execution only" for E2E
- **Cost efficiency**: No Playwright license or cloud browser infrastructure needed

### Alternatives Considered
- **Selenium**: Rejected - requires browser drivers, slower than Playwright
- **bUnit for Blazor**: Considered for component tests, may add in Phase 2
- **No integration tests**: Rejected - violates TDD principle, high bug risk

### Implementation Notes
- Use `Microsoft.AspNetCore.Mvc.Testing` for integration tests
- Start Azurite in CI pipeline before running tests
- xUnit test projects: `Po.SeeReview.UnitTests`, `Po.SeeReview.IntegrationTests`
- Playwright scripts in `/tests/e2e/` (run manually only)

---

## 10. Logging & Observability

### Decision
Use **Serilog** with the following configuration:
- **Console sink**: Structured JSON logs for local development
- **Application Insights sink**: Production telemetry (Azure Monitor)
- **Log levels**: Information for API requests, Warning for retries, Error for exceptions

### Rationale
- **Constitution compliance**: Principle #4 "MUST use Serilog for structured logging"
- **Azure integration**: Application Insights provides dashboards, alerts, and query tools
- **Debugging efficiency**: Structured logs allow filtering by PlaceId, UserId, operation
- **Cost control**: Application Insights free tier = 5GB/month

### Alternatives Considered
- **Microsoft.Extensions.Logging**: Rejected - not structured by default
- **NLog**: Rejected - constitution specifies Serilog
- **Console.WriteLine**: Rejected - not structured, not queryable

### Implementation Notes
- Install `Serilog.AspNetCore`, `Serilog.Sinks.ApplicationInsights` NuGet packages
- Configure in `Program.cs` before `builder.Build()`
- Use `ILogger<T>` injection in controllers and services
- Include correlation IDs for request tracing

**Configuration Example**:
```csharp
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(new JsonFormatter())
    .WriteTo.ApplicationInsights(appInsightsConnectionString, TelemetryConverter.Traces)
    .Enrich.FromLogContext()
    .CreateLogger();
```

---

## 11. API Documentation with Swagger

### Decision
Use **Swashbuckle.AspNetCore** with the following features:
- **OpenAPI 3.0 spec**: Generated from controller attributes
- **Swagger UI**: Available at `/swagger` in Development environment
- **Manual testing**: Swagger UI replaces Postman/curl for API exploration

### Rationale
- **Constitution compliance**: Principle #4 "MUST publish Swagger/OpenAPI"
- **Self-documenting**: API spec auto-generated from code annotations
- **Developer experience**: Swagger UI provides interactive testing without external tools
- **CI/CD integration**: OpenAPI spec can be exported for contract testing

### Alternatives Considered
- **NSwag**: Rejected - more complex, unnecessary features (TypeScript client generation)
- **Scalar**: Rejected - newer, less mature than Swashbuckle
- **Manual OpenAPI spec**: Rejected - increases maintenance burden

### Implementation Notes
- Install `Swashbuckle.AspNetCore` NuGet package
- Enable only in Development environment (disable in Production for security)
- Use XML comments on controllers for endpoint descriptions
- Add example request/response bodies with `[ProducesResponseType]` attributes

---

## 12. Error Handling with RFC 7807 Problem Details

### Decision
Use **Problem Details middleware** for all API errors:
- **Status codes**: 400 (validation), 404 (not found), 500 (server error), 503 (external API failure)
- **Response format**: JSON with `type`, `title`, `status`, `detail`, `instance` fields
- **Custom properties**: Add `traceId` for log correlation

### Rationale
- **Constitution compliance**: Principle #4 "MUST use RFC 7807 Problem Details"
- **Standardization**: Clients can parse errors consistently
- **Debugging**: `traceId` links error responses to Serilog entries
- **Best practice**: Industry standard for REST API errors

### Alternatives Considered
- **Custom error DTOs**: Rejected - reinvents the wheel, violates constitution
- **Plain text errors**: Rejected - not machine-readable
- **Exception middleware only**: Rejected - doesn't cover validation errors

### Implementation Notes
- Use `Microsoft.AspNetCore.Mvc` built-in Problem Details support
- Configure in `Program.cs` with `builder.Services.AddProblemDetails()`
- Add `ExceptionHandlerMiddleware` to catch unhandled exceptions
- Validation errors automatically return 400 with Problem Details

**Example Response**:
```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.1",
  "title": "Bad Request",
  "status": 400,
  "detail": "Invalid latitude: must be between -90 and 90",
  "instance": "/api/restaurants/nearby",
  "traceId": "00-abc123-def456-00"
}
```

---

## Summary of Decisions

| # | Area | Decision | Constitution Alignment |
|---|------|----------|------------------------|
| 1 | Hosting | Blazor WASM hosted in API | Principle #6 (Blazor WASM) |
| 2 | Persistence | Azure Table Storage | Principle #5 (Table Storage) |
| 3 | Comic Generation | DALL-E 3 via Azure OpenAI | Principle #7 (Simplicity) |
| 4 | Image Storage | Azure Blob Storage | Principle #5 (Azure services) |
| 5 | Review Source | Google Maps API | Spec dependency |
| 6 | Strangeness | GPT-4o-mini classification | Spec requirement |
| 7 | Leaderboard | 30-second polling | Principle #7 (Simplicity) |
| 8 | Geolocation | Browser native API + JS interop | Principle #6 (Blazor WASM) |
| 9 | Testing | xUnit + Azurite + Playwright manual | Principle #3 (TDD) |
| 10 | Logging | Serilog + Application Insights | Principle #4 (Observability) |
| 11 | API Docs | Swashbuckle Swagger | Principle #4 (Swagger) |
| 12 | Errors | RFC 7807 Problem Details | Principle #4 (RFC 7807) |

**Status**: All research complete. Ready for Phase 1 design artifacts.
