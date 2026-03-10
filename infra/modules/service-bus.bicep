@description('Azure region for the Service Bus namespace.')
param location string

@description('Base name prefix for resources.')
param namePrefix string

@description('Service Bus SKU tier.')
@allowed(['Basic', 'Standard', 'Premium'])
param skuName string = 'Basic'

@description('Principal ID for RBAC role assignment (Azure Service Bus Data Owner).')
param principalId string

@description('Tags to apply to all resources.')
param tags object = {}

resource namespace 'Microsoft.ServiceBus/namespaces@2024-01-01' = {
  name: '${namePrefix}-servicebus'
  location: location
  tags: tags
  sku: {
    name: skuName
    tier: skuName
  }
}

resource executionQueue 'Microsoft.ServiceBus/namespaces/queues@2024-01-01' = {
  parent: namespace
  name: 'workflow-executions'
  properties: {
    lockDuration: 'PT5M'
    maxDeliveryCount: 3
  }
}

resource cancellationQueue 'Microsoft.ServiceBus/namespaces/queues@2024-01-01' = {
  parent: namespace
  name: 'execution-cancellations'
  properties: {
    lockDuration: 'PT1M'
    maxDeliveryCount: 5
  }
}

// Azure Service Bus Data Owner: 090c5cfd-751d-490a-894a-3ce6f1109419
resource sbRbac 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(namespace.id, principalId, '090c5cfd-751d-490a-894a-3ce6f1109419')
  scope: namespace
  properties: {
    roleDefinitionId: subscriptionResourceId(
      'Microsoft.Authorization/roleDefinitions',
      '090c5cfd-751d-490a-894a-3ce6f1109419'
    )
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}

@description('Service Bus connection string.')
#disable-next-line outputs-should-not-contain-secrets // Consumed by app deployment
output connectionString string = listKeys(
  '${namespace.id}/AuthorizationRules/RootManageSharedAccessKey',
  namespace.apiVersion
).primaryConnectionString

@description('Service Bus namespace name.')
output namespaceName string = namespace.name
