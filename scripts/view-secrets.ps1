# View all secrets in Azure Key Vault
# Usage: .\scripts\view-secrets.ps1

param(
    [string]$VaultName = "poseereview-kv"
)

Write-Host "🔑 Secrets in Key Vault: $VaultName" -ForegroundColor Cyan
Write-Host ""

# Get all secret names
$secrets = az keyvault secret list --vault-name $VaultName --query "[].name" -o tsv

foreach ($secretName in $secrets) {
    $value = az keyvault secret show --vault-name $VaultName --name $secretName --query "value" -o tsv
    
    # Convert -- back to : for display
    $displayName = $secretName -replace '--', ':'
    
    Write-Host "  $displayName" -ForegroundColor Yellow
    Write-Host "    $value" -ForegroundColor Gray
    Write-Host ""
}

Write-Host "✅ Total secrets: $($secrets.Count)" -ForegroundColor Green
