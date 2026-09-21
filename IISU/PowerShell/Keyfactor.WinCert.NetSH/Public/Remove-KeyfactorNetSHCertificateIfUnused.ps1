function Remove-KeyfactorNetSHCertificateIfUnused {
    <#
    .SYNOPSIS
    Removes a certificate from a Windows certificate store, but only if no netsh http sslcert
    binding still references it.

    .DESCRIPTION
    Mirrors Remove-KeyfactorIISCertificateIfUnused (Keyfactor.WinCert.IIS): scans every current
    sslcert binding for the thumbprint before removing it from the store, since the same
    certificate could still be in active use by another binding on this machine.
    #>
    [CmdletBinding()]
    [OutputType([pscustomobject])]
    param (
        [Parameter(Mandatory = $true)]
        [string]$Thumbprint,

        [Parameter(Mandatory = $false)]
        [string]$StoreName = "My"
    )

    $normalizedThumbprint = ($Thumbprint -replace '\s', '').ToUpperInvariant()
    Write-Information "[VERBOSE] Remove-KeyfactorNetSHCertificateIfUnused: checking thumbprint $normalizedThumbprint in store $StoreName"

    try {
        $stillBound = @(Get-NetShSslCertBinding | Where-Object { $_.CertificateHash -eq $normalizedThumbprint })

        if ($stillBound.Count -gt 0) {
            $bindingSummary = ($stillBound | ForEach-Object { if ($_.IPPort) { $_.IPPort } else { $_.HostnamePort } }) -join ", "
            Write-Information "[VERBOSE] Certificate $normalizedThumbprint is still bound to $($stillBound.Count) binding(s) - skipping removal"
            $stillBound | ForEach-Object { Write-Warning "  Still bound: $(if ($_.IPPort) { $_.IPPort } else { $_.HostnamePort })" }

            return New-KeyfactorResult -Status Skipped -Code 803 -Step RemoveCertificate `
                -Message "Certificate $normalizedThumbprint is still bound to $($stillBound.Count) sslcert binding(s) ($bindingSummary); removal skipped." `
                -Details @{ Thumbprint = $normalizedThumbprint; StillBoundTo = $stillBound }
        }

        $cert = Get-ChildItem -Path "Cert:\LocalMachine\$StoreName" |
            Where-Object { $_.Thumbprint -eq $normalizedThumbprint }

        if (-not $cert) {
            Write-Information "[VERBOSE] Certificate $normalizedThumbprint not found in Cert:\LocalMachine\$StoreName - nothing to remove"
            return New-KeyfactorResult -Status Skipped -Code 804 -Step RemoveCertificate `
                -Message "Certificate $normalizedThumbprint was not found in Cert:\LocalMachine\$StoreName; nothing to remove." `
                -Details @{ Thumbprint = $normalizedThumbprint }
        }

        Remove-Item -Path "Cert:\LocalMachine\$StoreName\$normalizedThumbprint" -Force
        Write-Information "[VERBOSE] Certificate $normalizedThumbprint removed from Cert:\LocalMachine\$StoreName"

        return New-KeyfactorResult -Status Success -Code 0 -Step RemoveCertificate `
            -Message "Certificate $normalizedThumbprint removed from store '$StoreName'." `
            -Details @{ Thumbprint = $normalizedThumbprint }
    }
    catch {
        $errorMessage = "An error occurred while attempting to remove the netsh-bound certificate: $_"
        Write-Warning $errorMessage
        return New-KeyfactorResult -Status Error -Code 805 -Step RemoveCertificate -ErrorMessage $errorMessage -Details @{ Thumbprint = $normalizedThumbprint }
    }
}
