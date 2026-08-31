<#
WinLDAP / NTDS service store — lab validation script.

Run this INTERACTIVELY, step by step (not as a single unattended script), on a disposable/test
Domain Controller only. It exercises the exact certutil syntax and store-write behavior the
Keyfactor.WinCert.LDAP PowerShell module assumes, WITHOUT going through the orchestrator, so you can
confirm or correct the assumptions documented in that module before trusting it.

See docs/winldap-implementation-notes.md for the full list of assumptions this is validating and
why each one matters.

It intentionally does NOT delete anything by default - each step tells you what to inspect. Only
run the "cleanup" section at the end once you're done, and only against a test certificate you
created for this purpose.

Prerequisites: run elevated (local Administrator) directly on the test DC, either interactively or
via a real remote WinRM/JEA session if that's the connectivity model you want to validate.
#>

# ============================================================================
# STEP 0 - Create a disposable self-signed test certificate for this session.
# Replace the DnsName/Subject with this DC's real FQDN if you want the eligibility-check-relevant
# fields to be realistic; a mismatched name is still fine for steps 1-5 below (they don't call the
# Keyfactor eligibility check, only the raw certutil mechanics it depends on).
# ============================================================================
$testCert = New-SelfSignedCertificate `
    -Subject "CN=$($env:COMPUTERNAME).$($env:USERDNSDOMAIN)" `
    -DnsName @("$($env:COMPUTERNAME).$($env:USERDNSDOMAIN)") `
    -KeyUsage KeyEncipherment, DigitalSignature `
    -CertStoreLocation "Cert:\LocalMachine\My" `
    -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.1") `
    -KeyExportPolicy Exportable

Write-Host "Created test certificate with thumbprint: $($testCert.Thumbprint)" -ForegroundColor Cyan
Write-Host "This is now staged in Cert:\LocalMachine\My, matching Add-KeyfactorLdapsCertificate's staging step." -ForegroundColor Cyan

$certPath = Join-Path $env:TEMP "winldap-test.cer"
[System.IO.File]::WriteAllBytes($certPath, $testCert.RawData)
Write-Host "Exported public cert bytes to: $certPath" -ForegroundColor Cyan

# ============================================================================
# STEP 1 - Confirm the NTDS service store is even reachable via certutil -store -service.
# Keyfactor.WinCert.LDAP\Private\Get-NtdsServiceStoreCertificate.ps1 assumes this exact syntax.
# Note whatever the store currently contains BEFORE you add anything.
# ============================================================================
Write-Host "`n=== STEP 1: enumerate the current NTDS\My service store ===" -ForegroundColor Yellow
certutil -store -service NTDS My

# ============================================================================
# STEP 2 - Confirm the -addstore -service syntax actually writes the test cert.
# Keyfactor.WinCert.LDAP\Private\Set-NtdsServiceStoreCertificate.ps1 assumes this exact syntax.
# ============================================================================
Write-Host "`n=== STEP 2: add the test cert to the NTDS\My service store ===" -ForegroundColor Yellow
certutil -f -addstore -service NTDS My $certPath
Write-Host "`nExit code: $LASTEXITCODE (0 = success expected)" -ForegroundColor Yellow

Write-Host "`n=== STEP 2b: re-enumerate to confirm it's there ===" -ForegroundColor Yellow
certutil -store -service NTDS My

# ============================================================================
# STEP 3 - THE MOST IMPORTANT CHECK: does the NTDS-store copy resolve HasPrivateKey = true?
# Add-KeyfactorLdapsCertificate assumes Windows resolves the private-key association via
# CAPI/CNG machine-key-container matching, independent of which logical store lists the cert -
# i.e. it does NOT re-import the PFX into the service store, only the public certificate. If this
# assumption is wrong, HasPrivateKey below will show False and the design needs to change to
# import a PFX (not just a .cer) into the service store instead.
# ============================================================================
Write-Host "`n=== STEP 3: confirm private key association in the service store (CRITICAL) ===" -ForegroundColor Red
certutil -store -service NTDS My $testCert.Thumbprint

# Look for a line indicating the private key / key container in the output above.
# Also check via the raw registry to see what's actually stored:
Write-Host "`nRaw registry check:" -ForegroundColor Red
Get-ChildItem "HKLM:\SOFTWARE\Microsoft\Cryptography\Services\NTDS\SystemCertificates\My\Certificates" -ErrorAction SilentlyContinue

