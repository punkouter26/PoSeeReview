# Quickstart: Constitution v2.0.0 Compliance

**Feature**: 002-constitution-compliance  
**Last Updated**: 2025-11-12

This guide provides step-by-step instructions for implementing and verifying constitution compliance features.

---

## Prerequisites

- .NET 9.0 SDK installed
- Azure CLI installed
- Azure Developer CLI (azd) installed
- Azure subscription with Resource Manager permissions
- Visual Studio Code with C# extension

---

## Phase 1: Centralized Package Management

### Create Directory.Packages.props

```powershell
# Navigate to repository root
cd C:\Users\punko\Downloads\PoSeeReview

# Extract current package versions
dotnet list package --include-transitive > package-inventory.txt

# Create Directory.Packages.props
@"
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  
  <ItemGroup>
    <!-- Add package versions here -->
  </ItemGroup>
</Project>
"@ | Out-File -FilePath "Directory.Packages.props" -Encoding UTF8
```

### Migrate Package Versions

1. Open each .csproj file
2. Copy `<PackageReference Include="..." Version="..." />` entries
3. Add to Directory.Packages.props as `<PackageVersion Include="..." Version="..." />`
4. Remove `Version` attributes from .csproj files
5. Keep only `<PackageReference Include="..." />` (no version)

### Verify

```powershell
dotnet restore
dotnet build
```

Expected: Build succeeds with all packages resolved from Directory.Packages.props

---

## Phase 2: Enable Nullable Reference Types

### Update All .csproj Files

Add to each project's `<PropertyGroup>`:

```xml
<Nullable>enable</Nullable>
```

Projects to update:
- src/Po.SeeReview.Api/Po.SeeReview.Api.csproj
- src/Po.SeeReview.Client/Po.SeeReview.Client.csproj
- src/Po.SeeReview.Core/Po.SeeReview.Core.csproj
- src/Po.SeeReview.Infrastructure/Po.SeeReview.Infrastructure.csproj
- src/Po.SeeReview.Shared/Po.SeeReview.Shared.csproj

### Generate Warnings Inventory

```powershell
# Build and capture warnings
dotnet build > build-output.txt 2>&1

# Parse warnings (manual or script-based)
# Filter for CS86xx warnings
Select-String -Path build-output.txt -Pattern "CS86\d{2}" | Out-File nullable-warnings-raw.txt
```

### Create docs/nullable-warnings.md

```markdown
# Nullable Reference Type Warnings Inventory

**Generated**: 2025-11-12  
**Total Warnings**: [COUNT]

| File | Line | Code | Message | Severity | Status |
|------|------|------|---------|----------|--------|
| src/Po.SeeReview.Api/Features/Restaurants/GetRestaurants.cs | 42 | CS8600 | Converting null literal to non-nullable | High | Open |
| ... | ... | ... | ... | ... | ... |
```

### Verify

```powershell
dotnet build
```

Expected: Build succeeds with warnings logged (not failing)

---

## Phase 3: Configure Code Coverage

### Install dotnet-coverage Tool

```powershell
# Comes with .NET SDK, verify availability
dotnet-coverage --version
```

### Collect Coverage

```powershell
# Run tests with coverage
dotnet-coverage collect "dotnet test" --output docs/coverage/coverage.coverage

# Convert to XML
dotnet-coverage merge -o docs/coverage/coverage.xml -f xml docs/coverage/coverage.coverage

# Convert to HTML
dotnet-coverage merge -o docs/coverage/ -f html docs/coverage/coverage.coverage
```

### View Reports

```powershell
# Open HTML report in browser
Start-Process docs/coverage/index.html
```

### Create Coverage Script

Create `scripts/collect-coverage.ps1`:

```powershell
#!/usr/bin/env pwsh
param(
    [string]$OutputDir = "docs/coverage"
)

Write-Host "Collecting code coverage..." -ForegroundColor Cyan

# Ensure output directory exists
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

# Collect coverage
dotnet-coverage collect "dotnet test" --output "$OutputDir/coverage.coverage"

# Convert to formats
dotnet-coverage merge -o "$OutputDir/coverage.xml" -f xml "$OutputDir/coverage.coverage"
dotnet-coverage merge -o "$OutputDir/" -f html "$OutputDir/coverage.coverage"

Write-Host "Coverage reports generated in $OutputDir" -ForegroundColor Green
Write-Host "Open $OutputDir/index.html to view results" -ForegroundColor Yellow
```

Make executable:
```powershell
chmod +x scripts/collect-coverage.ps1
```

### Verify

```powershell
.\scripts\collect-coverage.ps1
```

Expected: Coverage reports generated in docs/coverage/

---

## Phase 4: bUnit Component Testing

