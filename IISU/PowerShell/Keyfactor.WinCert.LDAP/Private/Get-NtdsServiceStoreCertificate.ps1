function Get-NtdsServiceStoreCertificate {
    <#
    .SYNOPSIS
    Enumerates certificates in a Windows service-specific certificate store (e.g. NTDS\My, the
    registry-backed store the LDAPS listener reads from).

    .DESCRIPTION
    UNVERIFIED - MUST BE LAB-VALIDATED. See Invoke-CertUtilNtdsStore.ps1 for the certutil argument
    shape this assumes. certutil's enumeration output is human-readable text, not JSON, so this
    parses per-certificate blocks delimited by the "================ Certificate N ================"
    header, which has been a stable certutil convention across many Windows versions - the exact
    field labels within a block (locale-dependent) are NOT relied upon here; only the SHA1 hash line
    is parsed, using a pattern anchored on "Cert Hash(sha1):" which may itself be localized on
    non-English systems (the same class of problem already hit and fixed for Get-CryptoProviders -
    see CHANGELOG). If this proves unreliable in testing, prefer reading the certificate LIST from
    this store and hydrating full metadata from the cross-referenced Personal store below, rather
    than trying to parse more fields out of certutil text.

    For each thumbprint found in the service store, full metadata (Subject/Issuer/NotAfter/
    HasPrivateKey/raw bytes) is hydrated from Cert:\LocalMachine\My when present there - this is the
    expected common case, since Add-KeyfactorLdapsCertificate always stages into Personal before
    writing into the service store. A thumbprint present in the service store but NOT found in
    Personal (e.g. placed there by some other process) is still returned, but with only Thumbprint
    populated and a warning emitted - full hydration for that case is not implemented pending lab
    validation of a reliable certutil export path.
    #>
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [string]$ServiceName,

        [Parameter(Mandatory = $true)]
        [string]$StoreName
    )

    $result = Invoke-CertUtilNtdsStore -Arguments @('-store', '-service', $ServiceName, $StoreName)

    if (-not $result.Started) {
        Write-Error "Unable to launch certutil.exe to enumerate the '$ServiceName\$StoreName' service store: $($result.StdErr)"
        return @()
    }

    # certutil returns a non-zero exit code (and "Local AD or Directory Service is currently
    # unavailable"-style text on some builds) when the store is genuinely empty - treat that as
    # zero results rather than a hard error, but surface the raw output for diagnosis either way.
    if ($result.ExitCode -ne 0 -and $result.StdOut -notmatch 'CertUtil: -store command completed successfully') {
        Write-Warning "certutil exited with code $($result.ExitCode) while enumerating '$ServiceName\$StoreName'. StdOut: $($result.StdOut) StdErr: $($result.StdErr)"
    }

    $thumbprints = @()
    foreach ($line in ($result.StdOut -split "`r?`n")) {
        if ($line -match 'Cert Hash\(sha1\)\s*:\s*([0-9a-fA-F ]+)') {
            $thumbprints += ($matches[1] -replace '\s', '').ToUpper()
        }
    }
    $thumbprints = $thumbprints | Select-Object -Unique

    if ($thumbprints.Count -eq 0) {
        Write-Information "No certificates found in the '$ServiceName\$StoreName' service store."
        return @()
    }

    $personalCerts = @{}
    try {
        Get-ChildItem -Path 'Cert:\LocalMachine\My' -ErrorAction Stop | ForEach-Object {
            $personalCerts[$_.Thumbprint.ToUpper()] = $_
        }
    }
    catch {
        Write-Warning "Unable to read Cert:\LocalMachine\My for metadata cross-reference: $_"
    }

    $items = @()
    foreach ($thumb in $thumbprints) {
        $cert = $personalCerts[$thumb]

        if ($null -eq $cert) {
            Write-Warning "Certificate '$thumb' exists in the '$ServiceName\$StoreName' service store but was not found in Cert:\LocalMachine\My - returning a partial record. This certificate may have been placed in the service store outside of Keyfactor's Add-KeyfactorLdapsCertificate flow."
            $items += [PSCustomObject]@{
                StoreName     = "$ServiceName\$StoreName"
                Certificate   = ""
                ExpiryDate    = ""
                Issuer        = ""
                Thumbprint    = $thumb
                HasPrivateKey = $false
                ProviderName  = ""
                Base64Data    = ""
            }
            continue
        }

        $items += [PSCustomObject]@{
            StoreName     = "$ServiceName\$StoreName"
            Certificate   = $cert.Subject
            ExpiryDate    = $cert.NotAfter
            Issuer        = $cert.Issuer
            Thumbprint    = $cert.Thumbprint
            HasPrivateKey = $cert.HasPrivateKey
            ProviderName  = Get-CertificateCSP $cert
            Base64Data    = [System.Convert]::ToBase64String($cert.RawData)
        }
    }

    return $items
}
