# Keyfactor.WinCert.Common.psm1
#
# Static explicit dot-sourcing is used instead of Get-ChildItem/Select-Object discovery.
# This avoids a dependency on proxy-restricted cmdlets during JEA session initialization.
# (In RestrictedRemoteServer, Select-Object -ExpandProperty is not available in the proxy.)

# Private helpers must be loaded before the public functions that call them.
. "$PSScriptRoot\Private\Get-CertificateSAN.ps1"
. "$PSScriptRoot\Private\Get-CertificateCSP.ps1"
. "$PSScriptRoot\Private\Get-CryptoProviders.ps1"
. "$PSScriptRoot\Private\Test-CryptoServiceProvider.ps1"
. "$PSScriptRoot\Private\Validate-CryptoProvider.ps1"
. "$PSScriptRoot\Private\Convert-DNSSubject.ps1"

# Public functions
. "$PSScriptRoot\Public\New-KeyfactorResult.ps1"
. "$PSScriptRoot\Public\Get-KeyfactorCertificates.ps1"
. "$PSScriptRoot\Public\Add-KeyfactorCertificate.ps1"
. "$PSScriptRoot\Public\Remove-KeyfactorCertificate.ps1"
. "$PSScriptRoot\Public\New-KeyfactorODKGEnrollment.ps1"

# Export only public functions for non-JEA use.
# In JEA sessions, VisibleFunctions in the .psrc is the actual access control mechanism.
Export-ModuleMember -Function @(
    # Public functions
    'New-KeyfactorResult',
    'Get-KeyfactorCertificates',
    'Add-KeyfactorCertificate',
    'Remove-KeyfactorCertificate',
    'New-KeyfactorODKGEnrollment',
    # Shared certificate inspection utilities — exported so other modules (e.g. IIS) can call them
    'Get-CertificateCSP',
    'Get-CertificateSAN'
)
