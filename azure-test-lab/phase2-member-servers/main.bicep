// Phase 2: the domain-joined member servers - a 2-node ADFS farm, an IIS
// server, a SQL Server, and a dev/debug box. Deploy this only after
// Phase 1 (VNet + dc-01) has finished, its forest promotion has been
// verified, and you've run the manual VNet DNS cutover described in
// azure-test-lab/docs/03-manual-dns-cutover.md.
targetScope = 'resourceGroup'

@description('Azure region for every resource. Must match Phase 1\'s location.')
param location string = resourceGroup().location

@description('FQDN of the domain created in Phase 1. Must match exactly.')
param domainName string = 'vmlab.local'

@description('NetBIOS name of the domain created in Phase 1. Must match exactly.')
param netbiosName string = 'VMLAB'

@description('Resource ID of the lab subnet - copy this from Phase 1\'s subnetId output.')
param subnetId string

@description('Hostname of the domain controller deployed in Phase 1.')
param dcHostName string = 'dc-01'

@description('Private IP of the domain controller deployed in Phase 1. Must match dcPrivateIp from Phase 1 exactly. Set explicitly as the DNS server on every NIC in this deployment, rather than relying on the VNet-level DNS setting, since a NIC only picks up a VNet DNS change on its next DHCP lease - explicit per-NIC DNS works regardless of when each VM was created relative to the manual DNS cutover step.')
param dcPrivateIp string = '10.20.1.4'

@description('Local administrator username used for every VM. Also becomes the domain administrator username after Phase 1\'s forest promotion (Install-ADDSForest promotes the account that ran it), so it is reused here as the domain-admin identity for role bootstrap scripts.')
param localAdminUsername string = 'labadmin'

@description('Local administrator / domain administrator password. Must match the password used in Phase 1.')
@secure()
param localAdminPassword string

@description('Samaccountname for the ADFS farm service account this deployment creates.')
param adfsServiceAccountName string = 'svc-adfs'

@description('Password for the ADFS service account.')
@secure()
param adfsServiceAccountPassword string

@description('Temporary password used only to protect the bootstrap certificate while it is handed off from adfs-01 to adfs-02 via a share on dc-01. Generated fresh per deployment - not needed afterward.')
@secure()
param adfsBootstrapCertPassword string = newGuid()

@description('Static private IP for adfs-01 (the ADFS farm primary).')
param adfs01PrivateIp string = '10.20.1.10'

@description('Static private IP for adfs-02 (the ADFS farm secondary).')
param adfs02PrivateIp string = '10.20.1.11'

@description('Static private IP for iis-01.')
param iisPrivateIp string = '10.20.1.12'

@description('Static private IP for sql-01.')
param sqlPrivateIp string = '10.20.1.13'

@description('Static private IP for dev-01.')
param devPrivateIp string = '10.20.1.14'

@description('VM size for both ADFS nodes.')
param adfsVmSize string = 'Standard_B2ms'

@description('VM size for the IIS server.')
param iisVmSize string = 'Standard_B2ms'

@description('VM size for the SQL Server.')
param sqlVmSize string = 'Standard_B4ms'

@description('VM size for the dev/debug box.')
param devVmSize string = 'Standard_B4ms'

@description('Daily auto-shutdown time (24hr HHmm) for every lab VM.')
param autoShutdownTime string = '1900'

@description('Windows time zone ID used for the auto-shutdown schedule.')
param timeZoneId string = 'Eastern Standard Time'

var commonTags = {
  purpose: 'wincert-test-lab'
  owner: 'Bob.Pokorny@keyfactor.com'
}

var domainAdminUsername = '${netbiosName}\\${localAdminUsername}'
var federationServiceName = 'sts.${domainName}'

var memberServerImage = {
  publisher: 'MicrosoftWindowsServer'
  offer: 'WindowsServer'
  sku: '2022-datacenter-azure-edition'
  version: 'latest'
}

// SQL Server 2022 Developer on Windows Server 2022. The "sqldev-gen2" SKU
// is the free Developer edition - it does NOT need a marketplace purchase
// plan or "az vm image terms accept" (Azure's own validation error says so
// explicitly if a plan block is included: "doesn't require plan
// information"). Leave sqlMarketplacePlan as {} for this image.
var sqlImageReference = {
  publisher: 'MicrosoftSQLServer'
  offer: 'sql2022-ws2022'
  sku: 'sqldev-gen2'
  version: 'latest'
}
var sqlMarketplacePlan = {}