### Add bUnit Package

Manually add to Directory.Packages.props:

```xml
<PackageVersion Include="bUnit" Version="1.28.9" />
<PackageVersion Include="bUnit.web" Version="1.28.9" />
```

Add to tests/Po.SeeReview.UnitTests/Po.SeeReview.UnitTests.csproj:

```xml
<PackageReference Include="bUnit" />
<PackageReference Include="bUnit.web" />
```

### Create Sample Component Test

Create `tests/Po.SeeReview.UnitTests/ComponentTests/RestaurantCardTests.cs`:

```csharp
using Bunit;
using Po.SeeReview.Client.Components;
using Xunit;

namespace Po.SeeReview.UnitTests.ComponentTests;

public class RestaurantCardTests
{
    [Fact]
    public void RestaurantCard_RendersName_WhenProvided()
    {
        // Arrange
        using var ctx = new TestContext();
        var restaurant = new Restaurant { Name = "Test Restaurant" };
        
        // Act
        var component = ctx.RenderComponent<RestaurantCard>(parameters => parameters
            .Add(p => p.Restaurant, restaurant));
        
        // Assert
        component.MarkupMatches("<div>Test Restaurant</div>");
    }
}
```

### Run bUnit Tests

```powershell
dotnet test --filter "FullyQualifiedName~ComponentTests"
```

Expected: bUnit tests execute successfully

---

## Phase 5: Bicep Infrastructure

### Create Bicep Modules

**infra/modules/monitoring.bicep**:

```bicep
param location string = resourceGroup().location
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
output instrumentationKey string = appInsights.properties.InstrumentationKey
```

**infra/modules/budget.bicep**:

```bicep
param budgetName string = 'MonthlyBudget'
param amount int = 5
param contactEmails array

resource budget 'Microsoft.Consumption/budgets@2023-05-01' = {
  name: budgetName
  properties: {
    category: 'Cost'
    amount: amount
    timeGrain: 'Monthly'
    timePeriod: {
      startDate: '2025-11-01'
    }
    notifications: {
      'Actual_GreaterThan_80_Percent': {
        enabled: true
        operator: 'GreaterThan'
        threshold: 80
        contactEmails: contactEmails
        thresholdType: 'Actual'
      }
      'Actual_GreaterThan_100_Percent': {
        enabled: true
        operator: 'GreaterThan'
        threshold: 100
        contactEmails: contactEmails
        thresholdType: 'Actual'
      }
    }
  }
}

output budgetId string = budget.id
```

### Update main.bicep

Add module references:

```bicep
module monitoring 'modules/monitoring.bicep' = {
  name: 'monitoring-deployment'
  params: {
    location: location
    appName: appName
  }
}

module budget 'modules/budget.bicep' = {
  name: 'budget-deployment'
  params: {
    amount: 5
    contactEmails: ['your-email@example.com']
  }
}
```

### Deploy Infrastructure

```powershell
# Login to Azure
azd auth login

# Initialize azd (if not already done)
azd init

# Provision infrastructure
azd provision

# Verify resources created
az resource list --resource-group PoSeeReview --output table
```

Expected: All resources created successfully in Azure

---

## Phase 6: OpenTelemetry Configuration

### Add OpenTelemetry Packages

Add to Directory.Packages.props:

```xml
<PackageVersion Include="OpenTelemetry" Version="1.9.0" />
<PackageVersion Include="OpenTelemetry.Extensions.Hosting" Version="1.9.0" />
<PackageVersion Include="OpenTelemetry.Instrumentation.AspNetCore" Version="1.9.0" />
<PackageVersion Include="Azure.Monitor.OpenTelemetry.Exporter" Version="1.3.0" />
```

Add to src/Po.SeeReview.Api/Po.SeeReview.Api.csproj:

```xml
<PackageReference Include="OpenTelemetry" />
<PackageReference Include="OpenTelemetry.Extensions.Hosting" />
<PackageReference Include="OpenTelemetry.Instrumentation.AspNetCore" />
<PackageReference Include="Azure.Monitor.OpenTelemetry.Exporter" />
```

### Configure in Program.cs

```csharp
// Add after builder creation
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddSource("PoSeeReview.Api")
        .AddAzureMonitorTraceExporter(options =>
        {
            options.ConnectionString = builder.Configuration["ApplicationInsights:ConnectionString"];
        }))
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddMeter("PoSeeReview.Api")
        .AddAzureMonitorMetricExporter(options =>
        {
            options.ConnectionString = builder.Configuration["ApplicationInsights:ConnectionString"];
        }));
```

### Create Custom Telemetry

