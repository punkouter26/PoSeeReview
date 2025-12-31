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

// Container Apps Environment
module containerAppsEnvironment './modules/containerappenv.bicep' = {
  name: 'containerAppsEnvironment'
  scope: rg
  params: {
    location: location
    tags: tags
    resourceToken: resourceToken
    logAnalyticsWorkspaceId: monitoring.outputs.logAnalyticsWorkspaceId
    logAnalyticsWorkspaceCustomerId: monitoring.outputs.logAnalyticsCustomerId
    logAnalyticsWorkspaceSharedKey: monitoring.outputs.logAnalyticsSharedKey
  }
}

// API Container App
module api './modules/containerapp.bicep' = {
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
    storageAccountName: storage.outputs.name
    storageTableEndpoint: storage.outputs.tableEndpoint
    storageBlobEndpoint: storage.outputs.blobEndpoint
    appSettings: {
      'ASPNETCORE_ENVIRONMENT': environmentName == 'prod' ? 'Production' : 'Development'
    }
  }
}

// Key Vault - Secrets Management (needs API identity for access)
module keyVault './modules/keyvault.bicep' = {
  name: 'keyvault'
  scope: rg
  params: {
    location: location
    tags: tags
    resourceToken: resourceToken
    principalId: api.outputs.identityPrincipalId
  }
}

// Store secrets in Key Vault (placeholders - update via CLI or Portal)
module secrets './modules/secrets.bicep' = {
  name: 'secrets'
  scope: rg
  params: {
    keyVaultName: keyVault.outputs.name
    storageConnectionString: storage.outputs.connectionString
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

output API_SERVICE_NAME string = api.outputs.name
output API_URL string = 'https://${api.outputs.fqdn}'

output AZURE_CONTAINER_REGISTRY_NAME string = api.outputs.acrName
output AZURE_CONTAINER_REGISTRY_ENDPOINT string = api.outputs.acrLoginServer
output AZURE_CONTAINER_APPS_ENVIRONMENT_NAME string = containerAppsEnvironment.outputs.name

output APPLICATION_INSIGHTS_CONNECTION_STRING string = monitoring.outputs.applicationInsightsConnectionString
output APPLICATION_INSIGHTS_INSTRUMENTATION_KEY string = monitoring.outputs.applicationInsightsInstrumentationKey

output STORAGE_ACCOUNT_NAME string = storage.outputs.name
output KEY_VAULT_NAME string = keyVault.outputs.name
output KEY_VAULT_ENDPOINT string = keyVault.outputs.endpoint