# ============================================================================
# STEP 4 - Confirm removal syntax.
# Keyfactor.WinCert.LDAP\Private\Remove-NtdsServiceStoreCertificate.ps1 assumes this exact syntax.
# Do NOT run this yet if you want to continue to Steps 5-7 first (removal is the last thing to test).
# ============================================================================
Write-Host "`n=== STEP 4: (holding off - see bottom of script for the delstore command) ===" -ForegroundColor Yellow

# ============================================================================
# STEP 5 - Permissions check: was Step 2 run as local Administrator, or as a lower-privileged
# JEA virtual account? If you have a JEA endpoint configured (per Keyfactor.WinCert.LDAP.psrc),
# repeat Steps 1-3 by invoking through that JEA session instead of an interactive elevated prompt,
# to confirm the JEA identity actually has write access to this registry hive. Example:
#
#   $session = New-PSSession -ComputerName <this-dc> -ConfigurationName <your-jea-endpoint-name>
#   Invoke-Command -Session $session -ScriptBlock {
#       certutil -store -service NTDS My
#   }
#
# If this fails with access-denied while the elevated/local-Administrator run above succeeded,
# that confirms the JEA-remote design needs a different RunAs identity or an explicit ACL grant on
# the registry hive before it can be relied upon.
# ============================================================================
Write-Host "`n=== STEP 5: see script comments - repeat via a real JEA session to check permissions ===" -ForegroundColor Yellow

# ============================================================================
# STEP 6 - LDAPS listener pickup behavior. From a SEPARATE machine (not this DC), check what
# certificate LDAPS (port 636) is currently presenting, before and after the steps above, and
# before/after restarting NTDS:
#
#   openssl s_client -connect <this-dc-fqdn>:636 -showcerts </dev/null 2>/dev/null | openssl x509 -noout -fingerprint -sha1
#
# Compare the returned thumbprint to $($testCert.Thumbprint) below at each stage:
#   (a) immediately after Step 2 (no restart)
#   (b) after waiting ~10-15 minutes (AD DS's own auto-detection window)
#   (c) after Restart-Service NTDS -Force (see Step 7)
# ============================================================================
Write-Host "`nTest certificate thumbprint for comparison: $($testCert.Thumbprint)" -ForegroundColor Cyan

# ============================================================================
# STEP 7 - Confirm Restart-Service NTDS is the correct/safe way to force pickup, and measure
# the actual downtime window. ONLY run this against a test DC, and only when you're ready - this
# briefly takes AD DS offline on this DC ("Restartable AD DS", supported but disruptive).
# ============================================================================
Write-Host "`n=== STEP 7: (manual) Restart-Service -Name NTDS -Force -- only run when ready ===" -ForegroundColor Red
# Restart-Service -Name NTDS -Force

# ============================================================================
# STEP 8 - Remove-of-active-certificate risk check. Once you've confirmed (via Step 6) that this
# test cert IS the one LDAPS is actively presenting, remove it and observe what happens to 636 -
# does it stop responding, fall back to another eligible cert, or keep serving the removed cert
# until a restart? Only do this on a test DC where an LDAPS outage is acceptable.
# ============================================================================
Write-Host "`n=== STEP 8: (manual, only when ready) remove the active cert and observe port 636 ===" -ForegroundColor Red
# certutil -delstore -service NTDS My $($testCert.Thumbprint)

# ============================================================================
# CLEANUP - run this once you're done, to remove all traces of the test certificate.
# ============================================================================
function Remove-WinLdapTestArtifacts {
    param($Thumbprint, $CerPath)

    Write-Host "`n=== CLEANUP ===" -ForegroundColor Green
    certutil -delstore -service NTDS My $Thumbprint 2>$null
    Remove-Item "Cert:\LocalMachine\My\$Thumbprint" -Force -ErrorAction SilentlyContinue
    Remove-Item $CerPath -Force -ErrorAction SilentlyContinue
    Write-Host "Removed test certificate ($Thumbprint) from NTDS\My, LocalMachine\My, and deleted the temp .cer file." -ForegroundColor Green
}

Write-Host "`nWhen finished, run:" -ForegroundColor Green
Write-Host "  Remove-WinLdapTestArtifacts -Thumbprint '$($testCert.Thumbprint)' -CerPath '$certPath'" -ForegroundColor Green
