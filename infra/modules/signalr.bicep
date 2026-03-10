@description('Azure region for the SignalR Service.')
param location string

@description('Base name prefix for resources.')
param namePrefix string

@description('SignalR Service SKU.')
@allowed(['Free_F1', 'Standard_S1', 'Premium_P1'])
param skuName string = 'Free_F1'

@description('Tags to apply to all resources.')
param tags object = {}

resource signalr 'Microsoft.SignalRService/signalR@2024-03-01' = {
  name: '${namePrefix}-signalr'
  location: location
  tags: tags
  sku: {
    name: skuName
    capacity: 1
  }
  kind: 'SignalR'
  properties: {
    features: [
      {
        flag: 'ServiceMode'
        value: 'Default'
      }
      {
        flag: 'EnableConnectivityLogs'
        value: 'true'
      }
    ]
    cors: {
      allowedOrigins: ['*']
    }
  }
}

@description('SignalR Service connection string.')
#disable-next-line outputs-should-not-contain-secrets // Consumed by app deployment
output connectionString string = signalr.listKeys().primaryConnectionString

@description('SignalR Service hostname.')
output hostName string = signalr.properties.hostName
