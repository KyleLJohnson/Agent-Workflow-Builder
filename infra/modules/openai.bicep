@description('Azure region for the OpenAI resource.')
param location string

@description('Base name prefix for resources.')
param namePrefix string

@description('Model name to deploy.')
param modelName string = 'gpt-4.1-mini'

@description('Model version.')
param modelVersion string = '2025-04-14'

@description('Deployment capacity in thousands of tokens per minute.')
param capacityThousands int = 30

@description('Tags to apply to all resources.')
param tags object = {}

resource openai 'Microsoft.CognitiveServices/accounts@2024-10-01' = {
  name: '${namePrefix}-openai'
  location: location
  tags: tags
  kind: 'OpenAI'
  sku: {
    name: 'S0'
  }
  properties: {
    customSubDomainName: '${namePrefix}-openai'
    publicNetworkAccess: 'Enabled'
  }
}

resource modelDeployment 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = {
  parent: openai
  name: modelName
  sku: {
    name: 'GlobalStandard'
    capacity: capacityThousands
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: modelName
      version: modelVersion
    }
  }
}

@description('Azure OpenAI endpoint URL.')
output endpoint string = openai.properties.endpoint

@description('Deployed model name.')
output deploymentName string = modelDeployment.name

@description('Azure OpenAI resource name.')
output resourceName string = openai.name

@description('Azure OpenAI API key.')
#disable-next-line outputs-should-not-contain-secrets // Consumed by app deployment
output apiKey string = openai.listKeys().key1
