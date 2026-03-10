using 'main.bicep'

param location = 'westus2'
param environmentName = 'dev'

param appServiceSkuName = 'B1'
param appServiceInstanceCount = 1

param serviceBusSkuName = 'Basic'
param signalRSkuName = 'Free_F1'

param openAiModelName = 'gpt-4.1-mini'
param openAiModelVersion = '2025-04-14'
param openAiCapacity = 30

param cosmosEnableDedicatedGateway = false

param enablePrivateNetworking = true

param tags = {
  environment: 'dev'
  project: 'AgentWorkflowBuilder'
}
