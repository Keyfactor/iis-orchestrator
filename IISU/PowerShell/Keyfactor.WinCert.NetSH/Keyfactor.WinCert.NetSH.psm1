# Keyfactor.WinCert.NetSH.psm1
#
# Static explicit dot-sourcing is used instead of Get-ChildItem/Select-Object discovery.
# This avoids a dependency on proxy-restricted cmdlets during JEA session initialization.
# (In RestrictedRemoteServer, Select-Object -ExpandProperty is not available in the proxy.)

# Ensure Keyfactor.WinCert.Common is available (provides New-KeyfactorResult,
# Get-CertificateCSP, Get-CertificateSAN, etc.).
# In JEA sessions the .psrc lists both modules for import.
# In local non-JEA sessions PSHelper imports modules alphabetically, so Common loads first.
# This block is a fallback for standalone / development use.
if (-not (Get-Command 'New-KeyfactorResult' -ErrorAction SilentlyContinue)) {
    $commonModulePath = Join-Path $PSScriptRoot '..\Keyfactor.WinCert.Common\Keyfactor.WinCert.Common.psm1'
    if (Test-Path $commonModulePath) {
        Import-Module $commonModulePath -Force
    }
}

# Private helpers - load in dependency order (no-dependency functions first)
. "$PSScriptRoot\Private\ConvertFrom-NetShSslCertOutput.ps1"
. "$PSScriptRoot\Private\Get-NetShSslCertBinding.ps1"
. "$PSScriptRoot\Private\Set-NetShSslCertBinding.ps1"

# Public functions
. "$PSScriptRoot\Public\Get-KeyfactorNetSHBoundCertificates.ps1"
. "$PSScriptRoot\Public\New-KeyfactorNetSHBinding.ps1"
. "$PSScriptRoot\Public\Remove-KeyfactorNetSHBinding.ps1"
. "$PSScriptRoot\Public\Remove-KeyfactorNetSHCertificateIfUnused.ps1"

# Export only public functions for non-JEA use.
# In JEA sessions, VisibleFunctions in the .psrc is the actual access control mechanism.
Export-ModuleMember -Function @(
    'Get-KeyfactorNetSHBoundCertificates',
    'New-KeyfactorNetSHBinding',
    'Remove-KeyfactorNetSHBinding',
    'Remove-KeyfactorNetSHCertificateIfUnused'
)