```csharp
// In a feature handler
using System.Diagnostics;
using System.Diagnostics.Metrics;

public class GenerateComicHandler
{
    private static readonly ActivitySource ActivitySource = new("PoSeeReview.Api");
    private static readonly Meter Meter = new("PoSeeReview.Api");
    private static readonly Counter<long> ComicCounter = Meter.CreateCounter<long>("comics.generated");
    
    public async Task<Comic> Handle(GenerateComicRequest request)
    {
        using var activity = ActivitySource.StartActivity("GenerateComic");
        activity?.SetTag("restaurant.id", request.RestaurantId);
        
        // ... business logic ...
        
        ComicCounter.Add(1, new KeyValuePair<string, object?>("region", request.Region));
        
        return comic;
    }
}
```

### Verify

```powershell
# Run application
dotnet run --project src/Po.SeeReview.Api

# Trigger comic generation
# Check Application Insights for custom traces and metrics
```

Expected: Custom telemetry visible in Application Insights within 2 minutes

---

## Phase 7: KQL Query Library

### Create KQL Queries

**docs/kql/errors.kql**:

```kql
// Top 10 exceptions by count (last 24h)
exceptions
| where timestamp > ago(24h)
| summarize count() by type, outerMessage
| top 10 by count_
| order by count_ desc
```

**docs/kql/performance.kql**:

```kql
// API response time percentiles
requests
| where timestamp > ago(24h)
| summarize p50 = percentile(duration, 50), 
            p95 = percentile(duration, 95), 
            p99 = percentile(duration, 99) 
  by operation_Name
| order by p95 desc
```

**docs/kql/custom-metrics.kql**:

```kql
// Comic generation volume by region
customMetrics
| where name == "comics.generated"
| extend region = tostring(customDimensions.region)
| summarize count() by region, bin(timestamp, 1h)
| render timechart
```

### Test Queries

```powershell
# Navigate to Application Insights in Azure Portal
# Open Logs blade
# Paste query and run
```

Expected: Queries execute successfully and return relevant data

---

## Phase 8: Production Diagnostics

### Enable Snapshot Debugger

1. Navigate to Azure Portal → App Service → Application Insights
2. Click "Snapshot Debugger"
3. Toggle "Enable Snapshot Debugger" to ON
4. Configure settings:
   - Collection limit: 5 snapshots/hour
   - Retention: 14 days
5. Save configuration

### Enable Profiler

1. In same Application Insights blade, click "Profiler"
2. Toggle "Enable Profiler" to ON
3. Configure settings:
   - Duration: 2 minutes
   - CPU threshold: 80%
4. Save configuration

### Document in deployment.md

Add section to docs/deployment.md:

```markdown
## Production Diagnostics

### Snapshot Debugger
- Enabled on App Service
- Captures application state during exceptions
- Access snapshots in Application Insights → Failures → Exceptions → Snapshot

### Profiler
- Enabled on App Service
- Captures performance traces
- Access profiles in Application Insights → Performance → Profiler traces

**Security Note**: Snapshots may contain sensitive data. Verify snapshots do not contain PII before sharing.
```

---

## Verification Checklist

After completing all phases:

- [ ] Directory.Packages.props exists and manages all package versions
- [ ] All .csproj files have `<Nullable>enable</Nullable>`
- [ ] docs/nullable-warnings.md exists with warning inventory
- [ ] Coverage reports generate successfully in docs/coverage/
- [ ] bUnit tests run and pass
- [ ] Bicep modules deploy successfully via azd
- [ ] OpenTelemetry telemetry appears in Application Insights
- [ ] KQL queries execute and return data
- [ ] Snapshot Debugger and Profiler enabled in Portal
- [ ] All constitution checklist items marked complete

---

## Troubleshooting

### Directory.Packages.props not working

- Ensure `<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>` is set
- Verify .csproj files have no Version attributes on PackageReference
- Run `dotnet restore --force`

### Nullable warnings overwhelming

- Start with High severity warnings first
- Use `#nullable disable` temporarily on large files
- Resolve incrementally over multiple sprints

### Coverage collection fails

- Verify .NET SDK includes dotnet-coverage tool
- Check runsettings file syntax
- Ensure tests are discoverable (`dotnet test --list-tests`)

### Bicep deployment fails

- Verify Azure CLI logged in (`az account show`)
- Check resource naming doesn't conflict with existing resources
- Review Bicep validation errors (`azd provision --debug`)

### Telemetry not appearing

- Verify Application Insights connection string is correct
- Check network connectivity to Azure
- Wait up to 5 minutes for telemetry ingestion

---

## Next Steps

1. Run `/speckit.tasks` to generate detailed task breakdown
2. Begin implementation following task order (T136-T155)
3. Track progress via constitution checklist in plan.md
4. Update agent context after completing infrastructure setup

---

**End of Quickstart**
