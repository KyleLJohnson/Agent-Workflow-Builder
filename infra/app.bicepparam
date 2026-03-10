using 'app.bicep'

// These values come from the infra deployment outputs.
// After running: az deployment group show -g <rg> -n main --query properties.outputs
// fill in the values below, or use az deployment group create with --parameters directly.

param webAppName = '<from-infra-output: webAppName>'

// Cosmos DB
param cosmosConnectionString = '<from-infra-output: cosmosConnectionString>'
param cosmosDedicatedGatewayEndpoint = ''

// Service Bus
param serviceBusConnectionString = '<from-infra-output: serviceBusConnectionString>'

// SignalR
param signalRConnectionString = '<from-infra-output: signalRConnectionString>'

// Azure OpenAI
param openAiEndpoint = '<from-infra-output: openAiEndpoint>'
param openAiDeploymentName = '<from-infra-output: openAiDeploymentName>'
param openAiApiKey = '<from-infra-output: openAiApiKey>'

// Storage
param storageConnectionString = '<from-infra-output: storageConnectionString>'
param blobPlanPollingEnabled = false

// Application Insights
param appInsightsConnectionString = '<from-infra-output: appInsightsConnectionString>'

// Entra ID — leave as placeholders to disable auth, or fill in real values
param entraIdTenantId = '<tenant-id>'
param entraIdClientId = '<client-id>'
param entraIdAudience = 'api://<client-id>'

// Workflow engine (defaults are fine for dev)
param clarificationTimeoutMinutes = 10
param maxLoopIterations = 3
param maxConcurrentExecutionsPerUser = 5
param signalingPollingIntervalMs = 2000
