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

// Service names
param apiServiceName string = 'api'
param clientServiceName string = 'client'

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

// Key Vault - Secrets Management
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

// App Service Plan
module appServicePlan './modules/appserviceplan.bicep' = {
  name: 'appserviceplan'
  scope: rg
  params: {
    location: location
    tags: tags
    resourceToken: resourceToken
    environmentName: environmentName
  }
}

// API App Service
module api './modules/appservice.bicep' = {
  name: 'api-appservice'
  scope: rg
  params: {
    location: location
    tags: tags
    serviceName: apiServiceName
    resourceToken: resourceToken
    appServicePlanId: appServicePlan.outputs.id
    applicationInsightsConnectionString: monitoring.outputs.applicationInsightsConnectionString
    keyVaultEndpoint: keyVault.outputs.endpoint
    storageAccountName: storage.outputs.name
    storageTableEndpoint: storage.outputs.tableEndpoint
    storageBlobEndpoint: storage.outputs.blobEndpoint
    appSettings: {
      'ASPNETCORE_ENVIRONMENT': environmentName == 'prod' ? 'Production' : 'Development'
      'AzureStorage:ConnectionString': '@Microsoft.KeyVault(SecretUri=${keyVault.outputs.endpoint}secrets/storage-connection-string/)'
      'AzureOpenAI:Endpoint': '@Microsoft.KeyVault(SecretUri=${keyVault.outputs.endpoint}secrets/azure-openai-endpoint/)'
      'AzureOpenAI:ApiKey': '@Microsoft.KeyVault(SecretUri=${keyVault.outputs.endpoint}secrets/azure-openai-key/)'
      'GoogleMaps:ApiKey': '@Microsoft.KeyVault(SecretUri=${keyVault.outputs.endpoint}secrets/google-maps-key/)'
    }
  }
}

// Client (Static Web App or App Service for Blazor WASM)
module client './modules/staticwebapp.bicep' = {
  name: 'client-staticwebapp'
  scope: rg
  params: {
    location: location
    tags: tags
    serviceName: clientServiceName
    resourceToken: resourceToken
    apiBaseUrl: 'https://${api.outputs.defaultHostName}'
    applicationInsightsConnectionString: monitoring.outputs.applicationInsightsConnectionString
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

// Outputs
output AZURE_LOCATION string = location
output AZURE_TENANT_ID string = tenant().tenantId
output AZURE_RESOURCE_GROUP string = rg.name

output API_SERVICE_NAME string = api.outputs.name
output API_URL string = 'https://${api.outputs.defaultHostName}'

output CLIENT_SERVICE_NAME string = client.outputs.name
output CLIENT_URL string = client.outputs.defaultHostName

output APPLICATION_INSIGHTS_CONNECTION_STRING string = monitoring.outputs.applicationInsightsConnectionString
output APPLICATION_INSIGHTS_INSTRUMENTATION_KEY string = monitoring.outputs.applicationInsightsInstrumentationKey

output STORAGE_ACCOUNT_NAME string = storage.outputs.name
output KEY_VAULT_NAME string = keyVault.outputs.name
output KEY_VAULT_ENDPOINT string = keyVault.outputs.endpoint
