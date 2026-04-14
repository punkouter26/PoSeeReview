param(
    [switch]$SkipE2E
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

Write-Host "Running capped critical-path smoke tiers..." -ForegroundColor Cyan
Write-Host "Unit cap target: <=100 | Integration cap target: <=50 | E2E cap target: <=25" -ForegroundColor DarkCyan

function Run-TimedCommand {
    param(
        [string]$Name,
        [scriptblock]$Action
    )

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    & $Action
    $exitCode = if ($null -eq $LASTEXITCODE) { 0 } else { [int]$LASTEXITCODE }
    $sw.Stop()

    [pscustomobject]@{
        Tier = $Name
        ExitCode = $exitCode
        Seconds = [math]::Round($sw.Elapsed.TotalSeconds, 2)
    }
}

$results = @()

Push-Location C:\
try {
    $results += Run-TimedCommand -Name 'Unit(CriticalPath)' -Action {
        dotnet test "$repoRoot\tests\Po.SeeReview.UnitTests\Po.SeeReview.UnitTests.csproj" --filter "Suite=CriticalPath" -c Debug
    }

    $results += Run-TimedCommand -Name 'Integration(CriticalPath)' -Action {
        dotnet test "$repoRoot\tests\Po.SeeReview.IntegrationTests\Po.SeeReview.IntegrationTests.csproj" --filter "Suite=CriticalPath" -c Debug
    }
}
finally {
    Pop-Location
}

if (-not $SkipE2E) {
    Push-Location "$repoRoot\tests\e2e"
    try {
        if (-not (Test-Path "package-lock.json")) {
            npm install
        }

        $results += Run-TimedCommand -Name 'E2E(Smoke)' -Action {
            npm run test:smoke
        }
    }
    finally {
        Pop-Location
    }
}

$results | Format-Table -AutoSize | Out-String | Write-Host

$failed = @($results | Where-Object { [int]$_.ExitCode -ne 0 })
if ($failed.Length -gt 0) {
    Write-Host "One or more smoke tiers failed." -ForegroundColor Yellow
    exit 1
}

Write-Host "All smoke tiers passed." -ForegroundColor Green
