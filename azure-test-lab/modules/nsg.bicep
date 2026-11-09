// Network Security Group for the wincert test lab subnet.
// RDP is allowed only from the deployer's own public IP. Everything else
// inbound from the internet is denied. Traffic between VMs inside the
// VNet is trusted wholesale (not port-by-port) - a single AD domain needs
// far more than WinRM between its members (DNS 53, Kerberos 88, LDAP 389,
// SMB 445, RPC 135 + a dynamic high port range, etc.), and enumerating
// every one of those individually is exactly the kind of fragile
// allow-list that silently breaks AD functionality the moment one port is
// missed. Every VM in this lab is a member of the same single-subnet
// trust boundary (the domain itself), so the meaningful security boundary
// here is "VNet vs. internet," not port-by-port inside the VNet.
@description('Azure region for the NSG.')
param location string

@description('Name of the network security group.')
param nsgName string

@description('Public IP address (no CIDR suffix) that is allowed to RDP into the lab. Get yours from https://api.ipify.org or "whatismyip".')
param sourceIpAddress string

@description('Tags applied to the NSG.')
param tags object = {}

resource nsg 'Microsoft.Network/networkSecurityGroups@2023-09-01' = {
  name: nsgName
  location: location
  tags: tags
  properties: {
    securityRules: [
      {
        name: 'Allow-RDP-From-Admin-IP'
        properties: {
          priority: 100
          direction: 'Inbound'
          access: 'Allow'
          protocol: 'Tcp'
          sourceAddressPrefix: '${sourceIpAddress}/32'
          sourcePortRange: '*'
          destinationAddressPrefix: '*'
          destinationPortRange: '3389'
        }
      }
      {
        name: 'Allow-Intra-VNet'
        properties: {
          priority: 200
          direction: 'Inbound'
          access: 'Allow'
          protocol: '*'
          sourceAddressPrefix: 'VirtualNetwork'
          sourcePortRange: '*'
          destinationAddressPrefix: 'VirtualNetwork'
          destinationPortRange: '*'
        }
      }
      {
        name: 'Deny-All-Inbound-Explicit'
        properties: {
          priority: 4000
          direction: 'Inbound'
          access: 'Deny'
          protocol: '*'
          sourceAddressPrefix: '*'
          sourcePortRange: '*'
          destinationAddressPrefix: '*'
          destinationPortRange: '*'
        }
      }
    ]
  }
}

output nsgId string = nsg.id
output nsgName string = nsg.name
