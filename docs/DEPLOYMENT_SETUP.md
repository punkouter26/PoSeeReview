# Azure App Service Deployment Setup

## Current Status
✅ Code pushed to GitHub: https://github.com/punkouter26/PoSeeReview  
✅ GitHub Actions workflow configured (`.github/workflows/azure-deploy.yml`)  
✅ Setup scripts created (`scripts/setup-azure-appservices.ps1`)  
❌ Azure App Services not created (quota limit)

## Issue: App Service Quota Limit

Your Azure subscription has reached the quota limit for App Service plans. You'll see this error:
```
Operation cannot be completed without additional quota.
Current Limit (Basic VMs): 0
```

## Solution: Request Quota Increase

### Option 1: Azure Portal (Recommended)
1. Go to the [Azure Portal](https://portal.azure.com)
2. Search for "Quotas" in the top search bar
3. Select "App Service" from the list
4. Find "Basic VMs" or "Free VMs" 
5. Click "Request increase"
6. Request at least **1 instance**
7. Wait for approval (usually 24-48 hours)

### Option 2: Use Existing App Service Plan
If you have existing App Services in other resource groups that you're not using:
1. Delete them to free up quota
2. Run the setup script again

## Once Quota is Approved

### 1. Create Azure Resources
```powershell
cd scripts
.\setup-azure-appservices.ps1
```

This will create:
- App Service Plan (Basic B1): `poseereview-plan`
- API App Service: `poseereview-api`
- Client App Service: `poseereview-client`
- Service Principal for GitHub Actions

### 2. Configure GitHub Secrets

The script will output a JSON credential. Copy it and run:
```powershell
gh secret set AZURE_CREDENTIALS --body '<paste-the-json-here>'
```

### 3. Configure App Settings

Update the Client's API URL in `src/Po.SeeReview.Client/wwwroot/appsettings.json`:
```json
{
  "ApiBaseUrl": "https://poseereview-api.azurewebsites.net"
}
```

### 4. Add Azure Secrets to Key Vault

The App Services need these secrets (currently in appsettings.json):
```powershell
# Get connection string
$connectionString = az storage account show-connection-string \
  --name poseereviewstorage \
  --resource-group PoSeeReview \
  --query connectionString -o tsv

# Set app settings
az webapp config appsettings set \
  --name poseereview-api \
  --resource-group PoSeeReview \
  --settings \
    "ConnectionStrings__AzureTableStorage=$connectionString" \
    "ConnectionStrings__AzureBlobStorage=$connectionString" \
    "AzureOpenAI__Endpoint=<your-endpoint>" \
    "AzureOpenAI__ApiKey=<your-key>" \
    "AzureOpenAI__DeploymentName=gpt-4o-mini" \
    "AzureOpenAI__DalleDeploymentName=dall-e-3" \
    "GoogleMaps__ApiKey=<your-key>"
```

### 5. Push to GitHub

Once everything is configured, push any changes:
```powershell
git push
```

The GitHub Actions workflow will automatically:
- Build the .NET projects
- Run tests
- Deploy API to `poseereview-api`
- Deploy Client to `poseereview-client`

## Architecture

```
GitHub (clean-main branch)
    ↓
GitHub Actions CI/CD
    ↓
    ├─→ poseereview-api.azurewebsites.net (ASP.NET Core API)
    └─→ poseereview-client.azurewebsites.net (Blazor WASM)
            ↓
      Azure Storage (Tables + Blobs)
      Azure OpenAI (GPT-4o-mini + DALL-E 3)
      Google Maps API
```

## Costs

### App Service (Basic B1)
- **$13.14/month** (~$0.44/day) per app service
- **Total: ~$26.28/month** for API + Client

### Alternative: Use Azure Static Web Apps (FREE)
For the Blazor WASM client, you could use Azure Static Web Apps (free tier) instead:
```powershell
az staticwebapp create \
  --name poseereview-client \
  --resource-group PoSeeReview \
  --location eastus2 \
  --sku Free
```

This would reduce costs to **~$13.14/month** (just the API).

## Monitoring

View deployment status:
- GitHub Actions: https://github.com/punkouter26/PoSeeReview/actions
- Azure Portal: https://portal.azure.com

## Troubleshooting

### Deployment Fails
- Check GitHub Actions logs
- Verify AZURE_CREDENTIALS secret is set correctly
- Ensure App Service names are unique globally

### App Doesn't Start
- Check Application Insights logs
- Verify all app settings are configured
- Check App Service logs in Azure Portal

### Budget Alerts
Your budget ($2/day) will trigger email alerts at:
- 80%: $1.60/day
- 100%: $2.00/day
- 120%: $2.40/day

Note: App Services add ~$0.88/day to your current AI costs.
