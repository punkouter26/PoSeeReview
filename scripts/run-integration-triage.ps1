param(
    [ValidateSet('Api', 'Storage', 'Services', 'EdgeCases')]
    [string]$Domain = 'Api'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

Write-Host "Running integration triage lane: $Domain" -ForegroundColor Cyan

Push-Location C:\
try {
    dotnet test "$repoRoot\tests\Po.SeeReview.IntegrationTests\Po.SeeReview.IntegrationTests.csproj" --filter "Domain=$Domain" -c Debug
}
finally {
    Pop-Location
}
