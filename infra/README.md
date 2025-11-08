# Azure Infrastructure

This directory contains Bicep templates for provisioning SeeReview infrastructure on Azure.

## Architecture

The infrastructure consists of:

- **Application Insights** - Application monitoring and telemetry
- **Log Analytics Workspace** - Centralized logging
- **Azure Storage Account** - Table Storage (restaurants) + Blob Storage (images)
- **Azure Key Vault** - Secrets management with Managed Identity access
- **App Service Plan** - F1 (Free) for dev, S1 (Standard) for production
- **App Service (API)** - ASP.NET Core 9.0 API with system-assigned Managed Identity
- **Static Web App (Client)** - Blazor WebAssembly SPA
- **Diagnostic Settings** - Forward App Service logs/metrics to Log Analytics

## Prerequisites

1. **Azure CLI** - [Install Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli)
2. **Azure Developer CLI (azd)** - [Install azd](https://learn.microsoft.com/azure/developer/azure-developer-cli/install-azd)
3. **Azure Subscription** - Active Azure subscription

## Deployment with Azure Developer CLI

### First Time Setup

```powershell
# 1. Login to Azure
azd auth login

# 2. Initialize environment
azd init

# 3. Provision and deploy
azd up
```

The `azd up` command will:
- Provision all Azure resources defined in `main.bicep`
- Deploy the API to App Service
- Deploy the Client to Static Web App
- Output connection strings and URLs

### Environment Variables

After `azd up`, the following environment variables will be set:

```powershell
# View all environment variables
azd env get-values

# Key outputs:
# - AZURE_LOCATION
# - AZURE_RESOURCE_GROUP
# - API_URL
# - CLIENT_URL
# - APPLICATION_INSIGHTS_CONNECTION_STRING
# - KEY_VAULT_NAME
# - STORAGE_ACCOUNT_NAME
```

### Update Secrets

The deployment creates placeholder secrets in Key Vault. Update them:

```powershell
# Get Key Vault name
$kvName = azd env get-value KEY_VAULT_NAME

# Update Azure OpenAI secrets
az keyvault secret set --vault-name $kvName --name "azure-openai-endpoint" --value "https://YOUR-ENDPOINT.openai.azure.com/"
az keyvault secret set --vault-name $kvName --name "azure-openai-key" --value "YOUR_ACTUAL_KEY"

# Update Google Maps API key
az keyvault secret set --vault-name $kvName --name "google-maps-key" --value "YOUR_ACTUAL_GOOGLE_MAPS_KEY"
```

### Deploy Updates

```powershell
# Deploy code changes only (no infrastructure changes)
azd deploy

# Deploy specific service
azd deploy api
azd deploy client

# Provision infrastructure changes + deploy
azd up
```

### View Logs

```powershell
# Stream API logs
azd monitor --logs

# Open Azure Portal
azd monitor
```

### Clean Up

```powershell
# Delete all resources
azd down
```

## Manual Deployment with Bicep

If you prefer not to use `azd`:

### 1. Set Variables

```powershell
$location = "eastus"
$environmentName = "dev"
$subscriptionId = "YOUR_SUBSCRIPTION_ID"
```

### 2. Create Deployment

```powershell
az login
az account set --subscription $subscriptionId

# Deploy at subscription scope
az deployment sub create `
  --location $location `
  --template-file ./infra/main.bicep `
  --parameters environmentName=$environmentName location=$location
```

### 3. Retrieve Outputs

```powershell
az deployment sub show `
  --name main `
  --query properties.outputs
```

## Modules

| Module | Purpose | Key Resources |
|--------|---------|---------------|
| `monitoring.bicep` | Observability | Log Analytics, Application Insights |
| `storage.bicep` | Data storage | Storage Account (Tables + Blobs) |
| `keyvault.bicep` | Secrets | Key Vault + RBAC role assignments |
| `appserviceplan.bicep` | Compute | App Service Plan (SKU conditional on environment) |
| `appservice.bicep` | API hosting | App Service with Managed Identity |
| `staticwebapp.bicep` | Client hosting | Static Web App (Free tier) |
| `secrets.bicep` | Secret initialization | Key Vault secrets (placeholders) |

## Key Vault Integration

The API uses **Key Vault References** in App Settings:

```bicep
'AzureOpenAI:ApiKey': '@Microsoft.KeyVault(SecretUri=${keyVault.outputs.endpoint}secrets/azure-openai-key/)'
```

Benefits:
- Secrets never stored in App Service configuration
- Automatic rotation support
- Centralized secret management
- Managed Identity authentication (no credentials in code)

## Environment-Specific Configuration

The `appserviceplan.bicep` module uses conditional SKU selection:

```bicep
var sku = environmentName == 'prod' ? {
  name: 'S1'
  tier: 'Standard'
  capacity: 1
} : {
  name: 'F1'
  tier: 'Free'
  capacity: 1
}
```

Deploy production:
```powershell
azd env new prod
azd up
```

## Monitoring & Diagnostics

All App Service logs/metrics forward to Log Analytics:

- **AppServiceHTTPLogs** - Request logs
- **AppServiceConsoleLogs** - stdout/stderr
- **AppServiceAppLogs** - Application logs (Serilog)
- **AllMetrics** - CPU, memory, request rate, response times

Query in Log Analytics:
```kql
AppServiceConsoleLogs
| where TimeGenerated > ago(1h)
| project TimeGenerated, ResultDescription
| order by TimeGenerated desc
```

## Cost Estimation

### Development Environment (F1 tier)
- App Service Plan: **Free** (F1)
- Static Web App: **Free**
- Storage Account: ~$0.02/GB/month
- Key Vault: $0.03/10k operations
- Application Insights: 5GB/month free, then $2.30/GB
- **Estimated Monthly Cost: < $5**

### Production Environment (S1 tier)
- App Service Plan: **$56/month** (S1)
- Static Web App: **Free**
- Storage Account: ~$0.02/GB/month
- Key Vault: $0.03/10k operations
- Application Insights: $2.30/GB after 5GB free
- **Estimated Monthly Cost: ~$60-80**

## Troubleshooting

### Deployment Fails with "Principal does not exist"

Wait 30-60 seconds after Managed Identity creation before RBAC assignment.

**Solution:** Retry deployment - the Bicep template will pick up where it left off.

### Key Vault Access Denied

Ensure the API's Managed Identity has been assigned the **Key Vault Secrets User** role:

```powershell
$apiPrincipalId = az webapp identity show --name $apiName --resource-group $rgName --query principalId -o tsv
az role assignment create --role "Key Vault Secrets User" --assignee $apiPrincipalId --scope $kvId
```

### Static Web App Not Loading

Verify the `API_BASE_URL` app setting in the Static Web App configuration:

```powershell
az staticwebapp appsettings list --name $swaName --resource-group $rgName
```

## Security Best Practices

1. ✅ **Managed Identity** - No credentials in code
2. ✅ **Key Vault References** - Secrets pulled from Key Vault at runtime
3. ✅ **HTTPS Only** - All traffic encrypted
4. ✅ **TLS 1.2 Minimum** - Enforce modern encryption
5. ✅ **RBAC** - Least-privilege access (Key Vault Secrets User)
6. ✅ **Soft Delete** - 7-day recovery window for Key Vault
7. ✅ **No Public Blob Access** - Storage account locked down
8. ✅ **Diagnostic Logging** - All logs forwarded to Log Analytics

## Next Steps

1. ✅ Run `azd up` to provision infrastructure
2. Update Key Vault secrets with real values
3. Configure custom domain (optional)
4. Set up CI/CD pipeline (GitHub Actions or Azure DevOps)
5. Configure alerting rules in Application Insights
6. Review and adjust Log Analytics retention (default: 30 days)

## Additional Resources

- [Azure Developer CLI Documentation](https://learn.microsoft.com/azure/developer/azure-developer-cli/)
- [Bicep Documentation](https://learn.microsoft.com/azure/azure-resource-manager/bicep/)
- [App Service Managed Identity](https://learn.microsoft.com/azure/app-service/overview-managed-identity)
- [Key Vault References](https://learn.microsoft.com/azure/app-service/app-service-key-vault-references)
