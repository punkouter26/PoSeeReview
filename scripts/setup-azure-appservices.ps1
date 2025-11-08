#!/usr/bin/env pwsh

# Setup script for Azure App Services and GitHub Actions CI/CD
# This script creates the necessary Azure resources and configures GitHub secrets

$resourceGroup = "PoSeeReview"
$location = "centralus"  # eastus has quota issues, centralus works
$apiAppName = "poseereview-api"
$clientAppName = "poseereview-client"
$appServicePlan = "PoSharedAppServicePlan1"
$appServicePlanResourceGroup = "PoShared"

Write-Host "Setting up Azure App Services for PoSeeReview..." -ForegroundColor Cyan

# Check if App Service Plan already exists
$existingPlan = az appservice plan show --name $appServicePlan --resource-group $appServicePlanResourceGroup 2>$null
if ($existingPlan) {
    Write-Host "`n✓ App Service Plan already exists in $appServicePlanResourceGroup" -ForegroundColor Green
} else {
    # Create App Service Plan (Windows, Free F1 tier)
    Write-Host "`nCreating App Service Plan (Free tier) in $appServicePlanResourceGroup..." -ForegroundColor Yellow
    az appservice plan create `
        --name $appServicePlan `
        --resource-group $appServicePlanResourceGroup `
        --location $location `
        --sku F1
}

# Use the full plan ID (cross-resource group reference)
$planId = "/subscriptions/bbb8dfbe-9169-432f-9b7a-fbf861b51037/resourceGroups/$appServicePlanResourceGroup/providers/Microsoft.Web/serverfarms/$appServicePlan"

# Create API App Service (.NET 9.0 on Windows) in PoSeeReview resource group
Write-Host "`nCreating API App Service in $resourceGroup..." -ForegroundColor Yellow
az webapp create `
    --name $apiAppName `
    --resource-group $resourceGroup `
    --plan $planId `
    --runtime "dotnet:9"

# Create Client App Service (for Blazor WASM) in PoSeeReview resource group
Write-Host "`nCreating Client App Service in $resourceGroup..." -ForegroundColor Yellow
az webapp create `
    --name $clientAppName `
    --resource-group $resourceGroup `
    --plan $planId `
    --runtime "dotnet:9"

# Configure API App Settings
Write-Host "`nConfiguring API App Settings..." -ForegroundColor Yellow
$storageConnectionString = az storage account show-connection-string `
    --name poseereviewstorage `
    --resource-group $resourceGroup `
    --query connectionString `
    -o tsv

az webapp config appsettings set `
    --name $apiAppName `
    --resource-group $resourceGroup `
    --settings `
        "ConnectionStrings__AzureTableStorage=$storageConnectionString" `
        "ConnectionStrings__AzureBlobStorage=$storageConnectionString" `
        "ASPNETCORE_ENVIRONMENT=Production"

# Enable managed identity for API
Write-Host "`nEnabling Managed Identity..." -ForegroundColor Yellow
az webapp identity assign `
    --name $apiAppName `
    --resource-group $resourceGroup

# Configure CORS for Client
Write-Host "`nConfiguring CORS..." -ForegroundColor Yellow
$clientUrl = az webapp show --name $clientAppName --resource-group $resourceGroup --query defaultHostName -o tsv
az webapp cors add `
    --name $apiAppName `
    --resource-group $resourceGroup `
    --allowed-origins "https://$clientUrl"

# Create service principal for GitHub Actions
Write-Host "`nCreating Service Principal for GitHub Actions..." -ForegroundColor Yellow
$subscriptionId = az account show --query id -o tsv
$spName = "github-actions-poseereview"

$sp = az ad sp create-for-rbac `
    --name $spName `
    --role contributor `
    --scopes /subscriptions/$subscriptionId/resourceGroups/$resourceGroup `
    --sdk-auth

Write-Host "`n✅ Azure resources created successfully!" -ForegroundColor Green

Write-Host "`n📋 Next Steps:" -ForegroundColor Cyan
Write-Host "1. Add GitHub Secret 'AZURE_CREDENTIALS' with the following value:" -ForegroundColor White
Write-Host $sp -ForegroundColor Gray

Write-Host "`n2. Run the following command to add the secret:" -ForegroundColor White
Write-Host "gh secret set AZURE_CREDENTIALS --body '$sp'" -ForegroundColor Gray

Write-Host "`n3. API URL: https://$apiAppName.azurewebsites.net" -ForegroundColor White
Write-Host "4. Client URL: https://$clientAppName.azurewebsites.net" -ForegroundColor White

Write-Host "`n5. Update Client appsettings to point to API URL" -ForegroundColor Yellow
Write-Host "   File: src/Po.SeeReview.Client/wwwroot/appsettings.json" -ForegroundColor Gray
