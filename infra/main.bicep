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

@description('Unique ID to ensure resource name uniqueness')
param resourceGroupSuffix string = uniqueString(subscription().id, environmentName)

@description('Email addresses for budget alerts')
param budgetContactEmails array = []

// Service names
param apiServiceName string = 'api'

// Hosting mode — 'containerapp' (default, current azd path) or 'appservice'
// (matches the GitHub Actions deploy target in .github/workflows/deploy.yml).
// Production currently runs on App Service but azd provision would still
// create a Container App. Set this to 'appservice' to provision the
// App Service target via the existing appservice.bicep module.
@allowed([
  'containerapp'
  'appservice'
])
param hostingMode string = 'appservice'

// App Service Plan SKU when hostingMode == 'appservice'
@description('App Service Plan SKU (B1 Basic recommended for production)')
param appServicePlanSku string = 'B1'

// Resource names
var resourceToken = toLower('${environmentName}-${resourceGroupSuffix}')
var resourceGroupName = 'rg-${resourceToken}'
var tags = {
  'azd-env-name': azdEnvironmentName
  environment: environmentName
  application: 'SeeReview'
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

// Container Apps Environment — only when hostingMode == 'containerapp'
module containerAppsEnvironment './modules/containerappenv.bicep' = if (hostingMode == 'containerapp') {
  name: 'containerAppsEnvironment'
  scope: rg
  params: {
    location: location
    tags: tags
    resourceToken: resourceToken
    logAnalyticsWorkspaceCustomerId: monitoring.outputs.logAnalyticsCustomerId
    logAnalyticsWorkspaceSharedKey: monitoring.outputs.logAnalyticsSharedKey
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

// API hosting — one or the other based on hostingMode
module apiContainerApp './modules/containerapp.bicep' = if (hostingMode == 'containerapp') {
  name: 'api-containerapp'
  scope: rg
  params: {
    location: location
    tags: tags
    serviceName: apiServiceName
    resourceToken: resourceToken
    containerAppsEnvironmentId: containerAppsEnvironment.outputs.id
    applicationInsightsConnectionString: monitoring.outputs.applicationInsightsConnectionString
    keyVaultEndpoint: keyVault.outputs.endpoint
    storageTableEndpoint: storage.outputs.tableEndpoint
    storageBlobEndpoint: storage.outputs.blobEndpoint
    appSettings: {
      ASPNETCORE_ENVIRONMENT: environmentName == 'prod' ? 'Production' : 'Development'
    }
  }
}

// App Service target — matches the GitHub Actions deploy.yml target.
module apiAppService './modules/appservice.bicep' = if (hostingMode == 'appservice') {
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
    principalId: hostingMode == 'appservice' ? apiAppService.outputs.identityPrincipalId : apiContainerApp.outputs.identityPrincipalId
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
    storageConnectionString: storage.outputs.connectionString
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
output HOSTING_MODE string = hostingMode

output API_SERVICE_NAME string = hostingMode == 'appservice' ? apiAppService.outputs.appName : apiContainerApp.outputs.name
output API_URL string = hostingMode == 'appservice' ? 'https://${apiAppService.outputs.hostName}' : 'https://${apiContainerApp.outputs.fqdn}'

output AZURE_CONTAINER_REGISTRY_NAME string = hostingMode == 'containerapp' ? apiContainerApp.outputs.acrName : ''
output AZURE_CONTAINER_REGISTRY_ENDPOINT string = hostingMode == 'containerapp' ? apiContainerApp.outputs.acrLoginServer : ''
output AZURE_CONTAINER_APPS_ENVIRONMENT_NAME string = hostingMode == 'containerapp' ? containerAppsEnvironment.outputs.name : ''

output APPLICATION_INSIGHTS_CONNECTION_STRING string = monitoring.outputs.applicationInsightsConnectionString
output APPLICATION_INSIGHTS_INSTRUMENTATION_KEY string = monitoring.outputs.applicationInsightsInstrumentationKey

output STORAGE_ACCOUNT_NAME string = storage.outputs.name
output KEY_VAULT_NAME string = keyVault.outputs.name
output KEY_VAULT_ENDPOINT string = keyVault.outputs.endpoint