// --- dc-01 side prerequisites: service account, DNS record, cert handoff share ---
resource dcVm 'Microsoft.Compute/virtualMachines@2023-09-01' existing = {
  name: dcHostName
}

resource adfsPrereqs 'Microsoft.Compute/virtualMachines/runCommands@2023-09-01' = {
  parent: dcVm
  name: 'initialize-adfs-prereqs'
  location: location
  tags: commonTags
  properties: {
    source: {
      script: loadTextContent('scripts/Initialize-AdfsPrereqs.ps1')
    }
    parameters: [
      {
        name: 'DomainName'
        value: domainName
      }
      {
        name: 'FederationServiceName'
        value: federationServiceName
      }
      {
        name: 'Adfs01PrivateIp'
        value: adfs01PrivateIp
      }
      {
        name: 'AdfsServiceAccountName'
        value: adfsServiceAccountName
      }
    ]
    protectedParameters: [
      {
        name: 'AdfsServiceAccountPassword'
        value: adfsServiceAccountPassword
      }
    ]
    treatFailureAsDeploymentFailure: true
    timeoutInSeconds: 600
    asyncExecution: false
  }
}

// --- ADFS farm: primary, then secondary ---
module adfsPrimary '../modules/windows-vm.bicep' = {
  name: 'deploy-adfs-01'
  params: {
    location: location
    vmName: 'adfs-01'
    vmSize: adfsVmSize
    adminUsername: localAdminUsername
    adminPassword: localAdminPassword
    subnetId: subnetId
    staticPrivateIp: adfs01PrivateIp
    dnsServers: [
      dcPrivateIp
    ]
    imageReference: memberServerImage
    joinDomain: true
    domainName: domainName
    domainJoinUsername: domainAdminUsername
    domainJoinPassword: localAdminPassword
    bootstrapScriptName: 'bootstrap-adfs-primary'
    bootstrapScriptContent: loadTextContent('scripts/Bootstrap-AdfsPrimary.ps1')
    bootstrapParameters: [
      {
        name: 'DomainName'
        value: domainName
      }
      {
        name: 'FederationServiceName'
        value: federationServiceName
      }
      {
        name: 'NetbiosName'
        value: netbiosName
      }
      {
        name: 'AdfsServiceAccountName'
        value: adfsServiceAccountName
      }
      {
        name: 'DcHostName'
        value: dcHostName
      }
      {
        name: 'DomainAdminUsername'
        value: domainAdminUsername
      }
    ]
    bootstrapProtectedParameters: {
      AdfsServiceAccountPassword: adfsServiceAccountPassword
      CertExportPassword: adfsBootstrapCertPassword
      DomainAdminPassword: localAdminPassword
    }
    bootstrapTimeoutInSeconds: 1800
    autoShutdownTime: autoShutdownTime
    timeZoneId: timeZoneId
    tags: commonTags
  }
  dependsOn: [
    adfsPrereqs
  ]
}

module adfsSecondary '../modules/windows-vm.bicep' = {
  name: 'deploy-adfs-02'
  params: {
    location: location
    vmName: 'adfs-02'
    vmSize: adfsVmSize
    adminUsername: localAdminUsername
    adminPassword: localAdminPassword
    subnetId: subnetId
    staticPrivateIp: adfs02PrivateIp
    dnsServers: [
      dcPrivateIp
    ]
    imageReference: memberServerImage
    joinDomain: true
    domainName: domainName
    domainJoinUsername: domainAdminUsername
    domainJoinPassword: localAdminPassword
    bootstrapScriptName: 'bootstrap-adfs-secondary'
    bootstrapScriptContent: loadTextContent('scripts/Bootstrap-AdfsSecondary.ps1')
    bootstrapParameters: [
      {
        name: 'NetbiosName'
        value: netbiosName
      }
      {
        name: 'Adfs01HostName'
        value: 'adfs-01.${domainName}'
      }
      {
        name: 'DcHostName'
        value: dcHostName
      }
      {
        name: 'DomainAdminUsername'
        value: domainAdminUsername
      }
    ]
    bootstrapProtectedParameters: {
      AdfsServiceAccountPassword: adfsServiceAccountPassword
      CertExportPassword: adfsBootstrapCertPassword
      DomainAdminPassword: localAdminPassword
    }
    bootstrapTimeoutInSeconds: 1800
    autoShutdownTime: autoShutdownTime
    timeZoneId: timeZoneId
    tags: commonTags
  }
  dependsOn: [
    adfsPrimary
  ]
}

