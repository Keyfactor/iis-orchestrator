#Requires -RunAsAdministrator
<#
    Runs on dev-01. Installs everything needed to clone this repo, build
    it, and debug the Universal Orchestrator + this extension against the
    other lab VMs over WinRM: Chocolatey itself, Visual Studio Community
    (with the .NET desktop workload, which covers this repo's .NET SDK
    needs), Git, and PowerShell 7.

    Deliberately does NOT install or configure the Universal Orchestrator
    service or Keyfactor Command - that part is manual, per the user's own
    decision (see azure-test-lab/README.md).

    Idempotent: each choco install is itself idempotent (no-ops if the
    package is already at the requested version).
#>

$ErrorActionPreference = 'Stop'

if (-not (Get-Command choco.exe -ErrorAction SilentlyContinue)) {
    Write-Output "Installing Chocolatey..."
    Set-ExecutionPolicy Bypass -Scope Process -Force
    [System.Net.ServicePointManager]::SecurityProtocol = [System.Net.ServicePointManager]::SecurityProtocol -bor 3072
    Invoke-Expression ((New-Object System.Net.WebClient).DownloadString('https://community.chocolatey.org/install.ps1'))
    $env:Path = "$env:Path;C:\ProgramData\chocolatey\bin"
}

Write-Output "Installing git..."
choco install git -y --no-progress

Write-Output "Installing PowerShell 7..."
choco install powershell-core -y --no-progress

Write-Output "Installing Visual Studio 2022 Community (.NET desktop workload)..."
choco install visualstudio2022community -y --no-progress --package-parameters "--add Microsoft.VisualStudio.Workload.ManagedDesktop --add Microsoft.VisualStudio.Workload.NetWeb --includeRecommended"

Write-Output "Installing the .NET SDK (matches this repo's net8.0/net10.0 targets)..."
choco install dotnet-sdk -y --no-progress

Write-Output "Dev box prerequisites installed. Visual Studio may need a reboot to finish registering - reboot the VM if it doesn't launch cleanly on first try."
