# Data Model

**Feature**: Restaurant Review Comic Generator  
**Date**: 2025-01-22  
**Branch**: 001-review-comic-app

## Overview

This document defines the domain entities, Azure Table Storage schema, and data relationships for PoSeeReview. All entities are designed for Azure Table Storage with partition/row key strategies optimized for query patterns.

---

## Core Entities

### 1. Restaurant

**Purpose**: Represents a restaurant discovered via Google Maps API with cached metadata and reviews.

**Domain Model** (`Po.SeeReview.Core/Entities/Restaurant.cs`):
```csharp
public class Restaurant
{
    public string PlaceId { get; set; }                  // Google Maps place_id
    public string Name { get; set; }
    public string Address { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string Region { get; set; }                    // e.g., "US-CA-SF" for partitioning
    public double AverageRating { get; set; }             // 0-5 stars
    public int TotalReviews { get; set; }
    public List<Review> Reviews { get; set; }             // Top 10 reviews from Google
    public DateTimeOffset CachedAt { get; set; }          // For 24-hour cache expiration
}

public class Review
{
    public string AuthorName { get; set; }
    public string Text { get; set; }
    public int Rating { get; set; }                       // 1-5 stars
    public DateTimeOffset Time { get; set; }
    public double StrangenessScore { get; set; }          // 0-100 from Azure OpenAI
}
```

**Azure Table Storage Schema** (`Po.SeeReview.Infrastructure/Entities/RestaurantEntity.cs`):
```csharp
public class RestaurantEntity : ITableEntity
{
    // Azure Table required properties
    public string PartitionKey { get; set; }              // Format: "RESTAURANT_{Region}"
    public string RowKey { get; set; }                    // Format: "{PlaceId}"
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
    
    // Business properties
    public string PlaceId { get; set; }
    public string Name { get; set; }
    public string Address { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string Region { get; set; }
    public double AverageRating { get; set; }
    public int TotalReviews { get; set; }
    public string ReviewsJson { get; set; }               // Serialized List<Review> (max 32KB)
    public DateTimeOffset CachedAt { get; set; }
}
```

**Storage Details**:
- **Table Name**: `PoSeeReviewRestaurants`
- **Partition Key**: `RESTAURANT_{Region}` (e.g., `RESTAURANT_US-CA-SF`)
  - **Rationale**: Enables efficient queries for nearby restaurants in same region
- **Row Key**: `{PlaceId}` (unique Google identifier)
- **Cache Invalidation**: Delete entities where `Timestamp < Now - 24 hours`
- **Query Pattern**: Point read by PlaceId, or partition query by region + latitude/longitude filter

**Validation Rules**:
- `PlaceId`: Required, max 255 chars (Google Maps limit)
- `Name`: Required, max 255 chars
- `Latitude`: -90 to 90
- `Longitude`: -180 to 180
- `Region`: Required, format `{Country}-{State}-{City}` (e.g., `US-CA-SF`)
- `ReviewsJson`: Max 32KB (Azure Table property limit) - store top 10 reviews only

---

### 2. Comic

**Purpose**: Represents a generated four-panel comic strip with metadata and Azure Blob Storage reference.

**Domain Model** (`Po.SeeReview.Core/Entities/Comic.cs`):
```csharp
public class Comic
{
    public string ComicId { get; set; }                   // GUID
    public string PlaceId { get; set; }                   // Links to Restaurant
    public string RestaurantName { get; set; }
    public double StrangenessScore { get; set; }          // Max score from selected reviews
    public string BlobUrl { get; set; }                   // Azure Blob Storage HTTPS URL
    public DateTimeOffset GeneratedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }         // GeneratedAt + 24 hours
    public string PromptUsed { get; set; }                // DALL-E prompt for debugging
    public List<string> SourceReviews { get; set; }       // Top 5 reviews used for comic
}
```

**Azure Table Storage Schema** (`Po.SeeReview.Infrastructure/Entities/ComicEntity.cs`):
```csharp
public class ComicEntity : ITableEntity
{
    // Azure Table required properties
    public string PartitionKey { get; set; }              // Format: "COMIC_{PlaceId}"
    public string RowKey { get; set; }                    // Format: "{GeneratedAt:yyyyMMddHHmmss}"
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
    
    // Business properties
    public string ComicId { get; set; }
    public string PlaceId { get; set; }
    public string RestaurantName { get; set; }
    public double StrangenessScore { get; set; }
    public string BlobUrl { get; set; }
    public DateTimeOffset GeneratedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public string PromptUsed { get; set; }
    public string SourceReviewsJson { get; set; }         // Serialized List<string>
}
```

**Storage Details**:
- **Table Name**: `PoSeeReviewComics`
- **Partition Key**: `COMIC_{PlaceId}` (e.g., `COMIC_ChIJabcdef123`)
  - **Rationale**: Groups all comics for same restaurant, enables efficient cache lookup
