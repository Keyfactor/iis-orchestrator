// VNet + single subnet for the wincert test lab, with the NSG (from
// modules/nsg.bicep) associated at the subnet level.
@description('Azure region for the VNet.')
param location string

@description('Name of the VNet.')
param vnetName string

@description('Address space for the VNet, e.g. 10.20.0.0/16.')
param vnetAddressPrefix string

@description('Name of the single subnet.')
param subnetName string

@description('Address prefix for the subnet, e.g. 10.20.1.0/24.')
param subnetAddressPrefix string

@description('Resource ID of the NSG to associate with the subnet.')
param nsgId string

@description('Tags applied to the VNet.')
param tags object = {}

resource vnet 'Microsoft.Network/virtualNetworks@2023-09-01' = {
  name: vnetName
  location: location
  tags: tags
  properties: {
    addressSpace: {
      addressPrefixes: [
        vnetAddressPrefix
      ]
    }
    subnets: [
      {
        name: subnetName
        properties: {
          addressPrefix: subnetAddressPrefix
          networkSecurityGroup: {
            id: nsgId
          }
        }
      }
    ]
  }
}

output vnetId string = vnet.id
output vnetName string = vnet.name
output subnetId string = vnet.properties.subnets[0].id
