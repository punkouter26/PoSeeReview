# Azure Key Vault Setup Guide

## Overview

PoSeeReview uses **Azure Key Vault** for all secrets management. This works seamlessly in both local development and production environments.

## Key Vault Details

- **Name**: `poseereview-kv`
- **URL**: `https://poseereview-kv.vault.azure.net/`
- **Resource Group**: `PoSeeReview`
- **Location**: `eastus`

## Secrets Stored

All secrets use `--` instead of `:` in their names (Key Vault limitation):

| Secret Name | Configuration Key | Description |
|------------|------------------|-------------|
| `GoogleMaps--ApiKey` | `GoogleMaps:ApiKey` | Google Maps API key |
| `AzureOpenAI--Endpoint` | `AzureOpenAI:Endpoint` | Primary Azure AI Foundry endpoint for GPT models |
| `AzureOpenAI--DeploymentName` | `AzureOpenAI:DeploymentName` | GPT model deployment name (e.g., `gpt-4o`) |
| `AzureOpenAI--ApiKey` | `AzureOpenAI:ApiKey` | Primary Azure AI Foundry API key |
| `AzureOpenAI--DalleEndpoint` | `AzureOpenAI:DalleEndpoint` | Fallback endpoint for DALL-E (optional) |
| `AzureOpenAI--DalleApiKey` | `AzureOpenAI:DalleApiKey` | Fallback API key for DALL-E (optional) |
| `AzureOpenAI--DalleDeploymentName` | `AzureOpenAI:DalleDeploymentName` | DALL-E deployment name (e.g., `dall-e-3`) |
| `ConnectionStrings--AzureTableStorage` | `ConnectionStrings:AzureTableStorage` | Azure Table Storage connection string |
| `ConnectionStrings--AzureBlobStorage` | `ConnectionStrings:AzureBlobStorage` | Azure Blob Storage connection string |
| `AzureStorage--ConnectionString` | `AzureStorage:ConnectionString` | Local development storage |

## Azure AI Service Architecture

The application uses a **dual-resource strategy** for AI capabilities:

| Service | Resource | Location | Models | Purpose |
|---------|----------|----------|--------|---------|
| **Text Generation** | `poshared-openai` (Foundry) | East US 2 | `gpt-4o` | Primary - review analysis |
| **Image Generation** | `poseereview-openai` (OpenAI) | East US | `dall-e-3` | Fallback - comic generation |

### Why Two Resources?

- **Azure AI Foundry** (`poshared-openai`) provides the latest AI Services platform but may have limited image model availability
- **Classic Azure OpenAI** (`poseereview-openai`) has DALL-E 3 deployed and available
- The app automatically uses the DALL-E fallback endpoint when `DalleEndpoint` is configured

### Endpoint Configuration

```
Primary (Foundry):  https://poshared-openai.cognitiveservices.azure.com/
DALL-E Fallback:    https://eastus.api.cognitive.microsoft.com/
```

## Setup on New Computer

### Prerequisites
- Azure CLI installed
- .NET 9.0 SDK installed
- Git

### Steps

```powershell
# 1. Clone the repository
git clone https://github.com/punkouter26/PoSeeReview.git
cd PoSeeReview

# 2. Login to Azure (this authenticates you for Key Vault access)
az login

# 3. Verify you're using the correct subscription
az account show

# 4. Run the application - that's it!
dotnet run --project src/Po.SeeReview.Api
```

**That's all!** The application automatically:
- Detects your Azure credentials via `DefaultAzureCredential`
- Connects to Key Vault
- Loads all secrets
- Maps them to configuration keys (replacing `--` with `:`)

## How It Works

### Local Development
When you run locally, `DefaultAzureCredential` tries authentication methods in this order:
1. **Environment variables** (if set)
2. **Managed Identity** (if running in Azure)
3. **Visual Studio credentials** (if logged into VS)
4. **Azure CLI credentials** ← **This is what works for you** (`az login`)
5. **Azure PowerShell credentials**
6. **Interactive browser** (as fallback)

