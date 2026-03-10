@description('Azure region for Application Insights.')
param location string

@description('Base name prefix for resources.')
param namePrefix string

@description('Tags to apply to all resources.')
param tags object = {}

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${namePrefix}-law'
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: '${namePrefix}-ai'
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
  }
}

@description('Application Insights instrumentation key.')
output instrumentationKey string = appInsights.properties.InstrumentationKey

@description('Application Insights connection string.')
output connectionString string = appInsights.properties.ConnectionString
