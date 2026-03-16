// Azure App Service module
// Deploys App Service Plan + Web App for the API (Blazor WASM hosted in ASP.NET Core)
// Linux container hosting with .NET 10 on App Service

@description('Location for resources')
param location string

@description('Tags to apply to resources')
param tags object

@description('App Service name (e.g. app-poseereview)')
param appName string

@description('App Service Plan SKU')
param skuName string = 'B1'

@description('Key Vault URI for Managed Identity secret loading')
param keyVaultEndpoint string

@description('Storage Table endpoint')
param storageTableEndpoint string

@description('Storage Blob endpoint')
param storageBlobEndpoint string

@description('Application Insights connection string')
param applicationInsightsConnectionString string

@description('ASPNETCORE_ENVIRONMENT value')
param aspnetcoreEnvironment string = 'Production'

var planName = 'asp-${appName}'

// ─── App Service Plan (Linux) ───────────────────────────────────────────────

resource appServicePlan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: planName
  location: location
  tags: tags
  kind: 'linux'
  sku: {
    name: skuName
  }
  properties: {
    reserved: true // required for Linux plans
  }
}

// ─── Managed Identity ───────────────────────────────────────────────────────

resource managedIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-07-31-preview' = {
  name: 'id-${appName}'
  location: location
  tags: tags
}

// ─── Web App ─────────────────────────────────────────────────────────────────

resource webApp 'Microsoft.Web/sites@2024-04-01' = {
  name: appName
  location: location
  tags: union(tags, {
    'azd-service-name': 'api'
  })
  kind: 'app,linux'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${managedIdentity.id}': {}
    }
  }
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    clientAffinityEnabled: false
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      alwaysOn: false          // allow scale-to-zero on free/B1
      http20Enabled: true
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      healthCheckPath: '/api/health/live'

      // ── App settings ──────────────────────────────────────────────────────
      // NOTE: WEBSITES_CONTAINER_START_TIME_LIMIT is critical.
      // .NET 10 on App Service Linux runs update-ca-certificates (~5 min) followed
      // by Managed Identity cold-start token acquisition (~2 min) = ~10 min total.
      // The default probe timeout (230 s) causes ContainerTimeout crash loops.
      // 900 s (15 min) gives enough headroom for the worst-case cold start.
      appSettings: [
        {
          name: 'ASPNETCORE_ENVIRONMENT'
          value: aspnetcoreEnvironment
        }
        {
          name: 'WEBSITES_CONTAINER_START_TIME_LIMIT'
          value: '900'
        }
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: applicationInsightsConnectionString
        }
        {
          name: 'ApplicationInsightsAgent_EXTENSION_VERSION'
          value: '~3'
        }
        {
          name: 'KeyVault__Endpoint'
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
        {
          name: 'AZURE_CLIENT_ID'
          value: managedIdentity.properties.clientId
        }
      ]
    }
  }
}

// ─── Outputs ─────────────────────────────────────────────────────────────────

@description('Default hostname of the App Service')
output hostName string = webApp.properties.defaultHostName

@description('App Service name')
output appName string = webApp.name

@description('Managed Identity principal ID (for Key Vault / Storage RBAC)')
output identityPrincipalId string = managedIdentity.properties.principalId

@description('Managed Identity client ID')
output identityClientId string = managedIdentity.properties.clientId