- **Row Key**: `{GeneratedAt:yyyyMMddHHmmss}` (e.g., `20250122143000`)
  - **Rationale**: Natural ordering by generation time, prevents duplicates
- **Blob Storage Path**: `comics/{PlaceId}/{ComicId}.png`
- **Cache Strategy**: Return cached comic if `ExpiresAt > Now`, otherwise regenerate
- **Query Pattern**: 
  - Get latest comic: Query partition `COMIC_{PlaceId}`, order by RowKey descending, take 1
  - Cleanup expired: Scan all partitions, delete where `ExpiresAt < Now`

**Validation Rules**:
- `ComicId`: Required, GUID format
- `PlaceId`: Required, matches Restaurant.PlaceId
- `StrangenessScore`: 0-100
- `BlobUrl`: Required, valid HTTPS URL
- `ExpiresAt`: Must be `GeneratedAt + 24 hours`

---

### 3. LeaderboardEntry

**Purpose**: Aggregated view of restaurants ranked by strangeness score for real-time leaderboard.

**Domain Model** (`Po.SeeReview.Core/Entities/LeaderboardEntry.cs`):
```csharp
public class LeaderboardEntry
{
    public int Rank { get; set; }                         // 1-based ranking
    public string PlaceId { get; set; }
    public string RestaurantName { get; set; }
    public string Address { get; set; }
    public double StrangenessScore { get; set; }          // Highest score from comics
    public string ComicBlobUrl { get; set; }              // Thumbnail for leaderboard
    public DateTimeOffset LastUpdated { get; set; }
}
```

**Azure Table Storage Schema** (`Po.SeeReview.Infrastructure/Entities/LeaderboardEntity.cs`):
```csharp
public class LeaderboardEntity : ITableEntity
{
    // Azure Table required properties
    public string PartitionKey { get; set; }              // Format: "LEADERBOARD_{Region}"
    public string RowKey { get; set; }                    // Format: "{9999999999-StrangenessScore}_{PlaceId}"
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
    
    // Business properties
    public string PlaceId { get; set; }
    public string RestaurantName { get; set; }
    public string Address { get; set; }
    public double StrangenessScore { get; set; }
    public string Region { get; set; }
    public string ComicBlobUrl { get; set; }
    public DateTimeOffset LastUpdated { get; set; }
}
```

**Storage Details**:
- **Table Name**: `PoSeeReviewLeaderboard`
- **Partition Key**: `LEADERBOARD_{Region}` (e.g., `LEADERBOARD_US-CA-SF`)
  - **Rationale**: Regional leaderboards for better UX (show nearby restaurants)
- **Row Key**: `{9999999999-StrangenessScore}_{PlaceId}` (e.g., `9999999902_ChIJabc123`)
  - **Rationale**: Inverted score for descending sort (Azure Table sorts RowKey ascending)
  - Example: Score 98 → RowKey `9999999902_...` (sorts before score 50 → `9999999950_...`)
- **Update Trigger**: When new comic is generated, upsert leaderboard entry
- **Query Pattern**: Query partition `LEADERBOARD_{Region}`, take top 10 by RowKey (pre-sorted)

**Validation Rules**:
- `PlaceId`: Required, matches Restaurant.PlaceId
- `StrangenessScore`: 0-100
- `Region`: Required, matches Restaurant.Region
- `ComicBlobUrl`: Required, valid HTTPS URL
- **Uniqueness**: One entry per PlaceId per Region (upsert on comic generation)

---

## Entity Relationships

```
Restaurant (1) ──────< (N) Comic
    │                       │
    │                       │
    └───────────────────────┴──────> (1) LeaderboardEntry
                                      (aggregated max strangeness)
```

**Relationship Details**:
1. **Restaurant → Comic**: One-to-many
   - One restaurant can have multiple comics (over time, as cache expires)
   - Foreign key: `Comic.PlaceId` → `Restaurant.PlaceId`
   - Cascade delete: When restaurant cache expires, delete associated comics

2. **Comic → LeaderboardEntry**: Many-to-one (aggregated)
   - Multiple comics for same restaurant → single leaderboard entry with max score
   - Foreign key: `LeaderboardEntry.PlaceId` → `Restaurant.PlaceId`
   - Update strategy: On new comic, update leaderboard if score > current max

---

## State Transitions

### Restaurant Entity Lifecycle
```
[Not Cached] 
    ↓ (User searches nearby)
[Fetch from Google Maps API]
    ↓
[Store in Table Storage with CachedAt = Now]
    ↓
[Cached] (24 hours)
    ↓ (Timestamp > 24 hours)
[Delete entity]
    ↓
[Not Cached] (repeat cycle)
```

