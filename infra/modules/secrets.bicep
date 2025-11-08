@description('Key Vault name')
param keyVaultName string

@description('Storage account connection string')
@secure()
param storageConnectionString string

// Reference to existing Key Vault
resource keyVault 'Microsoft.KeyVault/vaults@2024-04-01-preview' existing = {
  name: keyVaultName
}

// Storage connection string secret
resource storageConnectionStringSecret 'Microsoft.KeyVault/vaults/secrets@2024-04-01-preview' = {
  parent: keyVault
  name: 'storage-connection-string'
  properties: {
    value: storageConnectionString
    contentType: 'text/plain'
  }
}

// Placeholder secrets (update manually or via CI/CD)
resource azureOpenAIEndpointSecret 'Microsoft.KeyVault/vaults/secrets@2024-04-01-preview' = {
  parent: keyVault
  name: 'azure-openai-endpoint'
  properties: {
    value: 'https://YOUR-AZURE-OPENAI-ENDPOINT.openai.azure.com/'
    contentType: 'text/plain'
  }
}

resource azureOpenAIKeySecret 'Microsoft.KeyVault/vaults/secrets@2024-04-01-preview' = {
  parent: keyVault
  name: 'azure-openai-key'
  properties: {
    value: 'YOUR_AZURE_OPENAI_KEY_PLACEHOLDER'
    contentType: 'text/plain'
  }
}

resource googleMapsKeySecret 'Microsoft.KeyVault/vaults/secrets@2024-04-01-preview' = {
  parent: keyVault
  name: 'google-maps-key'
  properties: {
    value: 'YOUR_GOOGLE_MAPS_KEY_PLACEHOLDER'
    contentType: 'text/plain'
  }
}

output storageConnectionStringSecretUri string = storageConnectionStringSecret.properties.secretUri
output azureOpenAIEndpointSecretUri string = azureOpenAIEndpointSecret.properties.secretUri
output azureOpenAIKeySecretUri string = azureOpenAIKeySecret.properties.secretUri
output googleMapsKeySecretUri string = googleMapsKeySecret.properties.secretUri
