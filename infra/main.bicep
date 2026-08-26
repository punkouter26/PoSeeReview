targetScope = 'subscription'

@minLength(1)
@maxLength(64)
@description('Name of the environment which will be used to generate the resource names')
param environmentName string

@minLength(1)
@description('Primary location for all resources')
param location string

@description('Azure Developer CLI environment name override')
param azdEnvironmentName string = environmentName

@description('Region code used in deterministic resource names')
param regionCode string = 'wus2'

@description('Email addresses for budget alerts')
param budgetContactEmails array = []

// Service names
param apiServiceName string = 'api'

// App Service Plan SKU
@description('App Service Plan SKU (B1 Basic recommended for production)')
param appServicePlanSku string = 'B1'

// Resource names
var resourceToken = toLower('poseereview-${environmentName}-${regionCode}')
var resourceGroupName = 'rg-${resourceToken}'
var tags = {
  'azd-env-name': azdEnvironmentName
  environment: environmentName
  application: 'SeeReview'
  app: 'poseereview'
  region: regionCode
  namingStandard: 'caf-derived'
}

// Resource group
resource rg 'Microsoft.Resources/resourceGroups@2024-11-01' = {
  name: resourceGroupName
  location: location
  tags: tags
}

// Monitoring - Log Analytics & Application Insights
module monitoring './modules/monitoring.bicep' = {
  name: 'monitoring'
  scope: rg
  params: {
    location: location
    tags: tags
    resourceToken: resourceToken
  }
}

// Storage - Azure Storage Account (Tables + Blobs)
module storage './modules/storage.bicep' = {
  name: 'storage'
  scope: rg
  params: {
    location: location
    tags: tags
    resourceToken: resourceToken
  }
}

// Key Vault - Secrets Management (created first without access policies)
module keyVault './modules/keyvault.bicep' = {
  name: 'keyvault'
  scope: rg
  params: {
    location: location
    tags: tags
    resourceToken: resourceToken
    principalId: '' // Will be updated after API is created
  }
}

// App Service — the only hosting target. Matches .github/workflows/deploy.yml.
module apiAppService './modules/appservice.bicep' = {
  name: 'api-appservice'
  scope: rg
  params: {
    location: location
    tags: tags
    appName: 'app-${resourceToken}'
    skuName: appServicePlanSku
    keyVaultEndpoint: keyVault.outputs.endpoint
    storageTableEndpoint: storage.outputs.tableEndpoint
    storageBlobEndpoint: storage.outputs.blobEndpoint
    applicationInsightsConnectionString: monitoring.outputs.applicationInsightsConnectionString
    aspnetcoreEnvironment: environmentName == 'prod' ? 'Production' : 'Development'
  }
}

// Key Vault Access - Grant API managed identity access to Key Vault
module keyVaultAccess './modules/keyvaultaccess.bicep' = {
  name: 'keyvaultaccess'
  scope: rg
  params: {
    keyVaultName: keyVault.outputs.name
    principalId: apiAppService.outputs.identityPrincipalId
  }
}

// Optional app-prefixed Google Maps key. Set via `azd env set poSeeReviewGoogleMapsApiKey <key>`
// then `azd provision` — the secret lands in kv-poshared under PoSeeReview--GoogleMaps--ApiKey.
// Empty by default so existing dev/test envs are not forced to provide it.
@secure()
param poSeeReviewGoogleMapsApiKey string = ''

// Store secrets in Key Vault (placeholders - update via CLI or Portal)
module secrets './modules/secrets.bicep' = {
  name: 'secrets'
  scope: rg
  params: {
    keyVaultName: keyVault.outputs.name
    poSeeReviewGoogleMapsApiKey: poSeeReviewGoogleMapsApiKey
  }
}

// Budget - Monthly spending alerts
module budget './modules/budget.bicep' = {
  name: 'budget'
  scope: rg
  params: {
    contactEmails: budgetContactEmails
  }
}

// Outputs
output AZURE_LOCATION string = location
output AZURE_TENANT_ID string = tenant().tenantId
output AZURE_RESOURCE_GROUP string = rg.name

output API_SERVICE_NAME string = apiAppService.outputs.appName
output API_URL string = 'https://${apiAppService.outputs.hostName}'

output APPLICATION_INSIGHTS_CONNECTION_STRING string = monitoring.outputs.applicationInsightsConnectionString
output APPLICATION_INSIGHTS_INSTRUMENTATION_KEY string = monitoring.outputs.applicationInsightsInstrumentationKey

output STORAGE_ACCOUNT_NAME string = storage.outputs.name
output KEY_VAULT_NAME string = keyVault.outputs.name
output KEY_VAULT_ENDPOINT string = keyVault.outputs.endpoint
