@description('Azure region for resources')
param location string

@description('Resource tags')
param tags object

@description('Unique resource token')
param resourceToken string

@description('Principal ID for access policy')
param principalId string

// Key Vault
resource keyVault 'Microsoft.KeyVault/vaults@2024-04-01-preview' = {
  name: 'kv-${resourceToken}'
  location: location
  tags: tags
  properties: {
    sku: {
      family: 'A'
      name: 'standard'
    }
    tenantId: subscription().tenantId
    enabledForDeployment: false
    enabledForDiskEncryption: false
    enabledForTemplateDeployment: false
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
    networkAcls: {
      defaultAction: 'Allow'
      bypass: 'AzureServices'
    }
  }
}

// Role assignment - Key Vault Secrets User for API Managed Identity
resource secretsUserRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, principalId, 'Key Vault Secrets User')
  scope: keyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}

output id string = keyVault.id
output name string = keyVault.name
output endpoint string = keyVault.properties.vaultUri
