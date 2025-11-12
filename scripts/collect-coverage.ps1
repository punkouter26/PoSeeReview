#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Collects code coverage from all test projects and generates reports.

.DESCRIPTION
    This script runs dotnet test with coverage collection enabled, then converts
    the coverage data to multiple formats (XML, HTML) for different use cases.

.PARAMETER OutputDir
    Directory where coverage reports will be saved. Default: docs/coverage

.PARAMETER Format
    Output format(s): all, xml, html, cobertura. Default: all

.EXAMPLE
    .\collect-coverage.ps1
    Collects coverage and generates all report formats in docs/coverage/

.EXAMPLE
    .\collect-coverage.ps1 -OutputDir "TestResults" -Format xml
    Collects coverage and generates only XML report in TestResults/
#>

param(
    [string]$OutputDir = "docs/coverage",
    [ValidateSet("all", "xml", "html", "cobertura")]
    [string]$Format = "all"
)

# Ensure output directory exists
$OutputPath = Join-Path $PSScriptRoot ".." $OutputDir
if (-not (Test-Path $OutputPath)) {
    New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null
}

Write-Host "=====================================" -ForegroundColor Cyan
Write-Host " Code Coverage Collection" -ForegroundColor Cyan
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host ""

# Step 1: Collect coverage
Write-Host "Step 1: Collecting coverage data..." -ForegroundColor Yellow
$coverageFile = Join-Path $OutputPath "coverage.coverage"

try {
    dotnet-coverage collect "dotnet test" --output $coverageFile
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Coverage collection failed with exit code $LASTEXITCODE"
        exit $LASTEXITCODE
    }
    Write-Host "✓ Coverage data collected: $coverageFile" -ForegroundColor Green
}
catch {
    Write-Error "Failed to collect coverage: $_"
    exit 1
}

Write-Host ""

# Step 2: Convert to XML format (for CI/CD integration)
if ($Format -eq "all" -or $Format -eq "xml") {
    Write-Host "Step 2: Converting to XML format..." -ForegroundColor Yellow
    $xmlFile = Join-Path $OutputPath "coverage.xml"
    
    try {
        dotnet-coverage merge -o $xmlFile -f xml $coverageFile
        if ($LASTEXITCODE -ne 0) {
            Write-Error "XML conversion failed with exit code $LASTEXITCODE"
            exit $LASTEXITCODE
        }
        Write-Host "✓ XML report generated: $xmlFile" -ForegroundColor Green
    }
    catch {
        Write-Error "Failed to convert to XML: $_"
        exit 1
    }
}

Write-Host ""

# Step 3: Convert to Cobertura format (for GitHub Actions)
if ($Format -eq "all" -or $Format -eq "cobertura") {
    Write-Host "Step 3: Converting to Cobertura format..." -ForegroundColor Yellow
    $coberturaFile = Join-Path $OutputPath "coverage.cobertura.xml"
    
    try {
        dotnet-coverage merge -o $coberturaFile -f cobertura $coverageFile
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "Cobertura conversion failed - continuing anyway"
        }
        else {
            Write-Host "✓ Cobertura report generated: $coberturaFile" -ForegroundColor Green
        }
    }
    catch {
        Write-Warning "Failed to convert to Cobertura: $_"
    }
}

Write-Host ""

# Step 4: Convert to HTML format (for human-readable reports)
# NOTE: HTML format is not supported by dotnet-coverage as of v18.1.0
# Use XML viewers or CI/CD integrations for coverage visualization
Write-Host "Step 4: HTML generation not supported by dotnet-coverage" -ForegroundColor Yellow
Write-Host "✓ Use coverage.xml with Visual Studio or XML viewers" -ForegroundColor Green
Write-Host "✓ Use coverage.cobertura.xml for GitHub Actions and CI/CD" -ForegroundColor Green

Write-Host ""
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host " Coverage Collection Complete!" -ForegroundColor Green
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Reports saved to: $OutputPath" -ForegroundColor White
Write-Host ""
Write-Host "Generated files:" -ForegroundColor Yellow
Write-Host "  - coverage.coverage (binary)" -ForegroundColor Cyan
Write-Host "  - coverage.xml (detailed XML report)" -ForegroundColor Cyan
Write-Host "  - coverage.cobertura.xml (CI/CD format)" -ForegroundColor Cyan
Write-Host ""
Write-Host "To view coverage details:" -ForegroundColor Yellow
Write-Host "  Open coverage.xml in Visual Studio or VS Code" -ForegroundColor Cyan
Write-Host ""
Write-Host "Coverage threshold: 80% (informational - does not fail build)" -ForegroundColor White
Write-Host ""
