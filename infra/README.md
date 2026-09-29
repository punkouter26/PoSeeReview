# Azure infrastructure

Bicep for PoSeeReview. **Applied by hand, never by CI** — `.github/workflows/deploy.yml` only
publishes the app package to the existing App Service `app-poseereview` (RG `PoSeeReview`).

| File | What it is |
|---|---|
| `main.bicep` | Subscription-scope template: resource group, monitoring, storage, App Service, budget, and read access to the shared vault |
| `alerts.bicep` | 5xx and AI-spend alerts for the **live** app; deployed on its own (see below) |
| `modules/appservice.bicep` | Linux App Service, .NET 10, system-assigned identity, startup command |
| `modules/storage.bicep` | Storage account (Tables + Blobs). The app reaches it by Managed Identity; the data-plane role grant is not in this template |
| `modules/monitoring.bicep` | Log Analytics + Application Insights |
| `modules/keyvaultaccess.bicep` | `Key Vault Secrets User` role for the app's identity |
| `modules/budget.bicep` | Monthly budget alert |

## Secrets

Secrets live in the shared vault **`kv-poshared`** (RG `PoShared`) under the `PoSeeReview--` prefix.
The templates never create or write a vault; they only grant the app's identity read access.

```powershell
az keyvault secret set --vault-name kv-poshared --name "PoSeeReview--GoogleMaps--ApiKey" --value "<key>"
```

## Provision

```powershell
az deployment sub create -l westus2 -f infra/main.bicep -p environmentName=dev location=westus2
# or: azd provision (azure.yaml + main.parameters.json)
```

## Alerts

```powershell
az deployment group create -g PoSeeReview -f infra/alerts.bicep -p alertEmail=<address>
```

## Validate

```powershell
az bicep build -f infra/main.bicep --stdout > $null
az bicep build -f infra/alerts.bicep --stdout > $null
```
