@description('Azure region for all resources.')
param location string

@description('Base name prefix for resources.')
param namePrefix string

@description('VNet address space.')
param vnetAddressPrefix string = '10.0.0.0/16'

@description('Subnet address prefix for App Service VNet integration.')
param appSubnetPrefix string = '10.0.1.0/24'

@description('Subnet address prefix for private endpoints.')
param privateEndpointSubnetPrefix string = '10.0.2.0/24'

@description('Tags to apply to all resources.')
param tags object = {}

resource vnet 'Microsoft.Network/virtualNetworks@2024-05-01' = {
  name: '${namePrefix}-vnet'
  location: location
  tags: tags
  properties: {
    addressSpace: {
      addressPrefixes: [vnetAddressPrefix]
    }
    subnets: [
      {
        name: 'app-integration'
        properties: {
          addressPrefix: appSubnetPrefix
          delegations: [
            {
              name: 'appServiceDelegation'
              properties: {
                serviceName: 'Microsoft.Web/serverFarms'
              }
            }
          ]
        }
      }
      {
        name: 'private-endpoints'
        properties: {
          addressPrefix: privateEndpointSubnetPrefix
        }
      }
    ]
  }
}

@description('Resource ID of the VNet.')
output vnetId string = vnet.id

@description('Resource ID of the App Service integration subnet.')
output appSubnetId string = vnet.properties.subnets[0].id

@description('Resource ID of the private endpoints subnet.')
output privateEndpointSubnetId string = vnet.properties.subnets[1].id
