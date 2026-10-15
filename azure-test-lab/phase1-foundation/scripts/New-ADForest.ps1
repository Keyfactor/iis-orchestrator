#Requires -RunAsAdministrator
<#
    Promotes this VM into a brand-new Active Directory forest.
    Idempotent: if AD DS is already installed and NTDS is running, this
    script is a no-op, so re-running phase1's main.bicep after this VM
    already exists (e.g. to change an unrelated parameter) will not attempt
    to re-promote an already-promoted domain controller.

    DomainName/NetbiosName/SafeModeAdminPassword are supplied by the calling
    runCommand resource's parameters/protectedParameters (see
    modules/windows-vm.bicep), which the platform binds to this param block.
#>
param(
    [Parameter(Mandatory = $true)]
    [string] $DomainName,

    [Parameter(Mandatory = $true)]
    [string] $NetbiosName,

    [Parameter(Mandatory = $true)]
    [string] $SafeModeAdminPassword
)

$ErrorActionPreference = 'Stop'

$domainName = $DomainName
$netbiosName = $NetbiosName
$safeModeAdminPassword = $SafeModeAdminPassword

$ntdsService = Get-Service -Name NTDS -ErrorAction SilentlyContinue
if ($ntdsService) {
    Write-Output "NTDS service already present - this VM is already a domain controller for a forest. Skipping promotion."
    exit 0
}

Write-Output "Installing AD-Domain-Services and management tools..."
Install-WindowsFeature -Name AD-Domain-Services -IncludeManagementTools | Out-Null

Write-Output "Promoting to a new forest: $domainName (NetBIOS: $netbiosName)..."
Import-Module ADDSDeployment

$secureSafeModePassword = ConvertTo-SecureString -String $safeModeAdminPassword -AsPlainText -Force

Install-ADDSForest `
    -DomainName $domainName `
    -DomainNetbiosName $netbiosName `
    -SafeModeAdministratorPassword $secureSafeModePassword `
    -InstallDns:$true `
    -DatabasePath 'C:\Windows\NTDS' `
    -LogPath 'C:\Windows\NTDS' `
    -SysvolPath 'C:\Windows\SYSVOL' `
    -Force:$true `
    -NoRebootOnCompletion:$false

Write-Output "Forest promotion requested. The VM will reboot to complete promotion."