### Comic Entity Lifecycle
```
[Not Generated]
    ↓ (User clicks "Generate Comic")
[Check cache: Query COMIC_{PlaceId}, filter ExpiresAt > Now]
    ↓ (Cache hit)
[Return cached comic]

    ↓ (Cache miss)
[Analyze reviews → Generate DALL-E prompt → Call API]
    ↓
[Upload PNG to Blob Storage]
    ↓
[Store ComicEntity with ExpiresAt = Now + 24h]
    ↓
[Update LeaderboardEntity if score > current]
    ↓
[Generated & Cached] (24 hours)
    ↓ (ExpiresAt < Now)
[Delete entity & blob]
    ↓
[Not Generated] (repeat cycle)
```

### LeaderboardEntry Lifecycle
```
[Not Ranked]
    ↓ (First comic generated for restaurant)
[Insert LeaderboardEntity with StrangenessScore]
    ↓
[Ranked] (persists indefinitely)
    ↓ (New comic generated with higher score)
[Update LeaderboardEntity.StrangenessScore]
    ↓
[Re-ranked] (RowKey changed, re-sorted)
```

---

## Indexes & Query Optimization

| Query | Index Type | Partition Key | Row Key Filter | Estimated Latency |
|-------|-----------|---------------|----------------|------------------|
| Get restaurant by PlaceId | Point read | `RESTAURANT_{Region}` | `{PlaceId}` | <50ms |
| Get restaurants in region | Partition query | `RESTAURANT_{Region}` | None | <200ms (100 entities) |
| Get latest comic for restaurant | Partition query | `COMIC_{PlaceId}` | Order by RowKey desc | <100ms |
| Get top 10 leaderboard | Partition query | `LEADERBOARD_{Region}` | Take(10) | <100ms |
| Cleanup expired comics | Full table scan | All | Filter `ExpiresAt < Now` | <5s (background job) |

**Optimization Notes**:
- **No secondary indexes needed**: Partition key design covers all query patterns
- **No full table scans in user flows**: Only background cleanup job scans
- **Pre-sorted leaderboard**: Inverted RowKey eliminates need for in-memory sorting

---

## Data Volumes & Sizing

**Assumptions** (per spec success criteria):
- **Restaurants**: 1,000 active cached (10 per user × 100 users)
- **Comics**: 500 cached (50% of restaurants have comics)
- **Leaderboard**: 1,000 entries (one per region-restaurant pair)

**Storage Calculations**:
| Entity | Avg Size | Count | Total |
|--------|---------|-------|-------|
| RestaurantEntity | 5 KB (incl. ReviewsJson) | 1,000 | 5 MB |
| ComicEntity | 1 KB (blob URL only) | 500 | 0.5 MB |
| LeaderboardEntity | 0.5 KB | 1,000 | 0.5 MB |
| Comic PNG blobs | 500 KB (DALL-E) | 500 | 250 MB |
| **TOTAL** | | | **256 MB** |

**Cost Estimate** (Azure pricing):
- Table Storage: 256 MB × $0.045/GB = $0.01/month (negligible)
- Blob Storage: 250 MB × $0.018/GB = $0.004/month (negligible)
- **Total storage cost**: <$0.02/month

**Scalability Limits**:
- Azure Table Storage: 20,000 entities/second per partition (far exceeds expected load)
- Blob Storage: 500 requests/second per blob (sufficient for concurrent comic views)

---

## Data Migration & Seeding

**Development Seed Data** (for testing):
```json
// seed-data.json (load via integration tests)
{
  "restaurants": [
    {
      "placeId": "ChIJtest123",
      "name": "The Flying Saucer Café",
      "address": "123 Space Needle Way, Seattle, WA",
      "latitude": 47.6205,
      "longitude": -122.3493,
      "region": "US-WA-SEA",
      "reviews": [
        { "text": "The pancakes tasted like the moon.", "rating": 5, "strangenessScore": 87 }
      ]
    }
  ]
}
```

**Production Data**:
- No migration needed (new feature, no legacy data)
- Azurite local emulator for development
- Azure Table Storage connection string swap for production

---

## Summary

| Entity | Primary Use Case | Table Name | Partition Strategy | TTL |
|--------|------------------|------------|-------------------|-----|
| Restaurant | Cache Google Maps data | `PoSeeReviewRestaurants` | `RESTAURANT_{Region}` | 24h |
| Comic | Store generated comics | `PoSeeReviewComics` | `COMIC_{PlaceId}` | 24h |
| LeaderboardEntry | Display top strangest | `PoSeeReviewLeaderboard` | `LEADERBOARD_{Region}` | ∞ |

**Schema Compliance**:
- ✅ All entities use Azure Table Storage (Constitution Principle #5)
- ✅ Partition keys optimize query patterns (no full table scans)
- ✅ 24-hour cache TTL implemented (Spec FR-004, FR-005)
- ✅ Binary blobs in Blob Storage (not embedded in tables)
