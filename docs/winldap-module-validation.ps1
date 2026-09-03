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

Steps 0-4 below test the LOCAL-AGENT path (functions running in-process on the DC). See the "JEA
VARIANT" section after Step 4 for the equivalent remote-via-JEA test, which is the one that actually
answers whether a JEA virtual account/gMSA has sufficient ACLs on the NTDS registry hive - see
docs/winldap-implementation-notes.md, "Remaining unverified assumptions" #5.

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
# JEA VARIANT - repeat Steps 1-4 through a real JEA session instead of in-process, to test the
# actual open question: does the JEA run-as account (virtual account or gMSA) have sufficient
# ACLs to write to the NTDS registry hive? Run this from a SEPARATE machine (not this DC), once
# you've registered a JEA endpoint here per docsource/content.md, with Keyfactor.WinCert.LDAP
# installed alongside Keyfactor.WinCert.Common and added to the endpoint's RoleDefinitions.
# ============================================================================
<#
$cred = Get-Credential   # account in the JEA endpoint's RoleDefinitions
$jeaSession = New-PSSession -ComputerName '<this-dc>' -ConfigurationName '<your-jea-endpoint-name>' -Credential $cred

# Confirm identity/group-membership/JEA health first - this is usually the fastest way to see why
# a permission-related failure happened, if one does.
Invoke-Command -Session $jeaSession -ScriptBlock { Get-KeyfactorDiagnostics } -InformationAction Continue

# Re-run the same Add/Get/Remove sequence as Steps 1-4 above, but through the JEA session. Build a
# fresh PFX first, since the earlier $base64Cert's certificate may already have been removed above.
#
# IMPORTANT: Test-LdapsCertificateEligibility.ps1 runs INSIDE the JEA session, i.e. on the target DC
# ('<this-dc>'), and checks the certificate's Subject/SAN against THAT machine's own $env:COMPUTERNAME/
# $env:USERDNSDOMAIN - not this script's local machine. Set $targetDcFqdn to the actual target DC's
# FQDN below, or the Add call will fail eligibility with a FQDN-mismatch error that has nothing to do
# with the JEA/ACL question you're actually testing.
$targetDcFqdn = '<this-dc-fqdn>'   # e.g. 'dc01.contoso.com' - must match the DC behind $jeaSession
$jeaTestCert = New-SelfSignedCertificate -Subject "CN=$targetDcFqdn" `
    -DnsName @($targetDcFqdn) -KeyUsage KeyEncipherment, DigitalSignature `
    -CertStoreLocation "Cert:\LocalMachine\My" -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.1") `
    -KeyExportPolicy Exportable
$jeaPfxPassword = 'Test1234!'
$jeaBase64Cert = [System.Convert]::ToBase64String($jeaTestCert.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Pfx, $jeaPfxPassword))
Remove-Item "Cert:\LocalMachine\My\$($jeaTestCert.Thumbprint)" -Force

Invoke-Command -Session $jeaSession -ScriptBlock {
    param($Base64Cert, $Password)
    Add-KeyfactorLdapsCertificate -Base64Cert $Base64Cert -PrivateKeyPassword $Password -StoreName 'NTDS\My' -RestartService $false
} -ArgumentList $jeaBase64Cert, $jeaPfxPassword

Invoke-Command -Session $jeaSession -ScriptBlock { Get-KeyfactorLdapCertificates -StoreName 'NTDS\My' }

Invoke-Command -Session $jeaSession -ScriptBlock {
    param($Thumbprint)
    Remove-KeyfactorLdapsCertificate -Thumbprint $Thumbprint -StoreName 'NTDS\My'
} -ArgumentList $jeaTestCert.Thumbprint

Remove-Item "Cert:\LocalMachine\My\$($jeaTestCert.Thumbprint)" -Force -ErrorAction SilentlyContinue
Remove-PSSession $jeaSession
#>

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
