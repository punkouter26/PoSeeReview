// Container Apps Environment module
// Provides the hosting environment for Azure Container Apps

@description('Location for the Container Apps Environment')
param location string

@description('Tags to apply to resources')
param tags object

@description('Resource token for unique naming')
param resourceToken string

@description('Log Analytics Workspace ID for Container Apps Environment')
param logAnalyticsWorkspaceId string

@description('Log Analytics Workspace Customer ID')
param logAnalyticsWorkspaceCustomerId string

@description('Log Analytics Workspace Shared Key')
@secure()
param logAnalyticsWorkspaceSharedKey string

// Resource names
var containerAppsEnvironmentName = 'cae-${resourceToken}'

// Container Apps Environment
resource containerAppsEnvironment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: containerAppsEnvironmentName
  location: location
  tags: tags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalyticsWorkspaceCustomerId
        sharedKey: logAnalyticsWorkspaceSharedKey
      }
    }
    zoneRedundant: false // Set to true for production workloads that need HA
  }
}

// Outputs
output id string = containerAppsEnvironment.id
output name string = containerAppsEnvironment.name
output defaultDomain string = containerAppsEnvironment.properties.defaultDomain
