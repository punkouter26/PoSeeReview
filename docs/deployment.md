# Deployment Guide

This document summarizes the steps required to deploy PoSeeReview to Azure App Service with Application Insights monitoring and the required backing services.

## Prerequisites

- Azure subscription with access to:
  - Azure App Service (Linux)
  - Azure Storage Account (Tables + Blobs)
  - Azure Application Insights
  - Azure OpenAI resource with GPT-4o-mini and DALL-E 3 deployments
- Google Cloud project with Places API enabled and API key provisioned
- Azure Developer CLI (`azd`) v1.9+ or Azure CLI + Bicep if deploying manually
- .NET 9 SDK installed locally (matches `global.json`)

## 1. Provision Azure Resources

Run the infrastructure deployment from the repository root. This provisions App Service, Storage, Key Vault, and Application Insights.

```powershell
# Log in to Azure
azd auth login

# Create or select environment
azd env new poseereview-prod

# Provision + deploy infrastructure
azd up
```

Key resources created:

| Resource | Purpose |
|----------|---------|
| `AppService_plan` | Hosts the API + Blazor WebAssembly app |
| `AppService` | Runs the ASP.NET Core API/hosted client |
| `StorageAccount` | Table storage (comics, leaderboard) + blob storage (images) |
| `ApplicationInsights` | Centralized telemetry + log analytics |
| `KeyVault` | Stores API secrets + connection strings |

> **Note**: The Bicep templates live in `infra/`. To run outside of `azd`, use `az deployment sub create` with the same files.

## 2. Configure Secrets

Store all secrets inside the provisioned Key Vault. The App Service is configured with managed identity and has access policies for `Get` and `List`.

Required secrets:

| Secret Name | Description |
|-------------|-------------|
| `AzureStorage--ConnectionString` | Storage account connection string (for tables & blobs) |
| `AzureStorage--ComicsContainerName` | Optional: override blob container name (defaults to `comics`) |
| `AzureStorage--ComicsTableName` | Optional: override table name (defaults to `PoSeeReviewComics`) |
| `AzureOpenAI--Endpoint` | Azure OpenAI endpoint URL |
| `AzureOpenAI--ApiKey` | Azure OpenAI API key |
| `AzureOpenAI--DeploymentName` | GPT-4o-mini deployment name |
| `AzureOpenAI--DalleDeploymentName` | DALL-E 3 deployment name |
| `GoogleMaps--ApiKey` | Google Maps Places API key |
| `ApplicationInsights--ConnectionString` | App Insights connection string (if not using managed identity) |

Set secrets using Azure CLI:

```powershell
$vault = "$(azd env get-values --output json | jq -r '.KEY_VAULT_NAME')"

az keyvault secret set --vault-name $vault --name "AzureOpenAI--Endpoint" --value "https://YOUR_RESOURCE.openai.azure.com/"
az keyvault secret set --vault-name $vault --name "AzureOpenAI--ApiKey" --value "YOUR_OPENAI_KEY"
az keyvault secret set --vault-name $vault --name "GoogleMaps--ApiKey" --value "YOUR_GOOGLE_MAPS_KEY"
```

The App Service reads secrets via Key Vault references defined in `infra/main.bicep`. No secrets should live in `appsettings.json` or source control.

## 3. Configure Deployment Slots (Optional)

For zero-downtime releases:

```powershell
# Create staging slot
az webapp deployment slot create --resource-group <rg> --name <app-name> --slot staging

# Swap after verifying
az webapp deployment slot swap --resource-group <rg> --name <app-name> --slot staging --target-slot production
```

Update CI pipeline (see `.github/workflows/ci.yml`) to deploy to the staging slot before swapping.

## 4. Build & Publish

To publish locally and push artifacts to the App Service:

```powershell
# Restore + build
dotnet restore
 dotnet publish src/Po.SeeReview.Api/Po.SeeReview.Api.csproj -c Release -o publish

# Deploy zipped payload (requires az CLI)
az webapp deploy --resource-group <rg> --name <app-name> --src-path publish --type zip
```

Alternatively, rely on CI to build and deploy (see next section).

## 5. Continuous Integration / Deployment

The GitHub Actions workflow (`.github/workflows/ci.yml`) performs:

1. Checkout + .NET SDK install (respecting `global.json`)
2. `dotnet restore`, `dotnet build`, `dotnet format --verify-no-changes`
3. `dotnet test` (unit + integration)
4. Constitution validation script (`scripts/ci/validate-constitution.ps1`)

Extend the workflow with an additional job to deploy via `azd deploy` or `az webapp deploy` once tests pass. Recommended secrets for Actions:

- `AZURE_CREDENTIALS` (service principal JSON)
- `AZURE_SUBSCRIPTION_ID`
- `AZURE_ENV_NAME` (azd environment)

## 6. Post-Deployment Health Checks

After each deployment:

- Browse to `https://<app-url>/swagger` to verify API availability
- Check `https://<app-url>/api/health` and `/api/health/ready`
- Verify Application Insights traces (Live Metrics) show request telemetry with correlation IDs
- Confirm rate limiting by issuing >60 requests/minute from the same client (expect HTTP 429)
- Ensure `ExpiredComicCleanupService` logs purge operations daily (App Insights query `traces | where message contains "Expired comic cleanup"`)

## 7. Rollback Plan

To revert to the previous deployment:

1. If using deployment slots, swap back to production.
2. If using zip deploy, redeploy the last known-good artifact.
3. Restore comics from backup if required (blob snapshots are enabled by default in the Bicep modules).

## Troubleshooting

| Issue | Resolution |
|-------|------------|
| 500 errors from comic generation | Check Application Insights exceptions; confirm OpenAI & Google API keys are valid |
| 429 responses | Rate limiting reached. Adjust limits via `appsettings` or `AddRateLimiter` configuration |
| Comics not expiring | Verify `Cleanup:ExpiredComicIntervalMinutes` setting and ensure hosted service logs appear |
| Takedown request not removing leaderboard entry | Confirm region value matches stored partition key (e.g., `US-WA-Seattle`) |

For deeper diagnostics, use `az webapp log tail --name <app-name> --resource-group <rg>` or Application Insights queries.

---

**Next steps**: Keep infrastructure definitions updated in `infra/` and ensure each release updates this document when deployment procedures change.
