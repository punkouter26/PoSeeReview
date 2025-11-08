# PoSeeReview

Restaurant review to comic strip generator using Azure OpenAI.

## Overview

PoSeeReview converts restaurant reviews into entertaining comic strips using AI. Search for restaurants, select reviews, and generate unique comics that capture the essence of dining experiences.

## Quick Start

See [specs/001-review-comic-app/quickstart.md](../specs/001-review-comic-app/quickstart.md) for detailed setup instructions.

## Architecture

- **Frontend**: Blazor WebAssembly
- **Backend**: ASP.NET Core 9.0 Web API
- **Storage**: Azure Table Storage + Blob Storage (Azurite locally)
- **AI**: Azure OpenAI (GPT-4o-mini, DALL-E 3)
- **Data**: Google Maps Places API (New)

## Project Structure

```
src/
  Po.SeeReview.Core/          # Domain entities and interfaces
  Po.SeeReview.Infrastructure/ # External service implementations
  Po.SeeReview.Api/            # ASP.NET Core Web API
  Po.SeeReview.Client/         # Blazor WebAssembly frontend
  Po.SeeReview.Shared/         # Shared DTOs
tests/
  Po.SeeReview.UnitTests/      # Unit tests
  Po.SeeReview.IntegrationTests/ # Integration tests
```

## Documentation

- [Product Requirements](PRD.MD)
- [Development Steps](STEPS.MD)
- [Feature Specification](../specs/001-review-comic-app/spec.md)
- [Implementation Plan](../specs/001-review-comic-app/plan.md)
- [Quick Start Guide](../specs/001-review-comic-app/quickstart.md)

## License

MIT
