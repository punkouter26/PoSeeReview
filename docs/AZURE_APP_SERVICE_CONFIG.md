# Azure Container Apps Configuration Guide

## Required Configuration Settings

The PoSeeReview API is deployed as a container to Azure Container Apps (ACA) and requires the following environment variables.

### How to Configure

1. Navigate to your Azure Container App in the Azure Portal
2. Go to **Containers** → **Environment variables**
3. Or use Azure CLI to configure during deployment
4. Add the following environment variables:

### Storage Configuration

| Environment Variable | Value | Notes |
|------|-------|-------|
| `ConnectionStrings__AzureTableStorage` | `DefaultEndpointsProtocol=https;AccountName=<account>;AccountKey=<key>;EndpointSuffix=core.windows.net` | Azure Table Storage connection string |
| `ConnectionStrings__AzureBlobStorage` | `DefaultEndpointsProtocol=https;AccountName=<account>;AccountKey=<key>;EndpointSuffix=core.windows.net` | Azure Blob Storage connection string |

**Note:** The double underscore (`__`) is the standard .NET configuration delimiter for nested settings in environment variables.

### Azure OpenAI Settings

| Environment Variable | Value | Notes |
|------|-------|-------|
| `AzureOpenAI__Endpoint` | `https://<your-resource>.openai.azure.com/` | Your Azure OpenAI endpoint |
| `AzureOpenAI__ApiKey` | `<your-api-key>` | Azure OpenAI API key (store in Key Vault) |
| `AzureOpenAI__DeploymentName` | `gpt-4` | GPT model deployment name |
| `AzureOpenAI__DalleDeploymentName` | `dall-e-3` | DALL-E 3 deployment name |

### Google Maps API

| Environment Variable | Value | Notes |
|------|-------|-------|
| `GoogleMaps__ApiKey` | `<your-api-key>` | Google Maps Places API key (store in Key Vault) |

### Application Insights (Optional)

| Environment Variable | Value | Notes |
|------|-------|-------|
| `ApplicationInsights__ConnectionString` | `InstrumentationKey=...` | Application Insights connection string |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | `InstrumentationKey=...` | Alternative format supported by ACA |

### Key Vault Integration (Recommended for Production)

For production, store secrets in Azure Key Vault and use Managed Identity:

1. **Enable Managed Identity** on your Container App:
   ```bash
   az containerapp identity assign \
     --name <your-container-app-name> \
     --resource-group <your-resource-group> \
     --system-assigned
   ```

2. **Grant Key Vault Access**:
   ```bash
   # Get the principal ID from the previous command
   az keyvault set-policy \
     --name poseereview-kv \
     --object-id <managed-identity-principal-id> \
     --secret-permissions get list
   ```

3. **Store Secrets in Key Vault**:
   ```bash
   az keyvault secret set --vault-name poseereview-kv --name ConnectionStrings--AzureTableStorage --value "<connection-string>"
   az keyvault secret set --vault-name poseereview-kv --name ConnectionStrings--AzureBlobStorage --value "<connection-string>"
   az keyvault secret set --vault-name poseereview-kv --name AzureOpenAI--ApiKey --value "<api-key>"
   az keyvault secret set --vault-name poseereview-kv --name GoogleMaps--ApiKey --value "<api-key>"
   ```

The application will automatically retrieve secrets from Key Vault using Managed Identity.

### Azure Developer CLI (azd) Deployment

The recommended way to deploy is using `azd`:

```bash
# Set secrets as environment variables (azd will handle Key Vault)
azd env set AZURE_TABLE_STORAGE_CONNECTION_STRING "<your-connection-string>"
azd env set AZURE_BLOB_STORAGE_CONNECTION_STRING "<your-connection-string>"
azd env set AZURE_OPENAI_API_KEY "<your-api-key>"
azd env set GOOGLE_MAPS_API_KEY "<your-api-key>"

# Deploy to Azure
azd up
```

The `azd` tool will automatically:
- Create the Azure Container Apps environment
- Set up Managed Identity
- Configure environment variables
- Deploy the container

