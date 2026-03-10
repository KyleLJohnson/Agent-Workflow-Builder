@description('Azure region for the Cosmos DB account.')
param location string

@description('Base name prefix for resources.')
param namePrefix string

@description('Principal ID for RBAC role assignment (Cosmos DB Built-in Data Contributor).')
param principalId string

@description('Enable the Dedicated Gateway for integrated cache.')
param enableDedicatedGateway bool = false

@description('Dedicated Gateway SKU (only used when enableDedicatedGateway is true).')
param dedicatedGatewaySkuName string = 'Cosmos.D4s'

@description('Number of Dedicated Gateway instances.')
param dedicatedGatewayInstanceCount int = 1

@description('Subnet resource ID for the private endpoint. Empty to skip private endpoint.')
param privateEndpointSubnetId string = ''

@description('VNet resource ID for private DNS zone link. Required when privateEndpointSubnetId is set.')
param vnetId string = ''

@description('Tags to apply to all resources.')
param tags object = {}

resource cosmosAccount 'Microsoft.DocumentDB/databaseAccounts@2024-12-01-preview' = {
  name: '${namePrefix}-cosmos'
  location: location
  tags: tags
  kind: 'GlobalDocumentDB'
  properties: {
    databaseAccountOfferType: 'Standard'
    capacityMode: 'Serverless'
    disableLocalAuth: true
    publicNetworkAccess: 'Disabled'
    consistencyPolicy: {
      defaultConsistencyLevel: 'Session'
    }
    locations: [
      {
        locationName: location
        failoverPriority: 0
        isZoneRedundant: false
      }
    ]
    enableFreeTier: false
  }
}

// Cosmos DB Built-in Data Contributor role: 00000000-0000-0000-0000-000000000002
resource cosmosRbac 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2024-12-01-preview' = {
  parent: cosmosAccount
  name: guid(cosmosAccount.id, principalId, '00000000-0000-0000-0000-000000000002')
  properties: {
    roleDefinitionId: '${cosmosAccount.id}/sqlRoleDefinitions/00000000-0000-0000-0000-000000000002'
    principalId: principalId
    scope: cosmosAccount.id
  }
}

resource database 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2024-12-01-preview' = {
  parent: cosmosAccount
  name: 'AgentWorkflowBuilder'
  properties: {
    resource: {
      id: 'AgentWorkflowBuilder'
    }
  }
}

type ContainerConfig = {
  @description('Container name.')
  name: string
  @description('Partition key path.')
  partitionKey: string
  @description('Default TTL in seconds. -1 to disable.')
  defaultTtl: int
}

var containers ContainerConfig[] = [
  { name: 'workflows', partitionKey: '/userId', defaultTtl: -1 }
  { name: 'executions', partitionKey: '/workflowId', defaultTtl: -1 }
  { name: 'sessions', partitionKey: '/executionId', defaultTtl: 86400 }
  { name: 'counters', partitionKey: '/id', defaultTtl: -1 }
  { name: 'session-leases', partitionKey: '/id', defaultTtl: -1 }
]

resource cosmosContainers 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-12-01-preview' = [
  for container in containers: {
    parent: database
    name: container.name
    properties: {
      resource: {
        id: container.name
        partitionKey: {
          paths: [container.partitionKey]
          kind: 'Hash'
          version: 2
        }
        defaultTtl: container.defaultTtl
      }
    }
  }
]

// Dedicated Gateway for integrated cache (optional)
resource dedicatedGateway 'Microsoft.DocumentDB/databaseAccounts/services@2024-12-01-preview' =
  if (enableDedicatedGateway) {
    parent: cosmosAccount
    name: 'SqlDedicatedGateway'
    properties: {
      serviceType: 'SqlDedicatedGateway'
      instanceSize: dedicatedGatewaySkuName
      instanceCount: dedicatedGatewayInstanceCount
    }
  }

// Private endpoint for secure VNet-only access
resource cosmosPrivateEndpoint 'Microsoft.Network/privateEndpoints@2024-05-01' =
  if (!empty(privateEndpointSubnetId)) {
    name: '${namePrefix}-cosmos-pe'
    location: location
    tags: tags
    properties: {
      subnet: {
        id: privateEndpointSubnetId
      }
      privateLinkServiceConnections: [
        {
          name: '${namePrefix}-cosmos-plsc'
          properties: {
            privateLinkServiceId: cosmosAccount.id
            groupIds: ['Sql']
          }
        }
      ]
    }
  }

resource cosmosDnsZone 'Microsoft.Network/privateDnsZones@2024-06-01' =
  if (!empty(privateEndpointSubnetId)) {
    name: 'privatelink.documents.azure.com'
    location: 'global'
    tags: tags
  }

resource cosmosDnsZoneLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2024-06-01' =
  if (!empty(privateEndpointSubnetId)) {
    parent: cosmosDnsZone
    name: '${namePrefix}-cosmos-vnetlink'
    location: 'global'
    properties: {
      virtualNetwork: {
        id: vnetId
      }
      registrationEnabled: false
    }
  }

resource cosmosDnsGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2024-05-01' =
  if (!empty(privateEndpointSubnetId)) {
    parent: cosmosPrivateEndpoint
    name: 'default'
    properties: {
      privateDnsZoneConfigs: [
        {
          name: 'cosmos'
          properties: {
            privateDnsZoneId: cosmosDnsZone.id
          }
        }
      ]
    }
  }

@description('Cosmos DB account connection string.')
#disable-next-line outputs-should-not-contain-secrets // Consumed by app deployment
output connectionString string = cosmosAccount.listConnectionStrings().connectionStrings[0].connectionString

@description('Cosmos DB account endpoint.')
output endpoint string = cosmosAccount.properties.documentEndpoint

@description('Dedicated Gateway endpoint (empty if not enabled).')
output dedicatedGatewayEndpoint string = enableDedicatedGateway
  ? replace(cosmosAccount.properties.documentEndpoint, '.documents.azure.com', '.sqlx.cosmos.azure.com')
  : ''

@description('Cosmos DB account name.')
output accountName string = cosmosAccount.name
