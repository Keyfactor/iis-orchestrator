#Requires -RunAsAdministrator
<#
    Runs on iis-01. Installs IIS plus a self-signed certificate bound to
    the Default Web Site over HTTPS, so WinIIS has an existing binding to
    inventory/rotate on day one, and WinNetSH/WinCert have a real
    certificate sitting in the machine store to exercise as well.

    Idempotent: skips role install if already present, skips cert/binding
    creation if a binding on 443 already exists.
#>
param(
    [Parameter(Mandatory = $true)]
    [string] $VmFqdn
)

$ErrorActionPreference = 'Stop'

Write-Output "Installing IIS role..."
Install-WindowsFeature -Name Web-Server, Web-Mgmt-Console -IncludeManagementTools | Out-Null

Import-Module WebAdministration

$existingBinding = Get-WebBinding -Name 'Default Web Site' -Protocol https -ErrorAction SilentlyContinue
if ($existingBinding) {
    Write-Output "Default Web Site already has an HTTPS binding - skipping certificate/binding setup."
    exit 0
}

Write-Output "Generating a self-signed certificate for $VmFqdn..."
$cert = New-SelfSignedCertificate `
    -DnsName $VmFqdn `
    -CertStoreLocation 'Cert:\LocalMachine\My' `
    -KeyExportPolicy Exportable `
    -NotAfter (Get-Date).AddYears(2)

Write-Output "Binding certificate $($cert.Thumbprint) to Default Web Site on 443..."
New-WebBinding -Name 'Default Web Site' -Protocol https -Port 443 -IPAddress '*'
(Get-WebBinding -Name 'Default Web Site' -Protocol https).AddSslCertificate($cert.Thumbprint, 'My')

Write-Output "IIS setup complete."
