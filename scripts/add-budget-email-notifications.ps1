#!/usr/bin/env pwsh

# Add email notifications to existing budget
# This script updates the PoSeeReview-Daily-Budget with email alerts

Write-Host "Adding email notifications to budget..." -ForegroundColor Cyan

$startDate = (Get-Date -Format "yyyy-MM-01")
$endDate = (Get-Date).AddYears(1).ToString("yyyy-MM-01")
$email = "punkouter26@gmail.com"

# Create notifications JSON
$notifications = @{
    "actual_GreaterThan_80_Percent" = @{
        "enabled" = $true
        "operator" = "GreaterThan"
        "threshold" = 80
        "contactEmails" = @($email)
    }
    "actual_GreaterThan_100_Percent" = @{
        "enabled" = $true
        "operator" = "GreaterThan"
        "threshold" = 100
        "contactEmails" = @($email)
    }
    "actual_GreaterThan_120_Percent" = @{
        "enabled" = $true
        "operator" = "GreaterThan"
        "threshold" = 120
        "contactEmails" = @($email)
    }
} | ConvertTo-Json -Depth 10 -Compress

# Update budget with notifications
az consumption budget create `
    --budget-name "PoSeeReview-Daily-Budget" `
    --resource-group "PoSeeReview" `
    --category "Cost" `
    --amount 60 `
    --time-grain "Monthly" `
    --start-date $startDate `
    --end-date $endDate `
    --notifications $notifications

if ($LASTEXITCODE -eq 0) {
    Write-Host "`n✅ Budget updated successfully with email notifications!" -ForegroundColor Green
    Write-Host "`nYou will receive email alerts at $email when:" -ForegroundColor Yellow
    Write-Host "  - 80% of budget is reached ($1.60/day)" -ForegroundColor White
    Write-Host "  - 100% of budget is reached ($2.00/day)" -ForegroundColor White
    Write-Host "  - 120% of budget is exceeded ($2.40/day)" -ForegroundColor White
} else {
    Write-Host "`n❌ Failed to update budget" -ForegroundColor Red
}