## Troubleshooting

### Error: "AzureTableStorage connection string is required"

**Solution:** Add the environment variable. The application checks in this order:
1. `ConnectionStrings__AzureTableStorage` environment variable
2. `AzureTableStorage` configuration value
3. `AZURE_TABLE_STORAGE_CONNECTION_STRING` environment variable

Set it in your Container App environment variables or via `azd`:
```bash
azd env set AZURE_TABLE_STORAGE_CONNECTION_STRING "<connection-string>"
azd deploy
```

### Error: "DefaultAzureCredential failed to retrieve a token"

**Solution:** Managed Identity is not enabled or doesn't have Key Vault access:

1. **Enable Managed Identity:**
   ```bash
   az containerapp identity assign \
     --name <app-name> \
     --resource-group <rg-name> \
     --system-assigned
   ```

2. **Grant Key Vault permissions:**
   ```bash
   az keyvault set-policy \
     --name poseereview-kv \
     --object-id <principal-id> \
     --secret-permissions get list
   ```

3. **Or skip Key Vault:** Set secrets as environment variables directly

### Application Won't Start / Container Keeps Restarting

Check Container App logs:
```bash
# View logs
az containerapp logs show \
  --name <app-name> \
  --resource-group <rg-name> \
  --follow

# View revision details
az containerapp revision list \
  --name <app-name> \
  --resource-group <rg-name> \
  --output table
```

Or in Azure Portal:
1. Go to Container App → **Log stream**
2. Check **Console logs** for startup errors
3. Verify environment variables under **Containers** → **Environment variables**

## Deployment Checklist

Before deploying to Azure Container Apps:

- [ ] Azure Table Storage connection string set in environment
- [ ] Azure Blob Storage connection string set in environment
- [ ] Azure OpenAI endpoint and API key configured
- [ ] Google Maps API key configured
- [ ] Container registry configured (ACR or Docker Hub)
- [ ] (Recommended) Managed Identity enabled
- [ ] (Recommended) Key Vault created and secrets stored
- [ ] (Recommended) Key Vault access granted to Managed Identity
- [ ] (Optional) Application Insights configured
- [ ] (Optional) Custom domain and SSL configured

## Local Development

For local development using Aspire, configuration is automatically handled by the AppHost. No manual configuration needed.

For running the API standalone locally:
1. Use `dotnet user-secrets` to store sensitive values
2. Or set environment variables
3. Or update `appsettings.Development.json` (not recommended for secrets)

Example using user-secrets:
```bash
cd src/Po.SeeReview.Api
dotnet user-secrets set "ConnectionStrings:AzureTableStorage" "UseDevelopmentStorage=true"
dotnet user-secrets set "ConnectionStrings:AzureBlobStorage" "UseDevelopmentStorage=true"
dotnet user-secrets set "AzureOpenAI:ApiKey" "your-key-here"
dotnet user-secrets set "GoogleMaps:ApiKey" "your-key-here"
```

## Environment Variables Reference

Complete list of environment variables for Azure Container Apps:

```bash
# Storage (Required)
ConnectionStrings__AzureTableStorage="DefaultEndpointsProtocol=https;AccountName=...;AccountKey=...;EndpointSuffix=core.windows.net"
ConnectionStrings__AzureBlobStorage="DefaultEndpointsProtocol=https;AccountName=...;AccountKey=...;EndpointSuffix=core.windows.net"

# Azure OpenAI (Required)
AzureOpenAI__Endpoint="https://your-resource.openai.azure.com/"
AzureOpenAI__ApiKey="your-api-key"
AzureOpenAI__DeploymentName="gpt-4"
AzureOpenAI__DalleDeploymentName="dall-e-3"

# Google Maps (Required)
GoogleMaps__ApiKey="your-api-key"

# Application Insights (Optional)
ApplicationInsights__ConnectionString="InstrumentationKey=...;IngestionEndpoint=..."

# ASP.NET Core (Auto-configured by ACA)
ASPNETCORE_ENVIRONMENT="Production"
ASPNETCORE_URLS="http://+:8080"
```
