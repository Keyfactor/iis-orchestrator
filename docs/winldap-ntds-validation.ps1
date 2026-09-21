<#
WinLDAP / NTDS service store — lab validation script.

Run this INTERACTIVELY, step by step (not as a single unattended script), on a disposable/test
Domain Controller only. It exercises the exact registry read/write/delete mechanism the
Keyfactor.WinCert.LDAP PowerShell module uses (see Set/Get/Remove-NtdsServiceStoreCertificate.ps1),
WITHOUT going through the orchestrator, so you can confirm the remaining unverified assumptions
before trusting it.

certutil.exe is NOT used here - an earlier version of this script tried
`certutil -addstore -service NTDS My`, which fails with ERROR_INVALID_PARAMETER because certutil's
-addstore/-delstore verbs never supported -service in the first place (confirmed via
`certutil -addstore -?`/`-delstore -?`; only the read-only -store verb documents it). The NTDS\My
store is registry-backed, and this script now writes/reads/deletes that registry data directly,
matching what the module does.

See docs/winldap-implementation-notes.md for the full list of assumptions this is validating and
why each one matters.

It intentionally does NOT delete anything by default - each step tells you what to inspect. Only
run the "cleanup" section at the end once you're done, and only against a test certificate you
created for this purpose.

Prerequisites: run elevated (local Administrator) directly on the test DC for Steps 0-4 and 6-8.
Step 5 covers remote WinRM/JEA, which WinLDAP now supports (see docsource/winldap.md) - the single
most important open question for that path is whether a JEA virtual account/gMSA actually has
write access to the NTDS registry hive, which Step 5 tests directly. Run it before relying on JEA
for WinLDAP in production.
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
# STEP 1 - Confirm the NTDS service store registry path is reachable, and note whatever it
# currently contains BEFORE you add anything.
# Keyfactor.WinCert.LDAP\Private\Get-NtdsServiceStoreCertificate.ps1 reads exactly this path.
# ============================================================================
$ntdsMyCertsPath = "HKLM:\SOFTWARE\Microsoft\Cryptography\Services\NTDS\SystemCertificates\My\Certificates"

Write-Host "`n=== STEP 1: enumerate the current NTDS\My service store ===" -ForegroundColor Yellow
Get-ChildItem $ntdsMyCertsPath -ErrorAction SilentlyContinue | ForEach-Object {
    $blob = (Get-ItemProperty -Path $_.PSPath -Name 'Blob' -ErrorAction SilentlyContinue).Blob
    if ($blob) {
        $existing = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2(, [byte[]]$blob)
        Write-Host "  $($_.PSChildName)  Subject=$($existing.Subject)  HasPrivateKey=$($existing.HasPrivateKey)"
    }
}

# ============================================================================
# STEP 2 - Confirm the registry write mechanism actually writes the test cert.
# Keyfactor.WinCert.LDAP\Private\Set-NtdsServiceStoreCertificate.ps1 does exactly this: export the
# certificate as a SerializedCert blob and write it as the "Blob" value under a subkey named by the
# certificate's uppercase SHA1 thumbprint.
# ============================================================================
Write-Host "`n=== STEP 2: add the test cert to the NTDS\My service store ===" -ForegroundColor Yellow
$testCertRegPath = Join-Path $ntdsMyCertsPath $testCert.Thumbprint.ToUpper()
$testCertBlob = $testCert.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::SerializedCert)

New-Item -Path $testCertRegPath -Force | Out-Null
Set-ItemProperty -Path $testCertRegPath -Name 'Blob' -Value $testCertBlob -Type Binary
Write-Host "Wrote $($testCertBlob.Length) bytes to $testCertRegPath\Blob" -ForegroundColor Yellow

Write-Host "`n=== STEP 2b: re-enumerate to confirm it's there ===" -ForegroundColor Yellow
Get-ChildItem $ntdsMyCertsPath -ErrorAction SilentlyContinue | Select-Object -ExpandProperty PSChildName

# ============================================================================
# STEP 3 - THE MOST IMPORTANT CHECK: does the NTDS-store copy resolve HasPrivateKey = true?
# Set-NtdsServiceStoreCertificate assumes Windows resolves the private-key association via
# CAPI/CNG machine-key-container matching, independent of which logical store lists the cert -
# i.e. it does NOT re-import the PFX into the service store, only a SerializedCert export
# (which carries a reference to the key container, not the key material itself). If this
# assumption is wrong, HasPrivateKey below will show False and the design needs to change.
# ============================================================================
Write-Host "`n=== STEP 3: confirm private key association in the service store (CRITICAL) ===" -ForegroundColor Red
$readBackBlob = (Get-ItemProperty -Path $testCertRegPath -Name 'Blob').Blob
$readBackCert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2(, [byte[]]$readBackBlob)
Write-Host "Thumbprint:    $($readBackCert.Thumbprint)" -ForegroundColor Red
Write-Host "HasPrivateKey: $($readBackCert.HasPrivateKey)" -ForegroundColor Red

