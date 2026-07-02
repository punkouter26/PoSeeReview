#requires -Version 7.0
<#
.SYNOPSIS
    Reads GoogleMaps--ApiKey (and PoSeeReview--GoogleMaps--ApiKey) from kv-poshared
    and restarts the PoSeeReview.Api with the real key as GoogleMaps__ApiKey.

.DESCRIPTION
    NET_RULES 5.1 / 6.x — Dev-env hot-fix for the "API key not valid" Google Places
    error on https://localhost:5001/. The app's StartupSecretValidator is fail-fast
    on GoogleMaps:ApiKey in every environment, but it does NOT validate the value
    against Google. So an auth'd az session can inject the real key and the app
    boots normally. AI secrets (AzureOpenAI*, Google:GeminiApiKey) remain as
    warnings in Development and are not required to pass through this script.

.PARAMETER MapsKey
    Optional. If omitted, the script reads it from kv-poshared. If both reads fail,
    supply it here (e.g. -MapsKey 'AIza...').

.EXAMPLE
    .\SCRIPTS\inject-google-maps-key.ps1
.EXAMPLE
    .\SCRIPTS\inject-google-maps-key.ps1 -MapsKey 'AIzaSyD-xxxxxxxxxxxxxxxxxxxxxxxxxxx'
#>
[CmdletBinding()]
param(
    [string]$MapsKey
)

$ErrorActionPreference = 'Stop'
$ProjectRoot = Split-Path -Parent $PSScriptRoot

function Write-Step($msg) { Write-Host "==> $msg" -ForegroundColor Cyan }
function Write-Ok($msg)   { Write-Host "    [ok] $msg" -ForegroundColor Green }
function Write-Fail($msg) { Write-Host "    [fail] $msg" -ForegroundColor Red }

try {
    Write-Step "Checking az login state"
    $acct = az account show --query "{name:name, user:user.name}" -o tsv 2>$null
    if (-not $acct) {
        Write-Fail "Not logged in to az. Run: az login --use-device-code"
        exit 1
    }
    Write-Ok "Logged in as: $acct"
}
catch {
    Write-Fail "az CLI not available or not logged in: $($_.Exception.Message)"
    exit 1
}

if (-not $MapsKey) {
    Write-Step "Reading GoogleMaps--ApiKey from kv-poshared"
    $sharedKey = az keyvault secret show --vault-name kv-poshared --name GoogleMaps--ApiKey --query value -o tsv 2>$null
    $appKey    = az keyvault secret show --vault-name kv-poshared --name PoSeeReview--GoogleMaps--ApiKey --query value -o tsv 2>$null
    # App-prefixed wins (second pass in KeyVaultConfigurationExtensions).
    if ($appKey -and $appKey.Trim()) {
        $MapsKey = $appKey.Trim()
        Write-Ok "Using PoSeeReview--GoogleMaps--ApiKey (app-prefixed)"
    }
    elseif ($sharedKey -and $sharedKey.Trim()) {
        $MapsKey = $sharedKey.Trim()
        Write-Ok "Using GoogleMaps--ApiKey (shared)"
    }
    else {
        Write-Fail "No key found in vault and -MapsKey not provided."
        exit 1
    }
}

Write-Step "Killing existing dotnet processes"
Get-Process dotnet -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2
Write-Ok "Killed"

Write-Step "Starting Azurite container (if not running)"
docker start poseereview-azurite 2>$null
if ($LASTEXITCODE -ne 0) {
    docker compose -f (Join-Path $ProjectRoot 'docker-compose.yml') up -d azurite
}
Write-Ok "Azurite ready"

Write-Step "Launching PoSeeReview.Api with GoogleMaps__ApiKey"
Set-Location $ProjectRoot
$env:ASPNETCORE_ENVIRONMENT                = 'Development'
$env:GoogleMaps__ApiKey                    = $MapsKey
$env:AzureOpenAI__Endpoint                 = 'https://placeholder.cognitiveservices.azure.com/'
$env:AzureOpenAI__ApiKey                   = 'dev-placeholder-key'
$env:AzureOpenAI__DeploymentName           = 'gpt-5.4-nano'
$env:Google__GeminiApiKey                  = 'dev-placeholder-gemini'
$env:AZURE_TABLE_STORAGE_CONNECTION_STRING = 'UseDevelopmentStorage=true'
$env:AZURE_BLOB_STORAGE_CONNECTION_STRING  = 'UseDevelopmentStorage=true'

Write-Step "Launching (logs at $env:TEMP\poseereview-api.log)"
dotnet run --project src/PoSeeReview.Api --launch-profile https 2>&1 |
    Tee-Object -FilePath (Join-Path $env:TEMP 'poseereview-api.log')