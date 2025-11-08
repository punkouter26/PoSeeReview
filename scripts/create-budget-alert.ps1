# Create Azure Budget Alert for PoSeeReview
# Limits spending to $2/day with email alerts

# Configuration
$resourceGroupName = "PoSeeReview"
$budgetName = "PoSeeReview-Daily-Budget"
$dailyLimit = 2
$alertEmail = "your.email@example.com"  # REPLACE WITH YOUR EMAIL

# Login to Azure (if not already logged in)
Write-Host "Logging in to Azure..." -ForegroundColor Cyan
az login

# Get subscription ID
$subscriptionId = az account show --query id -o tsv
Write-Host "Using subscription: $subscriptionId" -ForegroundColor Green

# Get resource group ID
$resourceGroupId = az group show --name $resourceGroupName --query id -o tsv
Write-Host "Resource Group ID: $resourceGroupId" -ForegroundColor Green

# Set dates
$startDate = (Get-Date -Format "yyyy-MM-dd")
$endDate = (Get-Date).AddYears(1).ToString("yyyy-MM-dd")

# Create budget with Azure CLI
Write-Host "Creating daily budget of `$$dailyLimit..." -ForegroundColor Cyan

az consumption budget create `
    --budget-name $budgetName `
    --category Cost `
    --amount $dailyLimit `
    --time-grain Daily `
    --start-date $startDate `
    --end-date $endDate `
    --resource-group-filter $resourceGroupName `
    --subscription $subscriptionId

Write-Host "`nBudget created successfully!" -ForegroundColor Green

# Note: Email notifications require creating an Action Group first
Write-Host "`nTo add email alerts:" -ForegroundColor Yellow
Write-Host "1. Create Action Group:" -ForegroundColor White
Write-Host "   az monitor action-group create --name 'BudgetAlerts' --resource-group '$resourceGroupName' --short-name 'BudgetAlert' --email-receiver name='$alertEmail' email-address='$alertEmail'" -ForegroundColor Gray
Write-Host "`n2. Then update budget to use the action group via Azure Portal" -ForegroundColor White
Write-Host "   Portal > Cost Management > Budgets > $budgetName > Notifications" -ForegroundColor Gray

Write-Host "`nQuick access:" -ForegroundColor Yellow
Write-Host "https://portal.azure.com/#view/Microsoft_Azure_CostManagement/Menu/~/budgets" -ForegroundColor Cyan

