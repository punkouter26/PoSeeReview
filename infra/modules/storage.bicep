@description('Azure region for resources')
param location string

@description('Resource tags')
param tags object

@description('Unique resource token')
param resourceToken string

// Storage Account
resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: 'st${take(replace(resourceToken, '-', ''), 21)}'
  location: location
  tags: tags
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS'
  }
  properties: {
    accessTier: 'Hot'
    supportsHttpsTrafficOnly: true
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
    networkAcls: {
      defaultAction: 'Allow'
      bypass: 'AzureServices'
    }
  }
}

// Table Service
resource tableService 'Microsoft.Storage/storageAccounts/tableServices@2023-05-01' = {
  parent: storage
  name: 'default'
}

// Blob Service
resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storage
  name: 'default'
  properties: {
    deleteRetentionPolicy: {
      enabled: true
      days: 7
    }
  }
}

output id string = storage.id
output name string = storage.name
output primaryEndpoints object = storage.properties.primaryEndpoints
output tableEndpoint string = storage.properties.primaryEndpoints.table
output blobEndpoint string = storage.properties.primaryEndpoints.blob
#disable-next-line outputs-should-not-contain-secrets
output connectionString string = 'DefaultEndpointsProtocol=https;AccountName=${storage.name};EndpointSuffix=${environment().suffixes.storage};AccountKey=${storage.listKeys().keys[0].value}'
