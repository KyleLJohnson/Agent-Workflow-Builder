targetScope = 'resourceGroup'

@description('Name of the existing Web App to configure.')
param webAppName string

// Cosmos DB
@description('Cosmos DB connection string.')
param cosmosConnectionString string

@description('Cosmos DB account endpoint for Managed Identity authentication.')
param cosmosEndpoint string = ''

@description('Cosmos DB Dedicated Gateway endpoint (empty to disable integrated cache).')
param cosmosDedicatedGatewayEndpoint string = ''

// Service Bus
@description('Service Bus connection string.')
param serviceBusConnectionString string

// SignalR
@description('Azure SignalR Service connection string.')
param signalRConnectionString string

// Azure OpenAI
@description('Azure OpenAI endpoint URL.')
param openAiEndpoint string

@description('Azure OpenAI deployed model name.')
param openAiDeploymentName string

@secure()
@description('Azure OpenAI API key.')
param openAiApiKey string

// Storage
@description('Storage Account connection string for blob plan polling.')
param storageConnectionString string

@description('Enable blob plan polling.')
param blobPlanPollingEnabled bool = false

// Application Insights
@description('Application Insights connection string.')
param appInsightsConnectionString string

// Entra ID (parameterized — app registration managed externally)
@description('Azure AD tenant ID. Leave as placeholder to disable auth.')
param entraIdTenantId string = '<tenant-id>'

@description('Azure AD client ID.')
param entraIdClientId string = '<client-id>'

@description('Azure AD audience.')
param entraIdAudience string = 'api://<client-id>'

// Workflow engine settings
@description('Clarification timeout in minutes.')
param clarificationTimeoutMinutes int = 10

@description('Maximum loop iterations per workflow step.')
param maxLoopIterations int = 3

@description('Maximum concurrent executions per user.')
param maxConcurrentExecutionsPerUser int = 5

@description('Signaling polling interval in milliseconds.')
param signalingPollingIntervalMs int = 2000

resource webApp 'Microsoft.Web/sites@2024-04-01' existing = {
  name: webAppName
}

resource appSettings 'Microsoft.Web/sites/config@2024-04-01' = {
  parent: webApp
  name: 'appsettings'
  properties: {
    // Cosmos DB
    CosmosDb__ConnectionString: cosmosConnectionString
    CosmosDb__Endpoint: cosmosEndpoint
    CosmosDb__DatabaseName: 'AgentWorkflowBuilder'
    CosmosDb__WorkflowContainerName: 'workflows'
    CosmosDb__ExecutionContainerName: 'executions'
    CosmosDb__SessionContainerName: 'sessions'
    CosmosDb__CounterContainerName: 'counters'
    CosmosDb__LeaseContainerName: 'session-leases'
    CosmosDb__DedicatedGatewayEndpoint: cosmosDedicatedGatewayEndpoint

    // Service Bus
    ServiceBus__ConnectionString: serviceBusConnectionString
    ServiceBus__ExecutionQueueName: 'workflow-executions'
    ServiceBus__CancellationQueueName: 'execution-cancellations'

    // Azure SignalR
    Azure__SignalR__ConnectionString: signalRConnectionString

    // Azure OpenAI (CopilotSdk)
    CopilotSdk__Provider__Type: 'azure'
    CopilotSdk__Provider__BaseUrl: openAiEndpoint
    CopilotSdk__Provider__ApiKey: openAiApiKey
    CopilotSdk__Provider__AzureApiVersion: '2024-10-21'
    CopilotSdk__DefaultModel: openAiDeploymentName

    // Entra ID / Azure AD
    #disable-next-line no-hardcoded-env-urls // Standard Azure AD login endpoint
    AzureAd__Instance: 'https://login.microsoftonline.com/'
    AzureAd__TenantId: entraIdTenantId
    AzureAd__ClientId: entraIdClientId
    AzureAd__Audience: entraIdAudience

    // Blob plan polling
    AzureBlobPlans__ConnectionString: storageConnectionString
    AzureBlobPlans__PollingIntervalSeconds: '30'
    AzureBlobPlans__Enabled: string(blobPlanPollingEnabled)

    // Workflow engine
    Workflow__ClarificationTimeoutMinutes: string(clarificationTimeoutMinutes)
    Workflow__MaxLoopIterations: string(maxLoopIterations)
    Workflow__MaxConcurrentExecutionsPerUser: string(maxConcurrentExecutionsPerUser)
    Workflow__SignalingPollingIntervalMs: string(signalingPollingIntervalMs)

    // Data storage path (persistent on Linux App Service)
    Data__BasePath: '/home/data'

    // Application Insights
    APPLICATIONINSIGHTS_CONNECTION_STRING: appInsightsConnectionString
  }
}
