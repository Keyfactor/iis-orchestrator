// Phase 1: VNet + NSG + a single Domain Controller, promoted to a brand-new
// AD forest. This is deliberately the only thing Phase 1 deploys - see
// azure-test-lab/README.md for why domain-join of the other VMs is a
// separate Phase 2 deployment, gated by a manual VNet-DNS cutover step.
targetScope = 'resourceGroup'

@description('Azure region for every resource. Defaults to the resource group\'s own region.')
param location string = resourceGroup().location

@description('FQDN of the new AD forest/domain, e.g. vmlab.local.')
param domainName string = 'vmlab.local'

@description('NetBIOS name for the new domain, e.g. VMLAB.')
param netbiosName string = 'VMLAB'

@description('Address space for the lab VNet.')
param vnetAddressPrefix string = '10.20.0.0/16'

@description('Address prefix for the single lab subnet.')
param subnetAddressPrefix string = '10.20.1.0/24'

@description('Static private IP for the domain controller. Must fall inside subnetAddressPrefix.')
param dcPrivateIp string = '10.20.1.4'

@description('Your current public IP address (no CIDR suffix) - RDP is only allowed from this address. Find it at https://www.whatismyip.com or similar.')
param sourceIpAddress string

@description('Local administrator username for the domain controller VM.')
param localAdminUsername string = 'labadmin'

@description('Local administrator password for the domain controller VM.')
@secure()
param localAdminPassword string

@description('Directory Services Restore Mode (DSRM) password, required by Install-ADDSForest. Keep this safe - it is needed for any future AD DS restore operations.')
@secure()
param safeModeAdminPassword string

@description('VM size for the domain controller.')
param dcVmSize string = 'Standard_B2ms'

@description('Daily auto-shutdown time (24hr HHmm) for every lab VM.')
param autoShutdownTime string = '1900'

@description('Windows time zone ID used for the auto-shutdown schedule.')
param timeZoneId string = 'Eastern Standard Time'

var commonTags = {
  purpose: 'wincert-test-lab'
  owner: 'Bob.Pokorny@keyfactor.com'
}

module nsg '../modules/nsg.bicep' = {
  name: 'deploy-nsg'
  params: {
    location: location
    nsgName: 'nsg-wincert-lab'
    sourceIpAddress: sourceIpAddress
    tags: commonTags
  }
}

module network '../modules/network.bicep' = {
  name: 'deploy-network'
  params: {
    location: location
    vnetName: 'vnet-wincert-lab'
    vnetAddressPrefix: vnetAddressPrefix
    subnetName: 'snet-lab'
    subnetAddressPrefix: subnetAddressPrefix
    nsgId: nsg.outputs.nsgId
    tags: commonTags
  }
}

module dc '../modules/windows-vm.bicep' = {
  name: 'deploy-dc-01'
  params: {
    location: location
    vmName: 'dc-01'
    vmSize: dcVmSize
    adminUsername: localAdminUsername
    adminPassword: localAdminPassword
    subnetId: network.outputs.subnetId
    staticPrivateIp: dcPrivateIp
    dnsServers: [
      dcPrivateIp
    ]
    joinDomain: false
    bootstrapScriptName: 'promote-ad-forest'
    bootstrapScriptContent: loadTextContent('scripts/New-ADForest.ps1')
    bootstrapParameters: [
      {
        name: 'DomainName'
        value: domainName
      }
      {
        name: 'NetbiosName'
        value: netbiosName
      }
    ]
    bootstrapProtectedParameters: {
      SafeModeAdminPassword: safeModeAdminPassword
    }
    autoShutdownTime: autoShutdownTime
    timeZoneId: timeZoneId
    tags: commonTags
  }
}

output vnetName string = network.outputs.vnetName
output subnetId string = network.outputs.subnetId
output nsgId string = nsg.outputs.nsgId
output dcPrivateIpAddress string = dc.outputs.privateIpAddress
output dcPublicIpAddress string = dc.outputs.publicIpAddress
output domainName string = domainName
output netbiosName string = netbiosName