# ============================================================================
# STEP 4 - Confirm removal.
# Keyfactor.WinCert.LDAP\Private\Remove-NtdsServiceStoreCertificate.ps1 does exactly this: delete
# the thumbprint subkey.
# Do NOT run this yet if you want to continue to Steps 5-7 first (removal is the last thing to test).
# ============================================================================
Write-Host "`n=== STEP 4: (holding off - see bottom of script for the Remove-Item command) ===" -ForegroundColor Yellow

# ============================================================================
# STEP 5 - THE CRUX QUESTION FOR REMOTE/JEA: does a JEA virtual account or gMSA actually have
# write access to the NTDS registry hive? This has NOT been lab-validated as of this writing (see
# docs/winldap-implementation-notes.md) - run this from a SEPARATE machine (not this DC) once you
# have registered a JEA endpoint on this DC per docsource/content.md's setup steps, with
# Keyfactor.WinCert.LDAP installed alongside Keyfactor.WinCert.Common under
# C:\Program Files\WindowsPowerShell\Modules\ and Keyfactor.WinCert.LDAP added to the endpoint's
# RoleDefinitions.
#
# Replace <this-dc> and <your-jea-endpoint-name> below, then run interactively.
# ============================================================================
Write-Host "`n=== STEP 5: validate JEA registry-write access (run from a separate machine) ===" -ForegroundColor Red
<#
$cred = Get-Credential   # account that is a member of the JEA endpoint's RoleDefinitions
$jeaSession = New-PSSession -ComputerName '<this-dc>' -ConfigurationName '<your-jea-endpoint-name>' -Credential $cred

# Confirm identity, group memberships, and JEA/WinRM health first.
Invoke-Command -Session $jeaSession -ScriptBlock { Get-KeyfactorDiagnostics } -InformationAction Continue

# THE ACTUAL TEST: attempt a real write to the NTDS registry hive through the JEA session, using
# the same mechanism Set-NtdsServiceStoreCertificate.ps1 uses. If the run-as account's ACLs are
# insufficient, this will fail here with an access-denied error - that's the answer to the open
# question, not a bug in the script.
Invoke-Command -Session $jeaSession -ScriptBlock {
    param($Thumbprint, $Blob)
    $regPath = "HKLM:\SOFTWARE\Microsoft\Cryptography\Services\NTDS\SystemCertificates\My\Certificates\$Thumbprint"
    New-Item -Path $regPath -Force | Out-Null
    Set-ItemProperty -Path $regPath -Name 'Blob' -Value $Blob -Type Binary
    Write-Host "JEA session successfully wrote to $regPath"
} -ArgumentList $testCert.Thumbprint.ToUpper(), $testCertBlob

# Read it back through the session to confirm HasPrivateKey survives the round trip remotely too.
Invoke-Command -Session $jeaSession -ScriptBlock {
    param($Thumbprint)
    $regPath = "HKLM:\SOFTWARE\Microsoft\Cryptography\Services\NTDS\SystemCertificates\My\Certificates\$Thumbprint"
    $blob = (Get-ItemProperty -Path $regPath -Name 'Blob').Blob
    $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2(, [byte[]]$blob)
    Write-Host "HasPrivateKey (read back through JEA session): $($cert.HasPrivateKey)"
}

# Also exercise the actual module functions through the session, not just raw registry access:
Invoke-Command -Session $jeaSession -ScriptBlock { Get-KeyfactorLdapCertificates -StoreName 'NTDS\My' }

Remove-PSSession $jeaSession
#>

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
# Remove-Item -Path (Join-Path $ntdsMyCertsPath $testCert.Thumbprint.ToUpper()) -Force -Recurse

# ============================================================================
# CLEANUP - run this once you're done, to remove all traces of the test certificate.
# ============================================================================
function Remove-WinLdapTestArtifacts {
    param($Thumbprint, $CerPath)

    Write-Host "`n=== CLEANUP ===" -ForegroundColor Green
    $regPath = "HKLM:\SOFTWARE\Microsoft\Cryptography\Services\NTDS\SystemCertificates\My\Certificates\$($Thumbprint.ToUpper())"
    Remove-Item -Path $regPath -Force -Recurse -ErrorAction SilentlyContinue
    Remove-Item "Cert:\LocalMachine\My\$Thumbprint" -Force -ErrorAction SilentlyContinue
    Remove-Item $CerPath -Force -ErrorAction SilentlyContinue
    Write-Host "Removed test certificate ($Thumbprint) from NTDS\My, LocalMachine\My, and deleted the temp .cer file." -ForegroundColor Green
}

Write-Host "`nWhen finished, run:" -ForegroundColor Green
Write-Host "  Remove-WinLdapTestArtifacts -Thumbprint '$($testCert.Thumbprint)' -CerPath '$certPath'" -ForegroundColor Green
