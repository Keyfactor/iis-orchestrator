function Get-KeyfactorLdapCertificates {
    <#
    .SYNOPSIS
    Returns the certificates in the AD DS (NTDS) LDAPS service certificate store, as JSON.

    .DESCRIPTION
    Inventory is intentionally scoped to the NTDS service store ONLY - it is the single source of
    truth returned to Keyfactor Command. The Personal ("My") store, used internally as a staging
    location by Add-KeyfactorLdapsCertificate, is cross-checked here only as a best-effort
    diagnostic: a Write-Warning is emitted for any certificate that looks like an eligible LDAPS
    certificate sitting in Personal but not (yet, or no longer) present in the NTDS store. That
    warning is surfaced to the orchestrator job log; it is never added to the returned data, since
    Keyfactor Command's inventory reconciliation treats the returned list as authoritative for "what
    is in this store."

    .PARAMETER StoreName
    The service store to inventory, in "<ServiceName>\<StoreName>" form. Defaults to "NTDS\My", the
    only value this store type's integration-manifest.json entry currently allows.
    #>
    param (
        [Parameter(Mandatory = $false)]
        [string]$StoreName = "NTDS\My"
    )

    $parts = $StoreName -split '\\', 2
    if ($parts.Count -ne 2) {
        Write-Error "StoreName '$StoreName' is not in the expected '<ServiceName>\<StoreName>' form (e.g. 'NTDS\My')."
        return
    }
    $serviceName = $parts[0]
    $leafStoreName = $parts[1]

    $items = Get-NtdsServiceStoreCertificate -ServiceName $serviceName -StoreName $leafStoreName

    # Best-effort diagnostic cross-check against Personal - never affects the returned inventory.
    try {
        $ntdsThumbprints = @($items | ForEach-Object { $_.Thumbprint })
        $serverAuthOid = '1.3.6.1.5.5.7.3.1'

        Get-ChildItem -Path 'Cert:\LocalMachine\My' -ErrorAction Stop | ForEach-Object {
            if ($ntdsThumbprints -notcontains $_.Thumbprint) {
                $eku = $_.Extensions | Where-Object { $_.Oid.Value -eq '2.5.29.37' }
                $looksLikeServerAuth = (-not $eku) -or ($eku.EnhancedKeyUsages | Where-Object { $_.Value -eq $serverAuthOid })

                if ($looksLikeServerAuth) {
                    Write-Warning "Certificate '$($_.Thumbprint)' (Subject: $($_.Subject)) exists in Cert:\LocalMachine\My and appears Server-Authentication-eligible, but was not found in the '$StoreName' service store. It may not yet be in use by the LDAPS listener."
                }
            }
        }
    }
    catch {
        Write-Information "[VERBOSE] Personal-store diagnostic cross-check skipped: $_"
    }

    if ($items.Count -gt 0) {
        $items | ConvertTo-Json -Depth 10
    }
    else {
        Write-Warning "No certificates were found in the '$StoreName' service store."
    }
}
