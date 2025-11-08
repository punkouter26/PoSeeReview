@description('Azure region for resources')
param location string

@description('Resource tags')
param tags object

@description('Service name (e.g., api)')
param serviceName string

@description('Unique resource token')
param resourceToken string

@description('App Service Plan resource ID')
param appServicePlanId string

@description('Application Insights connection string')
param applicationInsightsConnectionString string

@description('Key Vault endpoint')
param keyVaultEndpoint string

@description('Storage Table endpoint')
param storageTableEndpoint string

@description('Storage Blob endpoint')
param storageBlobEndpoint string

@description('Additional app settings')
param appSettings object = {}

// Convert app settings object to array
var additionalAppSettings = [for key in items(appSettings): {
  name: key.key
  value: key.value
}]

// App Service with Managed Identity
resource appService 'Microsoft.Web/sites@2024-04-01' = {
  name: 'app-${serviceName}-${resourceToken}'
  location: location
  tags: union(tags, { 'azd-service-name': serviceName })
  kind: 'app,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: appServicePlanId
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|9.0'
      alwaysOn: false
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      http20Enabled: true
      healthCheckPath: '/api/health'
      appSettings: union([
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: applicationInsightsConnectionString
        }
        {
          name: 'ApplicationInsightsAgent_EXTENSION_VERSION'
          value: '~3'
        }
        {
          name: 'XDT_MicrosoftApplicationInsights_Mode'
          value: 'Recommended'
        }
        {
          name: 'AZURE_KEY_VAULT_ENDPOINT'
          value: keyVaultEndpoint
        }
        {
          name: 'AzureStorage__TableEndpoint'
          value: storageTableEndpoint
        }
        {
          name: 'AzureStorage__BlobEndpoint'
          value: storageBlobEndpoint
        }
      ], additionalAppSettings)
    }
  }
}

// Diagnostic settings
resource diagnosticSettings 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'logs-metrics'
  scope: appService
  properties: {
    logs: [
      {
        category: 'AppServiceHTTPLogs'
        enabled: true
      }
      {
        category: 'AppServiceConsoleLogs'
        enabled: true
      }
      {
        category: 'AppServiceAppLogs'
        enabled: true
      }
    ]
    metrics: [
      {
        category: 'AllMetrics'
        enabled: true
      }
    ]
    workspaceId: split(applicationInsightsConnectionString, ';')[0]
  }
}

output id string = appService.id
output name string = appService.name
output defaultHostName string = appService.properties.defaultHostName
output identityPrincipalId string = appService.identity.principalId
