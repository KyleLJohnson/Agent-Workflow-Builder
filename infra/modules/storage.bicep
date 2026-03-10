@description('Azure region for the Storage Account.')
param location string

@description('Base name prefix for resources (alphanumeric only, max 17 chars for storage name limit).')
param namePrefix string

@description('Storage Account SKU.')
param skuName string = 'Standard_LRS'

@description('Tags to apply to all resources.')
param tags object = {}

// Storage account names: 3-24 chars, lowercase alphanumeric only
var storageAccountName string = take(replace(toLower('${namePrefix}stor'), '-', ''), 24)

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageAccountName
  location: location
  tags: tags
  kind: 'StorageV2'
  sku: {
    name: skuName
  }
  properties: {
    supportsHttpsTrafficOnly: true
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
  }
}

resource blobServices 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' existing = {
  parent: storageAccount
  name: 'default'
}

resource plansContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobServices
  name: 'plans'
  properties: {
    publicAccess: 'None'
  }
}

@description('Storage Account connection string.')
#disable-next-line outputs-should-not-contain-secrets // Consumed by app deployment
output connectionString string = 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};AccountKey=${storageAccount.listKeys().keys[0].value};EndpointSuffix=${environment().suffixes.storage}'

@description('Storage Account name.')
output accountName string = storageAccount.name
