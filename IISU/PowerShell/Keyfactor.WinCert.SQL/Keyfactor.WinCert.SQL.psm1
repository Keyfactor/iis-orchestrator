# Keyfactor.WinCert.SQL.psm1
#
# Static explicit dot-sourcing is used instead of Get-ChildItem/Select-Object discovery.
# This avoids a dependency on proxy-restricted cmdlets during JEA session initialization.

# Load Keyfactor.WinCert.Common if not already loaded (non-JEA local sessions).
# In JEA sessions the .psrc ModulesToImport loads both modules before any function is called.
if (-not (Get-Command 'New-KeyfactorResult' -ErrorAction SilentlyContinue)) {
    $commonModulePath = Join-Path $PSScriptRoot '..\Keyfactor.WinCert.Common\Keyfactor.WinCert.Common.psm1'
    if (Test-Path $commonModulePath) { Import-Module $commonModulePath -Force }
}

# Private helpers must be loaded before the public functions that call them.
. "$PSScriptRoot\Private\Get-KeyfactorSQLServiceName.ps1"
. "$PSScriptRoot\Private\Get-KeyfactorSQLServiceUser.ps1"
. "$PSScriptRoot\Private\Set-KeyfactorSQLCertificateBinding.ps1"

# Public functions
. "$PSScriptRoot\Public\Get-KeyfactorSQLInventory.ps1"
. "$PSScriptRoot\Public\New-KeyfactorSQLBinding.ps1"
. "$PSScriptRoot\Public\Remove-KeyfactorSQLCertificate.ps1"

# Export only public functions for non-JEA use.
# In JEA sessions, VisibleFunctions in the .psrc is the actual access control mechanism.
Export-ModuleMember -Function @(
    'Get-KeyfactorSQLInventory',
    'New-KeyfactorSQLBinding',
    'Remove-KeyfactorSQLCertificate'
)
