<#
WinLDAP — reset test state (NTDS registry store + Personal store) before a fresh test pass.

Run this INTERACTIVELY, section by section, on the lab Domain Controller as local Administrator.

THIS IS DESTRUCTIVE. It removes certificates from
HKLM:\SOFTWARE\Microsoft\Cryptography\Services\NTDS\SystemCertificates\My\Certificates and from
Cert:\LocalMachine\My. It does NOT try to guess which entries are "test certificates" - it only
removes whatever thumbprints you explicitly list in Step 2, after reviewing the enumeration in
Step 1. Cert:\LocalMachine\My is a general-purpose store shared with other services on the DC
(WinRM HTTPS, RDP, a DC's own default self-signed LDAPS cert if one was auto-generated at
promotion, etc.) - do not remove anything you don't recognize as your own test artifact.

After cleanup, re-run Inventory (either through Command, or directly via
Get-KeyfactorLdapCertificates -StoreName 'NTDS\My') so Command's view of the store resyncs to the
now-empty state before you start your next Add/renewal test.
#>

$ntdsMyCertsPath = "HKLM:\SOFTWARE\Microsoft\Cryptography\Services\NTDS\SystemCertificates\My\Certificates"

# ============================================================================
# STEP 1 - Enumerate what's currently in both stores. Review this output carefully before
# deciding what to remove in Step 2. Nothing is modified by this step.
# ============================================================================
Write-Host "`n=== NTDS\My service store ===" -ForegroundColor Yellow
Get-ChildItem $ntdsMyCertsPath -ErrorAction SilentlyContinue | ForEach-Object {
    $blob = (Get-ItemProperty -Path $_.PSPath -Name 'Blob' -ErrorAction SilentlyContinue).Blob
    if ($blob) {
        $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2(, [byte[]]$blob)
        Write-Host "  Thumbprint=$($_.PSChildName)  Subject=$($cert.Subject)  NotBefore=$($cert.NotBefore)  NotAfter=$($cert.NotAfter)"
    }
}

Write-Host "`n=== Cert:\LocalMachine\My (Personal store) ===" -ForegroundColor Yellow
Get-ChildItem "Cert:\LocalMachine\My" | ForEach-Object {
    Write-Host "  Thumbprint=$($_.Thumbprint)  Subject=$($_.Subject)  NotBefore=$($_.NotBefore)  NotAfter=$($_.NotAfter)  HasPrivateKey=$($_.HasPrivateKey)"
}

# ============================================================================
# STEP 2 - Fill in ONLY the thumbprints you've confirmed above are your own test certificates,
# then run the removal loop below. Nothing is removed until you populate this list and re-run
# from here down.
# ============================================================================
$ThumbprintsToRemove = @(
    # "PUT-A-THUMBPRINT-HERE",
    # "PUT-ANOTHER-THUMBPRINT-HERE"
)

if ($ThumbprintsToRemove.Count -eq 0) {
    Write-Host "`nNo thumbprints listed in `$ThumbprintsToRemove - nothing will be removed. Populate the list above first, using the Step 1 output." -ForegroundColor Red
}
else {
    foreach ($thumb in $ThumbprintsToRemove) {
        $clean = ($thumb -replace '[^a-fA-F0-9]', '').ToUpper()

        $regPath = Join-Path $ntdsMyCertsPath $clean
        if (Test-Path $regPath) {
            Remove-Item -Path $regPath -Recurse -Force
            Write-Host "Removed '$clean' from NTDS\My registry store." -ForegroundColor Green
        } else {
            Write-Host "'$clean' not found in NTDS\My registry store (already absent)." -ForegroundColor DarkGray
        }

        $certPath = "Cert:\LocalMachine\My\$clean"
        if (Test-Path $certPath) {
            Remove-Item -Path $certPath -Force
            Write-Host "Removed '$clean' from Cert:\LocalMachine\My." -ForegroundColor Green
        } else {
            Write-Host "'$clean' not found in Cert:\LocalMachine\My (already absent)." -ForegroundColor DarkGray
        }
    }
}

# ============================================================================
# STEP 3 - Re-enumerate to confirm the expected end state.
# ============================================================================
Write-Host "`n=== NTDS\My service store (after cleanup) ===" -ForegroundColor Yellow
Get-ChildItem $ntdsMyCertsPath -ErrorAction SilentlyContinue | Select-Object -ExpandProperty PSChildName

Write-Host "`n=== Cert:\LocalMachine\My (after cleanup) ===" -ForegroundColor Yellow
Get-ChildItem "Cert:\LocalMachine\My" | Select-Object Thumbprint, Subject

# ============================================================================
# STEP 4 (optional) - If you want to rule out any residual Schannel/lsass caching of a removed
# certificate carrying over into your next test (see docs/winldap-implementation-notes.md,
# "Resolved during lab validation (2026-09-16)" - restarting alone did not matter for that specific
# bug, but it's still a reasonable clean-slate step before a fresh test pass).
# ============================================================================
# Restart-Service NTDS -Force

# ============================================================================
# STEP 5 - Resync Command's view of the store. Either run Inventory through Command's UI, or
# check directly from PowerShell first:
# ============================================================================
# Import-Module "$PSScriptRoot\..\IISU\PowerShell\Keyfactor.WinCert.LDAP\Keyfactor.WinCert.LDAP.psm1" -Force
# Get-KeyfactorLdapCertificates -StoreName 'NTDS\My'   # should return nothing after a clean reset
