targetScope = 'resourceGroup'

@description('Azure region for all resources.')
param location string

@description('Environment name used as a suffix in resource names.')
@minLength(2)
@maxLength(8)
param environmentName string

@description('App Service Plan SKU.')
param appServiceSkuName string = 'B1'

@description('Number of App Service Plan instances.')
param appServiceInstanceCount int = 1

@description('Service Bus SKU tier.')
@allowed(['Basic', 'Standard', 'Premium'])
param serviceBusSkuName string = 'Basic'

@description('SignalR Service SKU.')
@allowed(['Free_F1', 'Standard_S1', 'Premium_P1'])
param signalRSkuName string = 'Free_F1'

@description('Deploy an Azure OpenAI resource. Set to false to use an existing OpenAI endpoint.')
param deployOpenAi bool = false

@description('OpenAI model name to deploy.')
param openAiModelName string = 'gpt-4.1-mini'

@description('OpenAI model version.')
param openAiModelVersion string = '2025-04-14'

@description('OpenAI deployment capacity (thousands of tokens per minute).')
param openAiCapacity int = 30

@description('Enable Cosmos DB Dedicated Gateway for integrated cache.')
param cosmosEnableDedicatedGateway bool = false

@description('Deploy VNet with private endpoints for backend services.')
param enablePrivateNetworking bool = false

@description('Tags to apply to all resources.')
param tags object = {}

var namePrefix string = 'awb-${environmentName}'

module networking 'modules/networking.bicep' = if (enablePrivateNetworking) {
  params: {
    location: location
    namePrefix: namePrefix
    tags: tags
  }
}

module appService 'modules/app-service.bicep' = {
  params: {
    location: location
    namePrefix: namePrefix
    skuName: appServiceSkuName
    instanceCount: appServiceInstanceCount
    vnetIntegrationSubnetId: enablePrivateNetworking ? networking.?outputs.?appSubnetId ?? '' : ''
    tags: tags
  }
}

module cosmosDb 'modules/cosmos-db.bicep' = {
  params: {
    location: location
    namePrefix: namePrefix
    principalId: appService.outputs.managedIdentityPrincipalId
    enableDedicatedGateway: cosmosEnableDedicatedGateway
    privateEndpointSubnetId: enablePrivateNetworking ? networking.?outputs.?privateEndpointSubnetId ?? '' : ''
    vnetId: enablePrivateNetworking ? networking.?outputs.?vnetId ?? '' : ''
    tags: tags
  }
}

module serviceBus 'modules/service-bus.bicep' = {
  params: {
    location: location
    namePrefix: namePrefix
    skuName: serviceBusSkuName
    principalId: appService.outputs.managedIdentityPrincipalId
    tags: tags
  }
}

module signalR 'modules/signalr.bicep' = {
  params: {
    location: location
    namePrefix: namePrefix
    skuName: signalRSkuName
    tags: tags
  }
}

module openAi 'modules/openai.bicep' = if (deployOpenAi) {
  params: {
    location: location
    namePrefix: namePrefix
    modelName: openAiModelName
    modelVersion: openAiModelVersion
    capacityThousands: openAiCapacity
    tags: tags
  }
}

module storage 'modules/storage.bicep' = {
  params: {
    location: location
    namePrefix: namePrefix
    tags: tags
  }
}

module appInsights 'modules/app-insights.bicep' = {
  params: {
    location: location
    namePrefix: namePrefix
    tags: tags
  }
}

// App Service outputs
@description('Name of the deployed Web App.')
output webAppName string = appService.outputs.webAppName

@description('Default hostname of the Web App.')
output webAppDefaultHostName string = appService.outputs.webAppDefaultHostName

// Cosmos DB outputs
@description('Cosmos DB connection string.')
output cosmosConnectionString string = cosmosDb.outputs.connectionString

@description('Cosmos DB endpoint.')
output cosmosEndpoint string = cosmosDb.outputs.endpoint

@description('Cosmos DB Dedicated Gateway endpoint.')
output cosmosDedicatedGatewayEndpoint string = cosmosDb.outputs.dedicatedGatewayEndpoint

// Service Bus outputs
@description('Service Bus connection string.')
output serviceBusConnectionString string = serviceBus.outputs.connectionString

// SignalR outputs
@description('SignalR Service connection string.')
output signalRConnectionString string = signalR.outputs.connectionString

// OpenAI outputs (only populated when deployOpenAi is true)
@description('Azure OpenAI endpoint.')
output openAiEndpoint string = deployOpenAi ? openAi.?outputs.?endpoint ?? '' : ''

@description('Azure OpenAI deployment name.')
output openAiDeploymentName string = deployOpenAi ? openAi.?outputs.?deploymentName ?? '' : ''

@description('Azure OpenAI API key.')
output openAiApiKey string = deployOpenAi ? openAi.?outputs.?apiKey ?? '' : ''

// Storage outputs
@description('Storage Account connection string.')
output storageConnectionString string = storage.outputs.connectionString

// App Insights outputs
@description('Application Insights connection string.')
output appInsightsConnectionString string = appInsights.outputs.connectionString
