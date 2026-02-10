// Azure Container App module
// Deploys a Container App for the API service

@description('Location for the Container App')
param location string

@description('Tags to apply to resources')
param tags object

@description('Service name for the container app')
param serviceName string

@description('Resource token for unique naming')
param resourceToken string

@description('Container Apps Environment ID')
param containerAppsEnvironmentId string

@description('Container image to deploy')
param containerImage string = 'mcr.microsoft.com/azuredocs/containerapps-helloworld:latest'

@description('Application Insights connection string')
param applicationInsightsConnectionString string

@description('Key Vault endpoint URL')
param keyVaultEndpoint string

@description('Storage account name')
param storageAccountName string

@description('Storage Table endpoint')
param storageTableEndpoint string

@description('Storage Blob endpoint')
param storageBlobEndpoint string

@description('Additional app settings')
param appSettings object = {}

@description('Target port for the container')
param targetPort int = 8080

@description('Enable external ingress')
param externalIngress bool = true

@description('CPU allocation (cores)')
param cpu string = '0.5'

@description('Memory allocation (Gi)')
param memory string = '1Gi'

@description('Minimum replicas')
param minReplicas int = 0

@description('Maximum replicas')
param maxReplicas int = 3

// Resource names
var containerAppName = 'ca-${serviceName}-${resourceToken}'
var registryName = 'acr${replace(resourceToken, '-', '')}'

// Azure Container Registry
resource acr 'Microsoft.ContainerRegistry/registries@2023-11-01-preview' = {
  name: registryName
  location: location
  tags: tags
  sku: {
    name: 'Basic'
  }
  properties: {
    adminUserEnabled: true
  }
}

// User-assigned managed identity for the container app
resource managedIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-07-31-preview' = {
  name: 'id-${containerAppName}'
  location: location
  tags: tags
}

// Role assignment for ACR pull
resource acrPullRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(acr.id, managedIdentity.id, 'acrpull')
  scope: acr
  properties: {
    principalId: managedIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d') // AcrPull role
  }
}

// Container App
resource containerApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: containerAppName
  location: location
  tags: union(tags, {
    'azd-service-name': serviceName
  })
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${managedIdentity.id}': {}
    }
  }
  properties: {
    managedEnvironmentId: containerAppsEnvironmentId
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: externalIngress ? {
        external: true
        targetPort: targetPort
        transport: 'auto'
        allowInsecure: false
      } : null
      registries: [
        {
          server: acr.properties.loginServer
          identity: managedIdentity.id
        }
      ]
      secrets: []
    }
    template: {
      containers: [
        {
          name: serviceName
          image: containerImage
          resources: {
            cpu: json(cpu)
            memory: memory
          }
          env: concat([
            {
              name: 'ASPNETCORE_ENVIRONMENT'
              value: 'Production'
            }
            {
              name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
              value: applicationInsightsConnectionString
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
          ], map(items(appSettings), item => {
            name: item.key
            value: item.value
          }))
          probes: [
            {
              type: 'Liveness'
              httpGet: {
                path: '/api/health/live'
                port: targetPort
                scheme: 'HTTP'
              }
              initialDelaySeconds: 10
              periodSeconds: 30
              failureThreshold: 3
            }
            {
              type: 'Readiness'
              httpGet: {
                path: '/api/health/ready'
                port: targetPort
                scheme: 'HTTP'
              }
              initialDelaySeconds: 5
              periodSeconds: 10
              failureThreshold: 3
            }
          ]
        }
      ]
      scale: {
        minReplicas: minReplicas
        maxReplicas: maxReplicas
        rules: [
          {
            name: 'http-rule'
            http: {
              metadata: {
                concurrentRequests: '100'
              }
            }
          }
        ]
      }
    }
  }
}

// Outputs
output id string = containerApp.id
output name string = containerApp.name
output fqdn string = containerApp.properties.configuration.ingress != null ? containerApp.properties.configuration.ingress.fqdn : ''
output defaultHostName string = containerApp.properties.configuration.ingress != null ? containerApp.properties.configuration.ingress.fqdn : ''
output identityPrincipalId string = managedIdentity.properties.principalId
output identityClientId string = managedIdentity.properties.clientId
output acrLoginServer string = acr.properties.loginServer
output acrName string = acr.name
