# Research: Constitution v2.0.0 Compliance

**Feature**: 002-constitution-compliance  
**Phase**: 0 (Research & Best Practices)  
**Date**: 2025-11-12

## Overview

This research phase resolves technical unknowns and identifies best practices for implementing constitution compliance requirements. All decisions focus on retrofitting existing PoSeeReview codebase while minimizing disruption.

---

## R1: Directory.Packages.props Migration Strategy

**Decision**: Use MSBuild Central Package Management (CPM) with `<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>` in Directory.Packages.props

**Rationale**:
- Native MSBuild feature introduced in .NET SDK 6.0+, mature in .NET 9.0
- No additional tooling required (no need for Paket or custom scripts)
- Automatically prevents version attributes in PackageReference elements when enabled
- IDE support in Visual Studio and VS Code with C# extension
- Enables consistent transitive dependency resolution across all projects

**Alternatives Considered**:
1. **Paket**: More powerful dependency management but adds complexity, requires separate learning curve, overkill for this project size
2. **Custom MSBuild targets**: Would require custom maintenance, error-prone, unnecessary when native CPM exists
3. **Git pre-commit hooks**: Reactive rather than preventative, doesn't stop IDE from suggesting versions

**Implementation Approach**:
1. Create Directory.Packages.props at repository root
2. Add `<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>` to enable CPM
3. Extract all unique PackageReference versions from .csproj files via `dotnet list package`
4. Add each package with version to Directory.Packages.props as `<PackageVersion Include="..." Version="..." />`
5. Remove Version attributes from all .csproj PackageReference elements
6. Test with `dotnet restore` and `dotnet build` at solution level
7. Verify no duplicate/conflicting versions remain

