// Generic Windows VM for the wincert test lab: public IP + NIC + VM,
// with optional domain-join and an optional one-shot bootstrap script
// (run via Microsoft.Compute/virtualMachines/runCommands, which needs no
// storage account staging and surfaces its own output/errors).
@description('Azure region for the VM.')
param location string

@description('Name of the VM (also used to derive the NIC/public IP/OS disk names).')
param vmName string

@description('VM size, e.g. Standard_B2ms.')
param vmSize string

@description('Local administrator username.')
param adminUsername string

@description('Local administrator password.')
@secure()
param adminPassword string

@description('Resource ID of the subnet the VM\'s NIC is attached to.')
param subnetId string

@description('Marketplace image reference for the VM.')
param imageReference object = {
  publisher: 'MicrosoftWindowsServer'
  offer: 'WindowsServer'
  sku: '2022-datacenter-azure-edition'
  version: 'latest'
}

@description('Marketplace purchase plan for the image, if one is required (e.g. the SQL Server marketplace image). Leave empty for images that don\'t need one. If set, run "az vm image terms accept" for this image before deploying.')
param marketplacePlan object = {}

@description('OS disk size in GB.')
param osDiskSizeGB int = 128

@description('Static private IP address for the NIC. Leave empty for dynamic (DHCP) allocation, which is fine for any domain-joined member server since Windows registers its own hostname in AD-integrated DNS.')
param staticPrivateIp string = ''

@description('DNS servers to set explicitly on this NIC (e.g. the domain controller\'s private IP). Leave empty to inherit the VNet-level DNS setting. Explicit per-NIC DNS is used throughout this lab rather than relying on VNet-level inheritance, because Azure only pushes a VNet DNS change to a NIC on its next DHCP lease - a NIC created before a VNet DNS update can otherwise keep stale DNS settings indefinitely.')
param dnsServers array = []

@description('Whether to enable Trusted Launch (Secure Boot + vTPM). Requires a Gen2 image - true for every image used in this lab.')
param enableTrustedLaunch bool = true

@description('Tags applied to every resource this module creates.')
param tags object = {}

@description('Whether to domain-join this VM after it boots.')
param joinDomain bool = false

@description('FQDN of the domain to join, e.g. vmlab.local.')
param domainName string = ''

@description('Domain account (UPN or DOMAIN\\user) with rights to join computers to the domain.')
param domainJoinUsername string = ''

@description('Password for domainJoinUsername.')
@secure()
param domainJoinPassword string = ''

@description('OU path to place the computer object in. Leave empty for the default Computers container.')
param domainJoinOUPath string = ''

@description('Name for the bootstrap runCommand resource. Leave empty to skip running a bootstrap script.')
param bootstrapScriptName string = ''

@description('Inline PowerShell script content to run once via runCommand, typically loaded with loadTextContent() by the caller.')
param bootstrapScriptContent string = ''

@description('Named parameters passed to the bootstrap script (bound to a matching param() block). Each item is { name, value }.')
param bootstrapParameters array = []

@description('Named parameters passed to the bootstrap script that should not be logged in cleartext (e.g. passwords), as a map of paramName -> value.')
@secure()
param bootstrapProtectedParameters object = {}

@description('Maximum time the bootstrap script is allowed to run before the deployment fails it. Increase for long-running installs (e.g. Visual Studio).')
param bootstrapTimeoutInSeconds int = 2400

@description('Shutdown time in 24hr HHmm format for the daily auto-shutdown schedule.')
param autoShutdownTime string = '1900'

@description('Windows time zone ID for the auto-shutdown schedule.')
param timeZoneId string = 'Eastern Standard Time'

var hasPlan = !empty(marketplacePlan)
var protectedParameterArray = [for entry in items(bootstrapProtectedParameters): {
  name: entry.key
  value: entry.value
}]

resource publicIp 'Microsoft.Network/publicIPAddresses@2023-09-01' = {
  name: '${vmName}-pip'
  location: location
  tags: tags
  sku: {
    name: 'Standard'
  }
  properties: {
    publicIPAllocationMethod: 'Static'
  }
}