### Production (Azure App Service)
When deployed to Azure App Service, the app uses **Managed Identity**:
- No credentials needed in code
- App Service automatically authenticates to Key Vault
- Just grant the App Service's Managed Identity the "Key Vault Secrets User" role

## Managing Secrets

### View All Secrets
```powershell
az keyvault secret list --vault-name poseereview-kv -o table
```

### Get a Secret Value
```powershell
az keyvault secret show --vault-name poseereview-kv --name GoogleMaps--ApiKey --query value -o tsv
```

### Update a Secret
```powershell
az keyvault secret set --vault-name poseereview-kv --name GoogleMaps--ApiKey --value "NEW_VALUE"
```

### Add a New Secret
```powershell
az keyvault secret set --vault-name poseereview-kv --name NewSecret--Name --value "secret-value"
```

## Permissions

You have been granted the **Key Vault Secrets Officer** role, which allows you to:
- ✅ Read secrets
- ✅ Create secrets
- ✅ Update secrets
- ✅ Delete secrets
- ✅ List secrets

To grant access to other developers:
```powershell
# Get their Azure AD user object ID
az ad user show --id their-email@domain.com --query id -o tsv

# Grant them access
az role assignment create \
  --role "Key Vault Secrets User" \
  --assignee <their-object-id> \
  --scope /subscriptions/bbb8dfbe-9169-432f-9b7a-fbf861b51037/resourceGroups/PoSeeReview/providers/Microsoft.KeyVault/vaults/poseereview-kv
```

## Troubleshooting

### "DefaultAzureCredential failed to retrieve a token"
- Run `az login` to authenticate
- Verify you're in the correct subscription: `az account show`
- Check your role assignment: `az role assignment list --scope /subscriptions/bbb8dfbe-9169-432f-9b7a-fbf861b51037/resourceGroups/PoSeeReview/providers/Microsoft.KeyVault/vaults/poseereview-kv`

### Secrets not loading
- Check the Key Vault URL is correct in `Program.cs`
- Verify secrets exist: `az keyvault secret list --vault-name poseereview-kv`
- Check application logs for detailed error messages

### Running tests
Tests run with `DISABLE_SERILOG=true` which also skips Key Vault configuration. They use mock services instead.

## Migration from User Secrets

**Old way** (User Secrets - deprecated):
```powershell
dotnet user-secrets set "GoogleMaps:ApiKey" "value"
```

**New way** (Key Vault):
```powershell
az keyvault secret set --vault-name poseereview-kv --name GoogleMaps--ApiKey --value "value"
```

You can delete your local user secrets file if you want:
```powershell
Remove-Item "$env:APPDATA\Microsoft\UserSecrets\seereview-api-secrets\secrets.json"
```

## Benefits of Key Vault vs User Secrets

| Feature | User Secrets | Azure Key Vault |
|---------|-------------|-----------------|
| Works locally | ✅ | ✅ |
| Works in Azure | ❌ | ✅ |
| Syncs across computers | ❌ | ✅ |
| Centralized management | ❌ | ✅ |
| Audit logging | ❌ | ✅ |
| Access control | ❌ | ✅ |
| Automatic rotation | ❌ | ✅ |
| No setup on new machines | ❌ | ✅ (just `az login`) |

## Security Best Practices

1. **Never commit secrets to git** - Key Vault eliminates this risk
2. **Rotate secrets regularly** - Update them in Key Vault, all environments get new values
3. **Use Managed Identity in Azure** - No credentials in code
4. **Grant minimal permissions** - Use "Key Vault Secrets User" for read-only access
5. **Enable soft-delete** - Already enabled (90-day retention)
6. **Monitor access** - Review Key Vault access logs in Azure Monitor

## Next Steps

1. ✅ Key Vault created and configured
2. ✅ All secrets migrated from user secrets
3. ✅ Application configured to use Key Vault
4. ✅ Permissions granted to your account
5. ⏳ Test the application locally
6. ⏳ Deploy to Azure App Service with Managed Identity
7. ⏳ Delete user secrets file (optional)

