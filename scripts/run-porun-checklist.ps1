param(
	[switch]$CompareAzure,
	[switch]$RunE2ESmoke,
	[switch]$KeepServerRunning,
	[string]$AzureBaseUrl = "https://app-poseereview.azurewebsites.net",
	[string]$LocalBaseUrl = "http://localhost:5000"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$apiProject = Join-Path $repoRoot "src/Po.SeeReview.Api/Po.SeeReview.Api.csproj"
$apiLogDir = Join-Path $repoRoot "src/Po.SeeReview.Api/logs"
$resultsDir = Join-Path $repoRoot "TESTRESULTS"

if (-not (Test-Path $resultsDir)) {
	New-Item -Path $resultsDir -ItemType Directory | Out-Null
}

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$summaryPath = Join-Path $resultsDir "porun-summary-$timestamp.json"

$report = [ordered]@{
	timestampUtc = (Get-Date).ToUniversalTime().ToString("o")
	repoRoot = $repoRoot
	localBaseUrl = $LocalBaseUrl
	compareAzure = [bool]$CompareAzure
	runE2ESmoke = [bool]$RunE2ESmoke
	checks = [ordered]@{}
	notes = @()
}

function Test-PortListening {
	param([int]$Port)

	$connections = Get-NetTCPConnection -LocalPort $Port -ErrorAction SilentlyContinue |
		Where-Object { $_.State -eq "Listen" }
	return ($null -ne $connections)
}

function Invoke-JsonEndpoint {
	param([string]$Url)

	try {
		$response = Invoke-RestMethod -Uri $Url -Method Get -TimeoutSec 45
		return [ordered]@{
			ok = $true
			url = $Url
			payload = $response
		}
	}
	catch {
		return [ordered]@{
			ok = $false
			url = $Url
			error = $_.Exception.Message
		}
	}
}

Write-Host "[PoRun] Collecting process and dependency state..." -ForegroundColor Cyan

$apiDotnetProcesses = @(Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" -ErrorAction SilentlyContinue |
	Where-Object { $_.CommandLine -like "*Po.SeeReview.Api*" })

$report.checks.staleApiProcess = [ordered]@{
	count = $apiDotnetProcesses.Count
	processIds = @($apiDotnetProcesses | Select-Object -ExpandProperty ProcessId)
}

$report.checks.ports = [ordered]@{
	local5000 = Test-PortListening -Port 5000
	local5001 = Test-PortListening -Port 5001
	azurite10000 = Test-PortListening -Port 10000
	azurite10002 = Test-PortListening -Port 10002
}

try {
	$dockerPs = docker ps --format "{{.Image}}|{{.Status}}|{{.Ports}}" 2>$null
	$report.checks.docker = [ordered]@{
		ok = $true
		containers = @($dockerPs)
		azuriteRunning = (@($dockerPs) -match "azurite").Count -gt 0
	}
}
catch {
	$report.checks.docker = [ordered]@{
		ok = $false
		error = $_.Exception.Message
	}
}

$startedLocalServer = $false
$serverProcess = $null

if (-not (Test-PortListening -Port 5000)) {
	Write-Host "[PoRun] Starting local API host..." -ForegroundColor Cyan
	$serverProcess = Start-Process -FilePath "dotnet" -ArgumentList @("run", "--project", $apiProject, "--launch-profile", "https") -WorkingDirectory $repoRoot -PassThru
	$startedLocalServer = $true

	$timeoutAt = (Get-Date).AddSeconds(120)
	while ((Get-Date) -lt $timeoutAt) {
		if (Test-PortListening -Port 5000) {
			break
		}

		Start-Sleep -Milliseconds 500
	}
}

$report.checks.localServer = [ordered]@{
	startedByScript = $startedLocalServer
	processId = if ($serverProcess) { $serverProcess.Id } else { $null }
	local5000Listening = Test-PortListening -Port 5000
}

Write-Host "[PoRun] Running local endpoint smoke checks..." -ForegroundColor Cyan

$report.checks.localHealth = Invoke-JsonEndpoint -Url "$LocalBaseUrl/api/health"
$report.checks.localReady = Invoke-JsonEndpoint -Url "$LocalBaseUrl/api/health/ready"
$report.checks.localDiag = Invoke-JsonEndpoint -Url "$LocalBaseUrl/api/diag"
$report.checks.localSearch = Invoke-JsonEndpoint -Url "$LocalBaseUrl/api/restaurants/search?location=Seattle&limit=5"
$report.checks.localLeaderboard = Invoke-JsonEndpoint -Url "$LocalBaseUrl/api/leaderboard?region=US&limit=10"

if (Test-Path $apiLogDir) {
	$latestLog = Get-ChildItem -Path $apiLogDir -Filter "*.log" -ErrorAction SilentlyContinue |
		Sort-Object LastWriteTime -Descending |
		Select-Object -First 1

	if ($latestLog) {
		$lines = Get-Content -Path $latestLog.FullName -Tail 300 -ErrorAction SilentlyContinue
		$warnErr = @($lines | Select-String -Pattern "\[WRN\]|\[ERR\]|Exception|Failed|429| 5\d\d " -CaseSensitive:$false)

		$report.checks.logs = [ordered]@{
			latestLogFile = $latestLog.FullName
			warningOrErrorLines = $warnErr.Count
			sample = @($warnErr | Select-Object -First 20 | ForEach-Object { $_.Line })
		}
	}
}

if ($CompareAzure) {
	Write-Host "[PoRun] Running deployed Azure smoke comparison..." -ForegroundColor Cyan
	$report.checks.azureHealth = Invoke-JsonEndpoint -Url "$AzureBaseUrl/api/health"
}

if ($RunE2ESmoke) {
	Write-Host "[PoRun] Running Playwright smoke suite..." -ForegroundColor Cyan
	Push-Location (Join-Path $repoRoot "tests/e2e")
	try {
		if (-not (Test-Path "package-lock.json")) {
			npm install
		}

		npm run test:smoke
		$report.checks.e2eSmoke = [ordered]@{
			ok = ($LASTEXITCODE -eq 0)
			exitCode = $LASTEXITCODE
		}
	}
	catch {
		$report.checks.e2eSmoke = [ordered]@{
			ok = $false
			error = $_.Exception.Message
		}
	}
	finally {
		Pop-Location
	}
}

if ($startedLocalServer -and -not $KeepServerRunning -and $serverProcess) {
	Write-Host "[PoRun] Stopping local API host started by script..." -ForegroundColor Cyan
	try {
		if (-not $serverProcess.HasExited) {
			Stop-Process -Id $serverProcess.Id -Force
		}
	}
	catch {
		$report.notes += "Could not stop local API process cleanly: $($_.Exception.Message)"
	}
}

$report | ConvertTo-Json -Depth 10 | Set-Content -Path $summaryPath -Encoding UTF8

Write-Host "[PoRun] Completed. Summary written to:" -ForegroundColor Green
Write-Host "  $summaryPath" -ForegroundColor Green
