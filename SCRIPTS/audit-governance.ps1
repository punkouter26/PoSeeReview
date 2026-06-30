<#
.SYNOPSIS
    Automated Azure Resource Graph (ARG) governance audit for the Po* estate.

.DESCRIPTION
    Flags (and, with -Shutdown, remediates) three classes of governance problems:
      1. Orphan assets      — App Service Plans with zero apps, unattached disks /
                              NICs / public IPs.
      2. Naming violations  — resources whose name does not follow the Po{Solution}
                              convention / approved resource prefixes.
      3. Idle compute       — non-production web apps averaging < 5% CPU over the
                              lookback window.

    By default the script is READ-ONLY (flags only). Pass -Shutdown to stop idle
    non-production web apps that were flagged. Production is never touched.

.PARAMETER SubscriptionId
    Subscription to audit. Defaults to the current az context.

.PARAMETER CpuIdleThreshold
    Average CPU percentage below which non-prod compute is considered idle. Default 5.

.PARAMETER LookbackHours
    Metrics lookback window for idle detection. Default 24.

.PARAMETER Shutdown
    Actually stop idle non-production web apps (otherwise they are only reported).

.EXAMPLE
    ./audit-governance.ps1
    ./audit-governance.ps1 -Shutdown
#>
[CmdletBinding()]
param(
    [string]$SubscriptionId,
    [int]$CpuIdleThreshold = 5,
    [int]$LookbackHours = 24,
    [switch]$Shutdown
)

$ErrorActionPreference = 'Stop'

# Approved resource-name prefixes (extend as the estate grows).
$ApprovedPrefixes = @('asp-', 'app-', 'kv-', 'st', 'log-', 'appi-', 'id-', 'cae-', 'ca-', 'acr', 'rg-')
# Names/resource-groups that signal production (case-insensitive contains).
$ProdMarkers = @('prod', 'production')

function Ensure-Graph {
    if (-not (az extension list --query "[?name=='resource-graph']" -o tsv 2>$null)) {
        Write-Host "Installing az resource-graph extension..." -ForegroundColor Yellow
        az extension add --name resource-graph --only-show-errors | Out-Null
    }
}

function Invoke-Arg([string]$query) {
    $args = @('graph', 'query', '-q', $query, '--first', '1000', '-o', 'json')
    if ($SubscriptionId) { $args += @('--subscriptions', $SubscriptionId) }
    (az @args | ConvertFrom-Json).data
}

Ensure-Graph
if ($SubscriptionId) { az account set --subscription $SubscriptionId | Out-Null }

$flags = [System.Collections.Generic.List[object]]::new()
function Add-Flag($Category, $Name, $ResourceGroup, $Detail, $Id) {
    $flags.Add([pscustomobject]@{
        Category = $Category; Name = $Name; ResourceGroup = $ResourceGroup; Detail = $Detail; Id = $Id
    })
}

# ── 1. Orphan App Service Plans (zero apps) ─────────────────────────────────
Write-Host "`n[1/4] Scanning for orphan App Service Plans..." -ForegroundColor Cyan
$plans = Invoke-Arg @"
resources
| where type =~ 'microsoft.web/serverfarms'
| project name, resourceGroup, id, apps = toint(properties.numberOfSites)
| where apps == 0
"@
foreach ($p in $plans) { Add-Flag 'OrphanPlan' $p.name $p.resourceGroup 'App Service Plan has 0 apps' $p.id }

# ── 2. Orphan network/disk assets ───────────────────────────────────────────
Write-Host "[2/4] Scanning for orphan disks / NICs / public IPs..." -ForegroundColor Cyan
$orphans = Invoke-Arg @"
resources
| where (type =~ 'microsoft.compute/disks' and isnull(managedBy))
     or (type =~ 'microsoft.network/networkinterfaces' and isnull(properties.virtualMachine))
     or (type =~ 'microsoft.network/publicipaddresses' and isnull(properties.ipConfiguration))
| project name, resourceGroup, id, type
"@
foreach ($o in $orphans) { Add-Flag 'OrphanAsset' $o.name $o.resourceGroup "Unattached $($o.type)" $o.id }

# ── 3. Naming-convention violations ─────────────────────────────────────────
Write-Host "[3/4] Scanning for naming-convention violations..." -ForegroundColor Cyan
$all = Invoke-Arg @"
resources
| project name, resourceGroup, id, type
"@
foreach ($r in $all) {
    $okPrefix = $false
    foreach ($pre in $ApprovedPrefixes) { if ($r.name.ToLower().StartsWith($pre)) { $okPrefix = $true; break } }
    # Po{Solution} resource groups and Po-prefixed names are also compliant.
    if ($r.name -match '^(?i)Po[A-Z0-9]') { $okPrefix = $true }
    if (-not $okPrefix) { Add-Flag 'NamingViolation' $r.name $r.resourceGroup "Name does not match approved prefixes or Po{Solution}" $r.id }
}

# ── 4. Idle non-production web apps (< threshold% CPU) ───────────────────────
Write-Host "[4/4] Scanning for idle non-production web apps..." -ForegroundColor Cyan
$webapps = Invoke-Arg @"
resources
| where type =~ 'microsoft.web/sites'
| project name, resourceGroup, id
"@
$startTime = (Get-Date).ToUniversalTime().AddHours(-$LookbackHours).ToString('yyyy-MM-ddTHH:mm:ssZ')
foreach ($w in $webapps) {
    $isProd = $false
    foreach ($m in $ProdMarkers) {
        if ($w.name -match "(?i)$m" -or $w.resourceGroup -match "(?i)$m") { $isProd = $true; break }
    }
    if ($isProd) { continue }

    $avgCpu = az monitor metrics list --resource $w.id --metric "CpuTime" `
        --start-time $startTime --interval PT1H --aggregation Total `
        --query "max(value[0].timeseries[0].data[].total)" -o tsv 2>$null

    # CpuPercentage is plan-level; for sites use CpuTime trend as a low-signal proxy.
    $cpuPct = az monitor metrics list --resource $w.id --metric "CpuPercentage" `
        --start-time $startTime --interval PT1H --aggregation Average `
        --query "avg(value[0].timeseries[0].data[].average)" -o tsv 2>$null

    if ($cpuPct -and [double]$cpuPct -lt $CpuIdleThreshold) {
        Add-Flag 'IdleCompute' $w.name $w.resourceGroup "Avg CPU $([math]::Round([double]$cpuPct,2))% < $CpuIdleThreshold% over ${LookbackHours}h" $w.id
        if ($Shutdown) {
            Write-Host "  Stopping idle non-prod web app: $($w.name)" -ForegroundColor Yellow
            az webapp stop --ids $w.id | Out-Null
        }
    }
}

# ── Report ──────────────────────────────────────────────────────────────────
Write-Host "`n================ GOVERNANCE AUDIT ================" -ForegroundColor Green
if ($flags.Count -eq 0) {
    Write-Host "No governance issues found." -ForegroundColor Green
} else {
    $flags | Sort-Object Category | Format-Table Category, Name, ResourceGroup, Detail -AutoSize
    Write-Host "Total flags: $($flags.Count)" -ForegroundColor Yellow
    if (-not $Shutdown) {
        Write-Host "Read-only run. Re-run with -Shutdown to stop idle non-production compute." -ForegroundColor DarkYellow
    }
}

# Emit machine-readable output for CI consumption.
$flags | ConvertTo-Json -Depth 4
