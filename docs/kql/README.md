# KQL Monitoring Library

This directory contains curated KQL (Kusto Query Language) queries for monitoring PoSeeReview application health, performance, and business metrics in Azure Application Insights.

## Query Files

| File | Purpose | Key Metrics |
|------|---------|-------------|
| `errors.kql` | Exception monitoring and error analysis | Top exceptions by type, failure trends, affected operations |
| `performance.kql` | API response time analysis | P50/P95/P99 percentiles by operation, slow requests |
| `dependencies.kql` | External service health monitoring | Success rate, response time, failures for Azure OpenAI, Google Maps, Storage |
| `custom-metrics.kql` | Business KPIs and custom telemetry | Comic generation rate, cache performance, strangeness scores |

## How to Use

### Running Queries in Azure Portal

1. Navigate to your Application Insights resource in Azure Portal
2. Select **Logs** from the left navigation menu
3. Copy and paste the desired query from the `.kql` files
4. Click **Run** to execute the query
5. Use the time range selector in the toolbar to adjust the query window (default: last 24 hours)

### Common Parameters

All queries accept standard KQL time range parameters:

```kql
// Last 24 hours (default in all queries)
| where timestamp > ago(24h)

// Last 7 days
| where timestamp > ago(7d)

// Last hour
| where timestamp > ago(1h)

// Custom time range
| where timestamp between(datetime(2025-11-01) .. datetime(2025-11-12))
```

### Modifying Queries

Each `.kql` file includes:
- **Primary query**: Ready-to-run query at the top
- **Alternative queries**: Commented variations for different analysis scenarios

To use an alternative query, uncomment it (remove `//` prefix) and comment out the primary query.

## Query Details

### errors.kql - Exception Monitoring

**Primary Query**: Groups exceptions by type with counts, sample messages, and time ranges.

**Key Columns**:
- `ExceptionType`: Type of exception (e.g., `System.InvalidOperationException`)
- `Count`: Number of occurrences in the time window
- `FirstSeen` / `LastSeen`: Time range of exception occurrences
- `AffectedOperations`: Number of distinct API operations that threw this exception
- `SampleMessage`: Truncated exception message (first 100 chars)

**Alternative Queries**:
1. Exception trends over time (hourly buckets) - visualize as timechart
2. Exceptions by operation - identify which endpoints fail most
3. Detailed exception analysis with full stack traces

**Use Cases**:
- Daily error review during standup
- Incident investigation when alerts fire
- Identifying new exception types after deployment

### performance.kql - Response Time Analysis

**Primary Query**: Calculates response time percentiles (p50, p95, p99) for each API operation.

**Key Columns**:
- `Operation`: API endpoint name (e.g., `POST /api/comics/{placeId}`)
- `Requests`: Total request count
- `P50 (ms)`: Median response time
- `P95 (ms)`: 95th percentile response time (SLA target)
- `P99 (ms)`: 99th percentile response time
- `Avg (ms)` / `Max (ms)`: Average and maximum response times

**Alternative Queries**:
1. Response time trends over time - track performance degradation
2. Slow requests (>2 seconds) - identify problematic requests
3. Performance by result code - correlate status codes with response times

**Use Cases**:
- SLA compliance monitoring (<200ms p95 target)
- Performance regression detection after deployments
- Capacity planning based on request volume and latency

### dependencies.kql - External Service Health

**Primary Query**: Monitors external dependencies with success rates and response times.

**Key Dependencies Tracked**:
- **Azure OpenAI**: DALL-E 3 image generation, GPT-4o-mini narrative generation
- **Google Maps API**: Restaurant lookups and review retrieval
- **Azure Storage**: Table storage (comics, leaderboard) and blob storage (images)

**Key Columns**:
- `DependencyType`: Type of dependency (HTTP, Azure blob, Azure table)
- `DependencyName`: Friendly name of the dependency
- `Target`: Target endpoint or service name
- `TotalCalls`: Number of dependency calls
- `SuccessRate`: Percentage of successful calls
- `Failed`: Count of failed calls
- `Avg/P95/P99 (ms)`: Response time percentiles

**Alternative Queries**:
1. Dependency failures with error codes - diagnose specific failures
2. Dependency trends over time - visualize availability
3. Slowest dependencies - identify performance bottlenecks

**Use Cases**:
- External API health monitoring
- SLA tracking for third-party services
- Identifying dependency failures causing cascading errors

### custom-metrics.kql - Business Metrics

**Primary Query**: Tracks PoSeeReview-specific business KPIs using OpenTelemetry custom metrics.

**Metrics Tracked**:

1. **Comic Generation Metrics** (`po.seereview.comics.generated`):
   - Total comics generated per hour
   - Cache hit rate (percentage of requests served from cache)
   - Forced regeneration count