// --- IIS server ---
module iis '../modules/windows-vm.bicep' = {
  name: 'deploy-iis-01'
  params: {
    location: location
    vmName: 'iis-01'
    vmSize: iisVmSize
    adminUsername: localAdminUsername
    adminPassword: localAdminPassword
    subnetId: subnetId
    staticPrivateIp: iisPrivateIp
    dnsServers: [
      dcPrivateIp
    ]
    imageReference: memberServerImage
    joinDomain: true
    domainName: domainName
    domainJoinUsername: domainAdminUsername
    domainJoinPassword: localAdminPassword
    bootstrapScriptName: 'install-iis-role'
    bootstrapScriptContent: loadTextContent('scripts/Install-IISRole.ps1')
    bootstrapParameters: [
      {
        name: 'VmFqdn'
        value: 'iis-01.${domainName}'
      }
    ]
    autoShutdownTime: autoShutdownTime
    timeZoneId: timeZoneId
    tags: commonTags
  }
}

// --- SQL Server (Marketplace image already has SQL Server installed) ---
module sql '../modules/windows-vm.bicep' = {
  name: 'deploy-sql-01'
  params: {
    location: location
    vmName: 'sql-01'
    vmSize: sqlVmSize
    adminUsername: localAdminUsername
    adminPassword: localAdminPassword
    subnetId: subnetId
    staticPrivateIp: sqlPrivateIp
    dnsServers: [
      dcPrivateIp
    ]
    imageReference: sqlImageReference
    marketplacePlan: sqlMarketplacePlan
    joinDomain: true
    domainName: domainName
    domainJoinUsername: domainAdminUsername
    domainJoinPassword: localAdminPassword
    bootstrapScriptName: 'confirm-sql-ready'
    bootstrapScriptContent: loadTextContent('scripts/Confirm-SqlServerReady.ps1')
    autoShutdownTime: autoShutdownTime
    timeZoneId: timeZoneId
    tags: commonTags
  }
}

// --- Dev/debug box ---
module dev '../modules/windows-vm.bicep' = {
  name: 'deploy-dev-01'
  params: {
    location: location
    vmName: 'dev-01'
    vmSize: devVmSize
    adminUsername: localAdminUsername
    adminPassword: localAdminPassword
    subnetId: subnetId
    staticPrivateIp: devPrivateIp
    dnsServers: [
      dcPrivateIp
    ]
    imageReference: memberServerImage
    osDiskSizeGB: 256
    joinDomain: true
    domainName: domainName
    domainJoinUsername: domainAdminUsername
    domainJoinPassword: localAdminPassword
    bootstrapScriptName: 'install-dev-prereqs'
    bootstrapScriptContent: loadTextContent('scripts/Install-DevBoxPrereqs.ps1')
    bootstrapTimeoutInSeconds: 5400
    autoShutdownTime: autoShutdownTime
    timeZoneId: timeZoneId
    tags: commonTags
  }
}

output adfs01PrivateIpAddress string = adfsPrimary.outputs.privateIpAddress
output adfs01PublicIpAddress string = adfsPrimary.outputs.publicIpAddress
output adfs02PrivateIpAddress string = adfsSecondary.outputs.privateIpAddress
output adfs02PublicIpAddress string = adfsSecondary.outputs.publicIpAddress
output iisPrivateIpAddress string = iis.outputs.privateIpAddress
output iisPublicIpAddress string = iis.outputs.publicIpAddress
output sqlPrivateIpAddress string = sql.outputs.privateIpAddress
output sqlPublicIpAddress string = sql.outputs.publicIpAddress
output devPrivateIpAddress string = dev.outputs.privateIpAddress
output devPublicIpAddress string = dev.outputs.publicIpAddress
output federationServiceName string = federationServiceName
