@description('Key Vault name')
param keyVaultName string

@description('Storage account connection string')
@secure()
param storageConnectionString string

// Reference to existing Key Vault
resource keyVault 'Microsoft.KeyVault/vaults@2024-04-01-preview' existing = {
  name: keyVaultName
}

// =============================================================================
// Azure Storage Secrets
// Secret names use '--' which Azure Key Vault Config Provider maps to ':'
// =============================================================================

// Connection strings for Table and Blob storage
resource azureTableStorageConnectionString 'Microsoft.KeyVault/vaults/secrets@2024-04-01-preview' = {
  parent: keyVault
  name: 'ConnectionStrings--AzureTableStorage'
  properties: {
    value: storageConnectionString
    contentType: 'text/plain'
  }
}

resource azureBlobStorageConnectionString 'Microsoft.KeyVault/vaults/secrets@2024-04-01-preview' = {
  parent: keyVault
  name: 'ConnectionStrings--AzureBlobStorage'
  properties: {
    value: storageConnectionString
    contentType: 'text/plain'
  }
}

// =============================================================================
// Azure OpenAI Secrets
// =============================================================================

resource azureOpenAIEndpoint 'Microsoft.KeyVault/vaults/secrets@2024-04-01-preview' = {
  parent: keyVault
  name: 'AzureOpenAI--Endpoint'
  properties: {
    value: 'https://YOUR-AZURE-OPENAI-ENDPOINT.openai.azure.com/'
    contentType: 'text/plain'
  }
}

resource azureOpenAIApiKey 'Microsoft.KeyVault/vaults/secrets@2024-04-01-preview' = {
  parent: keyVault
  name: 'AzureOpenAI--ApiKey'
  properties: {
    value: 'YOUR_AZURE_OPENAI_KEY_PLACEHOLDER'
    contentType: 'text/plain'
  }
}

resource azureOpenAIDeploymentName 'Microsoft.KeyVault/vaults/secrets@2024-04-01-preview' = {
  parent: keyVault
  name: 'AzureOpenAI--DeploymentName'
  properties: {
    value: 'gpt-4o'
    contentType: 'text/plain'
  }
}

resource azureOpenAIDalleDeploymentName 'Microsoft.KeyVault/vaults/secrets@2024-04-01-preview' = {
  parent: keyVault
  name: 'AzureOpenAI--DalleDeploymentName'
  properties: {
    value: 'dall-e-3'
    contentType: 'text/plain'
  }
}

// Optional: DALL-E specific endpoint (if using separate resource)
resource azureOpenAIDalleEndpoint 'Microsoft.KeyVault/vaults/secrets@2024-04-01-preview' = {
  parent: keyVault
  name: 'AzureOpenAI--DalleEndpoint'
  properties: {
    value: ''
    contentType: 'text/plain'
  }
}

resource azureOpenAIDalleApiKey 'Microsoft.KeyVault/vaults/secrets@2024-04-01-preview' = {
  parent: keyVault
  name: 'AzureOpenAI--DalleApiKey'
  properties: {
    value: ''
    contentType: 'text/plain'
  }
}

// =============================================================================
// Google Maps Secrets
// =============================================================================

resource googleMapsApiKey 'Microsoft.KeyVault/vaults/secrets@2024-04-01-preview' = {
  parent: keyVault
  name: 'GoogleMaps--ApiKey'
  properties: {
    value: 'YOUR_GOOGLE_MAPS_KEY_PLACEHOLDER'
    contentType: 'text/plain'
  }
}

// =============================================================================
// Content Safety Secrets (optional)
// =============================================================================

resource contentSafetyEndpoint 'Microsoft.KeyVault/vaults/secrets@2024-04-01-preview' = {
  parent: keyVault
  name: 'ContentSafety--Endpoint'
  properties: {
    value: ''
    contentType: 'text/plain'
  }
}

resource contentSafetyApiKey 'Microsoft.KeyVault/vaults/secrets@2024-04-01-preview' = {
  parent: keyVault
  name: 'ContentSafety--ApiKey'
  properties: {
    value: ''
    contentType: 'text/plain'
  }
}

// =============================================================================
// Outputs
// =============================================================================

output tableStorageSecretUri string = azureTableStorageConnectionString.properties.secretUri
output blobStorageSecretUri string = azureBlobStorageConnectionString.properties.secretUri
output azureOpenAIEndpointSecretUri string = azureOpenAIEndpoint.properties.secretUri
output azureOpenAIKeySecretUri string = azureOpenAIApiKey.properties.secretUri
output googleMapsKeySecretUri string = googleMapsApiKey.properties.secretUri
