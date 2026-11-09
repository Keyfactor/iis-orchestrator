#Requires -RunAsAdministrator
<#
    Runs on sql-01, which is provisioned from an Azure Marketplace SQL
    Server image (SQL Server is already installed there - the free
    Developer edition SKU this lab uses needs no marketplace terms
    acceptance, see docs/01-prerequisites.md). This script just
    confirms the instance is reachable and makes sure the
    SuperSocketNetLib\Certificate registry value WinSql needs to write to
    exists (even if empty), and opens the Windows Firewall for 1433
    intra-VNet only - the NSG already blocks it from the internet, but the
    marketplace image's local firewall defaults shouldn't be assumed.
#>

$ErrorActionPreference = 'Stop'

Write-Output "Checking SQL Server service..."
$sqlService = Get-Service -Name 'MSSQLSERVER' -ErrorAction SilentlyContinue
if (-not $sqlService) {
    throw "MSSQLSERVER service not found - this VM does not appear to be running the expected SQL Server marketplace image."
}
if ($sqlService.Status -ne 'Running') {
    Start-Service -Name 'MSSQLSERVER'
}

$instance = 'MSSQLSERVER'
$regPath = "HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL"
$fullInstanceKey = (Get-ItemProperty -Path $regPath -Name $instance -ErrorAction SilentlyContinue).$instance
if ($fullInstanceKey) {
    $superSocketPath = "HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\$fullInstanceKey\MSSQLServer\SuperSocketNetLib"
    if (Test-Path $superSocketPath) {
        Write-Output "SuperSocketNetLib registry path confirmed at $superSocketPath - WinSql has somewhere to write the bound certificate's thumbprint."
    }
}

$existingRule = Get-NetFirewallRule -DisplayName 'SQL Server (WinCert Lab)' -ErrorAction SilentlyContinue
if (-not $existingRule) {
    Write-Output "Opening TCP 1433 to the VNet only..."
    New-NetFirewallRule -DisplayName 'SQL Server (WinCert Lab)' -Direction Inbound -Protocol TCP -LocalPort 1433 -RemoteAddress LocalSubnet -Action Allow | Out-Null
}

Write-Output "SQL Server is ready."