**References**:
- [Microsoft Docs: Central Package Management](https://learn.microsoft.com/nuget/consume-packages/central-package-management)
- [.NET 7+ CPM Features](https://devblogs.microsoft.com/nuget/introducing-central-package-management/)

---

## R2: Nullable Reference Types Enablement Strategy

**Decision**: Enable nullable warnings globally, document all warnings in inventory, resolve incrementally without blocking builds

**Rationale**:
- Clarification confirmed warnings should not fail builds (informational mode)
- Large existing codebase likely has hundreds of warnings - fixing all upfront is impractical
- Incremental resolution allows feature development to continue while improving quality
- Warnings inventory provides visibility and tracks progress

**Alternatives Considered**:
1. **#nullable disable on all existing files**: Hides problems, prevents future improvement, defeats purpose
2. **Fail builds on warnings**: Too aggressive for retrofitting, would block all development
3. **Per-file enablement**: Creates inconsistency, hard to track what's enabled where

**Implementation Approach**:
1. Add `<Nullable>enable</Nullable>` to all .csproj files
2. Run `dotnet build` and capture all nullable warnings to text file
3. Parse warnings into structured inventory (docs/nullable-warnings.md) with: file path, line number, warning code, description
4. Categorize warnings by severity/area (e.g., controller parameters, service methods, entity properties)
5. Create backlog items for incremental resolution in future sprints
6. Configure CI to track warning count trend without failing builds

**Warning Categories to Expect**:
- CS8600: Converting null literal or possible null value to non-nullable type
- CS8601: Possible null reference assignment
- CS8602: Dereference of a possibly null reference
- CS8603: Possible null reference return
- CS8604: Possible null reference argument
- CS8618: Non-nullable field must contain a non-null value when exiting constructor

**References**:
- [Microsoft Docs: Nullable Reference Types](https://learn.microsoft.com/dotnet/csharp/nullable-references)
- [Strategies for Migrating to Nullable](https://devblogs.microsoft.com/dotnet/embracing-nullable-reference-types/)

---

## R3: Code Coverage Tool Configuration (dotnet-coverage)

**Decision**: Use built-in `dotnet-coverage` tool with HTML and Cobertura XML report generation

**Rationale**:
- Clarification confirmed dotnet-coverage as the chosen tool
- Built into .NET SDK, no additional installation required
- Supports multiple output formats (coverage, xml, cobertura, html)
- Integrates with Visual Studio Code coverage visualization
- Lower overhead than coverlet for simple scenarios

**Alternatives Considered**:
1. **coverlet.collector**: More features (branch coverage, multiple formats) but adds NuGet dependency
2. **Fine Code Coverage extension**: VS Code only, doesn't work in CI/CD
3. **ReportGenerator**: Complementary tool for better HTML reports, could be added later

**Implementation Approach**:
1. Run tests with coverage: `dotnet-coverage collect "dotnet test" --output docs/coverage/coverage.coverage`
2. Convert to XML: `dotnet-coverage merge -o docs/coverage/coverage.xml -f xml docs/coverage/coverage.coverage`
3. Convert to HTML: `dotnet-coverage merge -o docs/coverage/ -f html docs/coverage/coverage.coverage`
4. Configure runsettings file for coverage collection parameters:
   ```xml
   <RunSettings>
     <DataCollectionRunSettings>
       <DataCollectors>
         <DataCollector friendlyName="Code Coverage" enabled="True" />
       </DataCollectors>
     </DataCollectionRunSettings>
   </RunSettings>
   ```
5. Add coverage commands to README.md quickstart
6. Configure CI to publish coverage artifacts

**Coverage Thresholds**:
- Target: 80% line coverage for business logic assemblies
- Enforcement: Informational only (per clarification), tracked via reports
- Exclusions: Program.cs, Startup files, auto-generated code, migration files

**References**:
- [Microsoft Docs: dotnet-coverage](https://learn.microsoft.com/dotnet/core/additional-tools/dotnet-coverage)
- [Code Coverage Best Practices](https://learn.microsoft.com/dotnet/core/testing/unit-testing-code-coverage)

---

## R4: bUnit Testing Patterns for Blazor Components

**Decision**: Install bUnit NuGet package in Po.SeeReview.UnitTests, create ComponentTests/ folder with sample tests demonstrating key patterns

**Rationale**:
- bUnit is the de facto standard for Blazor component testing
- Integrates with xUnit (already in use for unit tests)
- Supports mocking dependencies (IHttpClientFactory, JS interop, navigation)
- Enables testing component rendering, parameter binding, event callbacks, lifecycle hooks

**Alternatives Considered**:
1. **Playwright for component testing**: Overkill, slower, requires full app running
2. **Manual DOM inspection**: No framework support, brittle, hard to maintain
3. **Separate test project for components**: Unnecessary separation, increases solution complexity

**Implementation Approach**:
1. Add bUnit NuGet package to Po.SeeReview.UnitTests.csproj
2. Create tests/Po.SeeReview.UnitTests/ComponentTests/ directory
3. Create sample test demonstrating patterns:
   - TestContext setup
   - Component rendering
   - Parameter passing
   - Event callback verification
   - Mocking IHttpClientFactory
   - Asserting rendered markup
4. Document patterns in quickstart.md

**Essential bUnit Patterns**:
```csharp
// Pattern 1: Basic rendering test
[Fact]
public void RestaurantCard_RendersCorrectly()
{
    using var ctx = new TestContext();
    var component = ctx.RenderComponent<RestaurantCard>(parameters => parameters
        .Add(p => p.Restaurant, new Restaurant { Name = "Test" }));
    
    component.MarkupMatches("<div>Test</div>");
}

// Pattern 2: Event handling test
[Fact]
public void RestaurantCard_OnClick_InvokesCallback()
{
    using var ctx = new TestContext();
    var clicked = false;
    var component = ctx.RenderComponent<RestaurantCard>(parameters => parameters
        .Add(p => p.OnClick, () => clicked = true));
    
    component.Find("button").Click();
    Assert.True(clicked);
}

// Pattern 3: Mocking HTTP
[Fact]
public void RestaurantList_LoadsData()
{
    using var ctx = new TestContext();
    ctx.Services.AddSingleton<IHttpClientFactory>(mockFactory);
    var component = ctx.RenderComponent<RestaurantList>();
    
    component.WaitForState(() => component.FindAll("li").Count > 0);
}
```

**References**:
- [bUnit Documentation](https://bunit.dev/)
- [bUnit Best Practices](https://bunit.dev/docs/test-doubles/index.html)

---

## R5: Bicep Infrastructure as Code Best Practices

**Decision**: Organize Bicep code into modular templates in /infra/modules/, orchestrated by main.bicep, deployed via Azure Developer CLI (azd)

**Rationale**:
- Modular structure promotes reusability and testability
- azd provides streamlined developer workflow (azd up = provision + deploy)
- Bicep is Azure-native IaC with type safety and IntelliSense
- Module pattern aligns with separation of concerns

**Alternatives Considered**:
1. **Terraform**: More verbose, requires separate state management, unnecessary complexity for Azure-only
2. **ARM templates**: JSON-based, less readable than Bicep, deprecated in favor of Bicep
3. **Pulumi**: Adds dependency on TypeScript/Python, overkill for this project size
4. **Azure CLI scripts**: Imperative, not idempotent, hard to version control state

**Implementation Approach**:
1. Create /infra/modules/ for reusable Bicep modules:
   - appservice.bicep: App Service plan + web app configuration
   - storage.bicep: Storage account with Table and Blob containers
   - monitoring.bicep: Application Insights + Log Analytics workspace
   - budget.bicep: Budget alert configuration
2. Create /infra/main.bicep to orchestrate modules with parameters
3. Create azure.yaml for azd configuration
4. Add parameters file for environment-specific values
5. Test deployment with `azd provision` to dev environment

**Bicep Module Structure**:
```bicep
// monitoring.bicep
param location string
param appName string

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: '${appName}-logs'
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: '${appName}-ai'
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
  }
}

output appInsightsConnectionString string = appInsights.properties.ConnectionString
```

**Budget Configuration Strategy**:
- Use Azure Consumption Budget API
- Set $5 monthly limit with 80%, 100%, 110% thresholds
- Configure email alerts to project owner
- Document manual scale-down procedure (azd does not support auto-shutdown)

**References**:
- [Microsoft Docs: Bicep](https://learn.microsoft.com/azure/azure-resource-manager/bicep/)
- [Azure Developer CLI](https://learn.microsoft.com/azure/developer/azure-developer-cli/)
- [Bicep Best Practices](https://learn.microsoft.com/azure/azure-resource-manager/bicep/best-practices)

---

## R6: OpenTelemetry Integration for Custom Telemetry

**Decision**: Use OpenTelemetry .NET SDK with ActivitySource for distributed tracing and Meter for custom metrics, export to Application Insights via Azure Monitor exporter

**Rationale**:
- OpenTelemetry is vendor-neutral, future-proof standard
- Azure Monitor exporter provides seamless Application Insights integration
- ActivitySource enables distributed tracing with parent-child span relationships
- Meter supports dimensional metrics (tags/labels for filtering)

**Alternatives Considered**:
1. **Application Insights SDK directly**: Vendor lock-in, harder to migrate if needed
2. **Serilog enrichers**: Limited to logging, doesn't support distributed traces or metrics
3. **Custom telemetry**: Reinventing the wheel, no standardization

**Implementation Approach**:
1. Install NuGet packages:
   - OpenTelemetry.Api
   - OpenTelemetry.Extensions.Hosting
   - OpenTelemetry.Instrumentation.AspNetCore
   - Azure.Monitor.OpenTelemetry.Exporter
2. Configure in Program.cs:
   ```csharp
   builder.Services.AddOpenTelemetry()
       .WithTracing(tracing => tracing
           .AddAspNetCoreInstrumentation()
           .AddSource("PoSeeReview.Api")
           .AddAzureMonitorTraceExporter())
       .WithMetrics(metrics => metrics
           .AddAspNetCoreInstrumentation()
           .AddMeter("PoSeeReview.Api")
           .AddAzureMonitorMetricExporter());
   ```
3. Create ActivitySource for custom spans:
   ```csharp
   private static readonly ActivitySource ActivitySource = new("PoSeeReview.Api");
   
   using var activity = ActivitySource.StartActivity("GenerateComic");
   activity?.SetTag("restaurant.id", restaurantId);
   activity?.SetTag("strangeness.score", strangenessScore);
   ```
4. Create Meter for custom metrics:
   ```csharp
   private static readonly Meter Meter = new("PoSeeReview.Api");
   private static readonly Counter<long> ComicCounter = Meter.CreateCounter<long>("comics.generated");
   private static readonly Histogram<double> StrangenessHistogram = Meter.CreateHistogram<double>("strangeness.score");
   
   ComicCounter.Add(1, new KeyValuePair<string, object?>("region", region));
   StrangenessHistogram.Record(strangenessScore);
   ```

**Custom Telemetry to Implement**:
- Traces: Review analysis duration, narrative generation, DALL-E API calls, blob storage operations
- Metrics: Comics generated (counter), strangeness score distribution (histogram), API costs (counter), leaderboard queries (counter)
- Tags: Region, restaurant category, user flow (browse/generate/leaderboard)

**References**:
- [OpenTelemetry .NET](https://opentelemetry.io/docs/instrumentation/net/)
- [Azure Monitor OpenTelemetry](https://learn.microsoft.com/azure/azure-monitor/app/opentelemetry-enable)

---

## R7: KQL Query Library Structure

**Decision**: Create docs/kql/ directory with categorized .kql files for common monitoring scenarios

**Rationale**:
- KQL is the native query language for Application Insights
- Predefined queries accelerate incident response and reduce learning curve
- Version-controlled queries ensure team consistency
- .kql files can be executed directly in Azure Portal or via CLI

**Implementation Approach**:
1. Create docs/kql/ directory
2. Create essential query files:
   - errors.kql: Exception tracking, error rates, failure analysis
   - performance.kql: Response times, dependency durations, slow queries
   - dependencies.kql: External service health, API call success rates
   - custom-metrics.kql: Business metrics (comic generation, strangeness scores)
3. Include query descriptions and parameters in comments
4. Document query usage in deployment.md

**Essential KQL Queries**:

**errors.kql**:
```kql
// Top 10 exceptions by count (last 24h)
exceptions
| where timestamp > ago(24h)
| summarize count() by type, outerMessage
| top 10 by count_
| order by count_ desc

// Error rate trend (hourly)
requests
| where timestamp > ago(7d)
| summarize total = count(), failures = countif(success == false) by bin(timestamp, 1h)
| extend errorRate = (failures * 100.0) / total
| render timechart
```

**performance.kql**:
```kql
// API response time percentiles
requests
| where timestamp > ago(24h)
| summarize p50 = percentile(duration, 50), p95 = percentile(duration, 95), p99 = percentile(duration, 99) by operation_Name
| order by p95 desc

// Slowest dependencies
dependencies
| where timestamp > ago(24h)
| summarize avg(duration), p95 = percentile(duration, 95) by name, type
| order by p95 desc
```

**custom-metrics.kql**:
```kql
// Comic generation volume by region
customMetrics
| where name == "comics.generated"
| extend region = tostring(customDimensions.region)
| summarize count() by region, bin(timestamp, 1h)
| render timechart

// Strangeness score distribution
customMetrics
| where name == "strangeness.score"
| summarize avg(value), p50 = percentile(value, 50), p95 = percentile(value, 95)
| project avg_strangeness, p50, p95
```

**References**:
- [Kusto Query Language](https://learn.microsoft.com/azure/data-explorer/kusto/query/)
- [Application Insights Query Examples](https://learn.microsoft.com/azure/azure-monitor/logs/query-tutorial)

---

## R8: Production Diagnostics Configuration

**Decision**: Enable Snapshot Debugger and Profiler via Azure Portal App Service configuration, document in deployment.md

**Rationale**:
- Snapshot Debugger captures application state during exceptions (variables, call stack)
- Profiler identifies performance bottlenecks with method-level execution times
- Both are Azure-native tools requiring no code changes
- Configuration via Portal is simpler than Bicep for diagnostic features

**Alternatives Considered**:
1. **Bicep automation**: Possible via appservice site extensions, but more complex and less maintainable
2. **Application Insights SDK agents**: Requires code changes, more intrusive
3. **Manual investigation**: Time-consuming, requires reproducing issues locally

**Implementation Approach**:
1. Document enablement steps in docs/deployment.md:
   - Navigate to App Service in Azure Portal
   - Open "Application Insights" blade
   - Enable Snapshot Debugger with default settings
   - Enable Profiler with default settings
2. Configure snapshots:
   - Retention: 14 days
   - Collection limit: 5 snapshots per hour
   - Privacy: Ensure no PII in snapshots (configure data masking if needed)
3. Configure Profiler:
   - Duration: 2 minutes per trigger
   - Frequency: On-demand or scheduled
   - CPU threshold: 80%
4. Test by triggering an exception and verifying snapshot appears in Application Insights

**Security Considerations**:
- Snapshots may contain sensitive data (user IDs, connection strings)
- Configure Application Insights data sampling to exclude sensitive telemetry
- Review snapshot retention policy to comply with data protection requirements
- Document in deployment.md: "Verify snapshots do not contain PII before sharing with team"

**References**:
- [Snapshot Debugger](https://learn.microsoft.com/azure/azure-monitor/snapshot-debugger/snapshot-debugger)
- [Application Insights Profiler](https://learn.microsoft.com/azure/azure-monitor/profiler/profiler)

---

## Summary

All technical unknowns have been resolved with concrete decisions:

1. ✅ **R1**: Directory.Packages.props using MSBuild CPM
2. ✅ **R2**: Nullable warnings inventory with incremental resolution
3. ✅ **R3**: dotnet-coverage with HTML/XML reports
4. ✅ **R4**: bUnit patterns in ComponentTests/ folder
5. ✅ **R5**: Modular Bicep with azd deployment
6. ✅ **R6**: OpenTelemetry with ActivitySource and Meter
7. ✅ **R7**: KQL library in docs/kql/
8. ✅ **R8**: Portal-based diagnostics configuration

**Next Phase**: Phase 1 (Design & Contracts) - Generate data-model.md, contracts/, quickstart.md