2. **Comic Generation Errors** (`po.seereview.comics.errors`):
   - Errors by type (restaurant_not_found, insufficient_reviews, unknown)
   - Error trends over time

3. **Comic Generation Duration** (`po.seereview.comics.generation_duration`):
   - Average, P50, P95, P99 duration for fresh generation vs cache hits
   - Performance comparison between cache and fresh generation

4. **Restaurant Lookups** (`po.seereview.restaurants.lookups`):
   - Lookups by source (Google Maps, cache)
   - Success rate by source

**Alternative Queries**:
1. Comic generation rate trend - timechart of hourly generation volume
2. Error analysis by type - breakdown of failure reasons
3. Duration analysis by cache status - performance impact of caching
4. Strangeness score distribution - analyze review sentiment extremes

**Use Cases**:
- Business KPI dashboards and reports
- Cache effectiveness monitoring (target: >70% hit rate)
- Understanding generation patterns (peak times, failure modes)
- Strangeness score analysis for content quality

## Best Practices

### Query Performance

1. **Limit time ranges**: Avoid queries over `>30d` unless necessary
2. **Use summarization**: Aggregate data with `summarize` instead of returning raw rows
3. **Filter early**: Apply `where` clauses before `summarize` operations
4. **Limit results**: Use `take` or `top` to limit output for large result sets

### Creating Alerts

Convert queries to alerts for proactive monitoring:

1. Run the query in Application Insights Logs
2. Click **New alert rule** in the toolbar
3. Configure alert logic (e.g., `Count > 10` for exceptions)
4. Set action groups for email/SMS/webhook notifications

**Recommended Alerts**:
- Exception rate > 10 per hour
- P95 response time > 200ms
- Dependency success rate < 95%
- Comic generation failure rate > 10%

### Creating Dashboards

Pin queries to Azure Dashboards for at-a-glance monitoring:

1. Run the query and click **Pin to dashboard**
2. Select an existing dashboard or create new
3. Configure refresh interval (e.g., 5 minutes)

**Recommended Dashboard Layout**:
- Top left: Exception count (last 24h)
- Top right: P95 response time trend
- Bottom left: Dependency health matrix
- Bottom right: Comic generation rate and cache hit %

## Query Syntax Reference

### Common KQL Operations

```kql
// Filtering
| where timestamp > ago(24h)
| where success == true
| where duration > 1000

// Aggregation
| summarize count() by operation_Name
| summarize avg(duration), max(duration) by bin(timestamp, 1h)

// Percentiles
| summarize P95 = percentile(duration, 95)

// Ordering and limiting
| order by count_ desc
| take 20

// Projecting columns
| project timestamp, operation_Name, duration

// Rendering charts
| render timechart
| render piechart
| render barchart
```

### Application Insights Tables

| Table | Purpose |
|-------|---------|
| `requests` | HTTP requests to the application |
| `dependencies` | External service calls (HTTP, SQL, Azure services) |
| `exceptions` | Unhandled exceptions and errors |
| `traces` | Custom logging and diagnostic traces |
| `customMetrics` | Custom metrics from OpenTelemetry or Application Insights SDK |
| `customEvents` | Custom business events |
| `pageViews` | Client-side page views (for Blazor WASM) |
| `performanceCounters` | System performance metrics (CPU, memory) |

## Troubleshooting

### Query Returns No Results

- **Check time range**: Ensure the `ago()` parameter covers when data was ingested
- **Verify data ingestion**: Check Live Metrics to confirm telemetry is flowing
- **Case sensitivity**: KQL is case-sensitive for column names and values
- **Custom metrics**: Verify OpenTelemetry is configured and metrics are being exported

### Slow Query Performance

- Reduce time range (e.g., `ago(24h)` instead of `ago(30d)`)
- Add filters earlier in the query pipeline
- Use `summarize` to aggregate before complex operations
- Avoid `join` operations on large datasets

### Metric Names Not Found

Custom metrics require OpenTelemetry configuration in `Program.cs`:
- Verify `AddOpenTelemetry().WithMetrics()` is configured
- Check meter name pattern matches: `"Po.SeeReview.*"`
- Confirm Azure Monitor exporter is configured with valid connection string
- Allow 2-5 minutes for metrics to appear after first emission

## Additional Resources

- [KQL Quick Reference](https://learn.microsoft.com/en-us/azure/data-explorer/kql-quick-reference)
- [Application Insights Schema](https://learn.microsoft.com/en-us/azure/azure-monitor/app/data-model)
- [KQL Tutorial](https://learn.microsoft.com/en-us/azure/data-explorer/kusto/query/tutorial)
- [Query Best Practices](https://learn.microsoft.com/en-us/azure/data-explorer/kusto/query/best-practices)

---

**Maintenance**: Update queries when new custom metrics are added or application telemetry schema changes. Test queries after Application Insights schema updates.
