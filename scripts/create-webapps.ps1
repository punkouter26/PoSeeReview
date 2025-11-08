#!/usr/bin/env pwsh

# Script to create App Services in PoSeeReview using PoSharedAppServicePlan1
# Retries with exponential backoff to handle network issues

$resourceGroup = "PoSeeReview"
$planId = "/subscriptions/bbb8dfbe-9169-432f-9b7a-fbf861b51037/resourceGroups/PoShared/providers/Microsoft.Web/serverfarms/PoSharedAppServicePlan1"
$apiAppName = "PoSeeReview"
$runtime = "dotnet:9"
$maxRetries = 10
$baseDelay = 5

function Create-WebApp {
    param (
        [string]$Name,
        [string]$ResourceGroup,
        [string]$PlanId,
        [string]$Runtime
    )
    
    Write-Host "`nCreating webapp: $Name" -ForegroundColor Cyan
    
    for ($i = 1; $i -le $maxRetries; $i++) {
        Write-Host "  Attempt $i of $maxRetries..." -ForegroundColor Yellow
        
        $result = az webapp create `
            --name $Name `
            --resource-group $ResourceGroup `
            --plan $PlanId `
            --runtime $Runtime `
            2>&1
        
        if ($LASTEXITCODE -eq 0) {
            Write-Host "  ✅ Successfully created $Name" -ForegroundColor Green
            return $true
        }
        
        # Check if already exists
        if ($result -like "*ResourceExists*" -or $result -like "*already exists*") {
            Write-Host "  ✅ $Name already exists" -ForegroundColor Green
            return $true
        }
        
        Write-Host "  ❌ Failed: $result" -ForegroundColor Red
        
        if ($i -lt $maxRetries) {
            $delay = $baseDelay * $i
            Write-Host "  Waiting $delay seconds before retry..." -ForegroundColor Gray
            Start-Sleep -Seconds $delay
        }
    }
    
    Write-Host "  ❌ Failed to create $Name after $maxRetries attempts" -ForegroundColor Red
    return $false
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Creating App Service in $resourceGroup" -ForegroundColor Cyan
Write-Host "Using plan: PoSharedAppServicePlan1" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

# Create App Service
$apiSuccess = Create-WebApp -Name $apiAppName -ResourceGroup $resourceGroup -PlanId $planId -Runtime $runtime

# Summary
Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "Summary:" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

if ($apiSuccess) {
    Write-Host "✅ App Service: https://$apiAppName.azurewebsites.net" -ForegroundColor Green
} else {
    Write-Host "❌ App Service: FAILED" -ForegroundColor Red
}

if ($apiSuccess) {
    Write-Host "`n🎉 App Service created successfully!" -ForegroundColor Green
    Write-Host "`nNext steps:" -ForegroundColor Yellow
    Write-Host "1. Trigger GitHub Actions deployment: gh workflow run 'Build and Deploy to Azure'" -ForegroundColor White
    Write-Host "2. Or run the setup script to configure app settings: cd scripts && .\setup-azure-appservices.ps1" -ForegroundColor White
    exit 0
} else {
    Write-Host "`n⚠️ App Service failed to create. Check errors above." -ForegroundColor Red
    exit 1
}