resource nic 'Microsoft.Network/networkInterfaces@2023-09-01' = {
  name: '${vmName}-nic'
  location: location
  tags: tags
  properties: {
    ipConfigurations: [
      {
        name: 'ipconfig1'
        properties: {
          subnet: {
            id: subnetId
          }
          privateIPAllocationMethod: empty(staticPrivateIp) ? 'Dynamic' : 'Static'
          privateIPAddress: empty(staticPrivateIp) ? null : staticPrivateIp
          publicIPAddress: {
            id: publicIp.id
          }
        }
      }
    ]
    dnsSettings: empty(dnsServers) ? any(null) : {
      dnsServers: dnsServers
    }
  }
}

resource vm 'Microsoft.Compute/virtualMachines@2023-09-01' = {
  name: vmName
  location: location
  tags: tags
  plan: hasPlan ? marketplacePlan : any(null)
  properties: {
    hardwareProfile: {
      vmSize: vmSize
    }
    osProfile: {
      computerName: vmName
      adminUsername: adminUsername
      adminPassword: adminPassword
      windowsConfiguration: {
        enableAutomaticUpdates: true
        provisionVMAgent: true
      }
    }
    storageProfile: {
      imageReference: imageReference
      osDisk: {
        createOption: 'FromImage'
        managedDisk: {
          storageAccountType: 'StandardSSD_LRS'
        }
        diskSizeGB: osDiskSizeGB
      }
    }
    networkProfile: {
      networkInterfaces: [
        {
          id: nic.id
        }
      ]
    }
    securityProfile: enableTrustedLaunch ? {
      securityType: 'TrustedLaunch'
      uefiSettings: {
        secureBootEnabled: true
        vTpmEnabled: true
      }
    } : any(null)
  }
}

resource domainJoinExtension 'Microsoft.Compute/virtualMachines/extensions@2023-09-01' = if (joinDomain) {
  parent: vm
  name: 'joindomain'
  location: location
  tags: tags
  properties: {
    publisher: 'Microsoft.Compute'
    type: 'JsonADDomainExtension'
    typeHandlerVersion: '1.3'
    autoUpgradeMinorVersion: true
    settings: {
      Name: domainName
      OUPath: domainJoinOUPath
      User: domainJoinUsername
      Restart: true
      Options: 3
    }
    protectedSettings: {
      Password: domainJoinPassword
    }
  }
}

// Always runs as Local System - this resource type's own runAsUser/
// runAsPassword feature was tried and failed in testing with a Win32
// "Incorrect function" error tied to the Secondary Logon service path it
// depends on. Scripts that need domain-admin rights (e.g. ADFS farm
// creation) get a DomainAdminUsername/Password bootstrap parameter
// instead and impersonate internally via a short-lived scheduled task -
// see Bootstrap-AdfsPrimary.ps1 for the pattern.
resource bootstrap 'Microsoft.Compute/virtualMachines/runCommands@2023-09-01' = if (!empty(bootstrapScriptName)) {
  parent: vm
  name: bootstrapScriptName
  location: location
  tags: tags
  properties: {
    source: {
      script: bootstrapScriptContent
    }
    parameters: bootstrapParameters
    protectedParameters: protectedParameterArray
    treatFailureAsDeploymentFailure: true
    timeoutInSeconds: bootstrapTimeoutInSeconds
    asyncExecution: false
  }
  dependsOn: joinDomain ? [
    domainJoinExtension
  ] : []
}

resource shutdownSchedule 'Microsoft.DevTestLab/schedules@2018-09-15' = {
  name: 'shutdown-computevm-${vmName}'
  location: location
  tags: tags
  properties: {
    status: 'Enabled'
    taskType: 'ComputeVmShutdownTask'
    dailyRecurrence: {
      time: autoShutdownTime
    }
    timeZoneId: timeZoneId
    targetResourceId: vm.id
    notificationSettings: {
      status: 'Disabled'
    }
  }
}

output vmId string = vm.id
output vmName string = vm.name
output privateIpAddress string = nic.properties.ipConfigurations[0].properties.privateIPAddress
output publicIpAddress string = publicIp.properties.ipAddress
