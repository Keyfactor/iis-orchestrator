<#
WinLDAP module — end-to-end validation script (public PowerShell functions, no Command needed).

Run this INTERACTIVELY on a disposable/test Domain Controller, under Windows PowerShell 5.1
(powershell.exe, NOT pwsh) - that's the runtime IISU/PSHelper.cs actually launches for local
execution, and X509Certificate2 private-key behavior differs between .NET Framework and .NET Core.

Unlike docs/winldap-ntds-validation.ps1 (which pokes the raw NTDS registry mechanism directly),
this script calls the actual public functions Management.cs/Inventory.cs invoke:
Add-KeyfactorLdapsCertificate, Get-KeyfactorLdapCertificates, Remove-KeyfactorLdapsCertificate -
including the eligibility check, PFX load, Personal-store staging, and the NTDS write/read/delete.
This is the closest you can get to testing "the extension" without wiring it into Command.

Prerequisites: run elevated (local Administrator, or whatever account the Universal Orchestrator's
Windows service will run as) directly on the test DC.
#>

Import-Module "$PSScriptRoot\..\IISU\PowerShell\Keyfactor.WinCert.LDAP\Keyfactor.WinCert.LDAP.psm1" -Force

# ============================================================================
# STEP 0 - Create a disposable test certificate that will actually pass
# Test-LdapsCertificateEligibility.ps1 (Server-Auth EKU + this DC's own FQDN as CN/SAN).
# ============================================================================
$testCert = New-SelfSignedCertificate `
    -Subject "CN=$($env:COMPUTERNAME).$($env:USERDNSDOMAIN)" `
    -DnsName @("$($env:COMPUTERNAME).$($env:USERDNSDOMAIN)") `
    -KeyUsage KeyEncipherment, DigitalSignature `
    -CertStoreLocation "Cert:\LocalMachine\My" `
    -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.1") `
    -KeyExportPolicy Exportable

Write-Host "Created test certificate with thumbprint: $($testCert.Thumbprint)" -ForegroundColor Cyan

# Package as a base64 PFX, exactly what Command would hand to Management.cs in JobCertificate.Contents.
$pfxPassword = 'Test1234!'
$pfxBytes = $testCert.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Pfx, $pfxPassword)
$base64Cert = [System.Convert]::ToBase64String($pfxBytes)

# Remove it from Personal for now - Add-KeyfactorLdapsCertificate is responsible for staging it there.
Remove-Item "Cert:\LocalMachine\My\$($testCert.Thumbprint)" -Force

# ============================================================================
# STEP 1 - Add. Exercises: eligibility check -> PFX load -> stage into Personal -> re-read from
# Personal -> write into NTDS\My -> optional restart.
# ============================================================================
Write-Host "`n=== STEP 1: Add-KeyfactorLdapsCertificate ===" -ForegroundColor Yellow
# New-KeyfactorResult (Keyfactor.WinCert.Common) returns a plain PSCustomObject, not a JSON string -
# that's also what ResultObject.FromPSResults reads directly from the PSObject pipeline in
# Management.cs, so no ConvertFrom-Json is needed (or valid) here.
$addResult = Add-KeyfactorLdapsCertificate `
    -Base64Cert $base64Cert `
    -PrivateKeyPassword $pfxPassword `
    -StoreName 'NTDS\My' `
    -RestartService $false
$addResult | Format-List
if ($addResult.Status -ne 'Success') {
    Write-Host "Add failed - stop here and inspect the error above before continuing." -ForegroundColor Red
}

# ============================================================================
# STEP 2 - Inventory. Exercises: registry enumeration -> X509Certificate2 hydration ->
# Personal-store diagnostic cross-check.
# ============================================================================
Write-Host "`n=== STEP 2: Get-KeyfactorLdapCertificates ===" -ForegroundColor Yellow
Get-KeyfactorLdapCertificates -StoreName 'NTDS\My'

# ============================================================================
# STEP 3 - Confirm from a SEPARATE machine (not this DC) what LDAPS (port 636) presents, and
# whether HasPrivateKey/thumbprint above line up with what you expect, before removing anything:
#
#   openssl s_client -connect <this-dc-fqdn>:636 -showcerts </dev/null 2>/dev/null | openssl x509 -noout -fingerprint -sha1
# ============================================================================
Write-Host "`nTest certificate thumbprint for comparison: $($testCert.Thumbprint)" -ForegroundColor Cyan

# ============================================================================
# STEP 4 - Remove. Only run once you're done - if this cert became the one LDAPS is actively
# presenting, removing it carries real operational risk (see docsource/winldap.md).
# ============================================================================
Write-Host "`n=== STEP 4: Remove-KeyfactorLdapsCertificate (run when ready) ===" -ForegroundColor Yellow
Write-Host "  Remove-KeyfactorLdapsCertificate -Thumbprint '$($testCert.Thumbprint)' -StoreName 'NTDS\My'"
# Remove-KeyfactorLdapsCertificate -Thumbprint $testCert.Thumbprint -StoreName 'NTDS\My'

# ============================================================================
# CLEANUP - removes the Personal-store copy and the temp cert (Remove-KeyfactorLdapsCertificate
# above only removes the NTDS-store copy, by design - see docsource/winldap.md).
# ============================================================================
function Remove-WinLdapModuleTestArtifacts {
    param($Thumbprint)
    Remove-Item "Cert:\LocalMachine\My\$Thumbprint" -Force -ErrorAction SilentlyContinue
    Write-Host "Removed '$Thumbprint' from Cert:\LocalMachine\My." -ForegroundColor Green
}

Write-Host "`nWhen finished, run:" -ForegroundColor Green
Write-Host "  Remove-WinLdapModuleTestArtifacts -Thumbprint '$($testCert.Thumbprint)'" -ForegroundColor Green
