#Requires -RunAsAdministrator
<#
    Runs on dc-01. Prepares everything the ADFS farm bootstrap needs from
    the domain side:
      1. A plain domain service account for the farm (simpler than a gMSA -
         no KDS root key propagation delay to wait out in a lab).
      2. A DNS A record for the federation service name, pointed at
         adfs-01's (static) private IP, since there's no load balancer in
         this lab.
      3. A small SMB share used purely to hand the ADFS bootstrap
         certificate from adfs-01 to adfs-02 (Add-AdfsFarmNode needs the
         same certificate, private key included, present on every node).

    Idempotent: safe to re-run if phase2's main.bicep is redeployed.
#>
param(
    [Parameter(Mandatory = $true)]
    [string] $DomainName,

    [Parameter(Mandatory = $true)]
    [string] $FederationServiceName,

    [Parameter(Mandatory = $true)]
    [string] $Adfs01PrivateIp,

    [Parameter(Mandatory = $true)]
    [string] $AdfsServiceAccountName,

    [Parameter(Mandatory = $true)]
    [string] $AdfsServiceAccountPassword
)

$ErrorActionPreference = 'Stop'
Import-Module ActiveDirectory

# --- 1. Service account -----------------------------------------------
$existingUser = Get-ADUser -Filter "SamAccountName -eq '$AdfsServiceAccountName'" -ErrorAction SilentlyContinue
if ($existingUser) {
    Write-Output "Service account '$AdfsServiceAccountName' already exists - skipping creation."
}
else {
    Write-Output "Creating ADFS service account '$AdfsServiceAccountName'..."
    $securePassword = ConvertTo-SecureString -String $AdfsServiceAccountPassword -AsPlainText -Force
    New-ADUser `
        -Name $AdfsServiceAccountName `
        -SamAccountName $AdfsServiceAccountName `
        -UserPrincipalName "$AdfsServiceAccountName@$DomainName" `
        -AccountPassword $securePassword `
        -Enabled $true `
        -PasswordNeverExpires $true `
        -CannotChangePassword $true
    Write-Output "Service account created."
}

# --- 2. DNS record for the federation service name ----------------------
$hostLabel = $FederationServiceName.Split('.')[0]
$zoneName = $DomainName
$existingRecord = Get-DnsServerResourceRecord -ZoneName $zoneName -Name $hostLabel -RRType A -ErrorAction SilentlyContinue
if ($existingRecord) {
    Write-Output "DNS A record for '$FederationServiceName' already exists - skipping."
}
else {
    Write-Output "Creating DNS A record '$FederationServiceName' -> $Adfs01PrivateIp..."
    Add-DnsServerResourceRecordA -ZoneName $zoneName -Name $hostLabel -IPv4Address $Adfs01PrivateIp
    Write-Output "DNS record created. Note: this points at adfs-01 only - there's no load balancer in this lab, so it is not a highly-available configuration."
}

# --- 3. Bootstrap-certificate handoff share -----------------------------
$sharePath = 'C:\AdfsBootstrap'
$shareName = 'AdfsBootstrap$'

if (-not (Test-Path $sharePath)) {
    New-Item -Path $sharePath -ItemType Directory | Out-Null
}

$existingShare = Get-SmbShare -Name $shareName -ErrorAction SilentlyContinue
if ($existingShare) {
    Write-Output "Share '$shareName' already exists - skipping creation."
}
else {
    Write-Output "Creating share '$shareName' (Domain Admins full control only)..."
    New-SmbShare -Name $shareName -Path $sharePath -FullAccess 'Domain Admins' | Out-Null
    # Lock down NTFS ACLs to match - only Domain Admins and SYSTEM.
    $acl = Get-Acl $sharePath
    $acl.SetAccessRuleProtection($true, $false)
    $domainAdmins = New-Object System.Security.Principal.NTAccount('Domain Admins')
    $systemAccount = New-Object System.Security.Principal.NTAccount('NT AUTHORITY\SYSTEM')
    $acl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule($domainAdmins, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')))
    $acl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule($systemAccount, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')))
    Set-Acl -Path $sharePath -AclObject $acl
    Write-Output "Share created and locked down."
}

Write-Output "ADFS prerequisites ready."
