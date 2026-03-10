@description('Azure region for all resources.')
param location string

@description('Base name prefix for resources.')
param namePrefix string

@description('App Service Plan SKU name.')
param skuName string = 'B1'

@description('Number of App Service Plan instances.')
param instanceCount int = 1

@description('Allowed CORS origins.')
param corsOrigins array = ['*']

@description('Subnet resource ID for VNet integration. Empty to skip.')
param vnetIntegrationSubnetId string = ''

@description('Tags to apply to all resources.')
param tags object = {}

resource appServicePlan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: '${namePrefix}-plan'
  location: location
  tags: tags
  kind: 'linux'
  sku: {
    name: skuName
    capacity: instanceCount
  }
  properties: {
    reserved: true // Linux
  }
}

resource webApp 'Microsoft.Web/sites@2024-04-01' = {
  name: '${namePrefix}-app'
  location: location
  tags: tags
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    clientAffinityEnabled: false // Required for multi-instance stateless scale-out
    virtualNetworkSubnetId: !empty(vnetIntegrationSubnetId) ? vnetIntegrationSubnetId : null
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      alwaysOn: true
      webSocketsEnabled: true // Required for SignalR
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      http20Enabled: true
      cors: {
        allowedOrigins: corsOrigins
        supportCredentials: !contains(corsOrigins, '*')
      }
    }
  }
}

@description('Name of the deployed Web App.')
output webAppName string = webApp.name

@description('Default hostname of the Web App.')
output webAppDefaultHostName string = webApp.properties.defaultHostName

@description('Principal ID of the system-assigned managed identity.')
output managedIdentityPrincipalId string = webApp.identity.principalId
