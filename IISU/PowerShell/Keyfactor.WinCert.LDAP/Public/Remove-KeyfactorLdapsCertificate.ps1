# See Add-KeyfactorLdapsCertificate.ps1 for the shared WinLDAP module result code range.
# Code  Status   Step                Description
# 0     Success  RemovePersonalStore Operation completed successfully
# 710   Error    InvalidStoreName    StoreName was not in '<ServiceName>\<StoreName>' form
# 720   Error    RemoveNtdsStore     Removing the certificate from the NTDS service store failed
# 730   Error    RemovePersonalStore Removing the certificate from the Personal (My) store failed
# 799   Error    CatchAll            Unexpected/unhandled exception

function Remove-KeyfactorLdapsCertificate {
    <#
    .SYNOPSIS
    Removes a certificate from both the AD DS (NTDS) LDAPS service certificate store and the
    Personal ("My") store, by thumbprint.

    .DESCRIPTION
    Lab-confirmed on a live Domain Controller: removing only the NTDS service store registry entry
    does NOT stop the LDAPS listener (port 636) from continuing to present that certificate, even
    after an NTDS service restart. Removing the certificate from Cert:\LocalMachine\My as well - with
    no service restart required - did stop it. This reverses the original design, which intentionally
    left Personal untouched as an internal Add-time staging detail (see docsource/winldap.md); that
    assumption did not hold up under lab testing and has been corrected here.

    IMPORTANT SHARED-STORE CAVEAT: Cert:\LocalMachine\My is a general-purpose store, not something
    exclusively owned by this store type. If the certificate being removed is also bound to another
    service on this Domain Controller (e.g. WinRM HTTPS, RDP, another Keyfactor-managed store), this
    will remove it from that service too - WinLDAP has no visibility into other consumers of this
    store and does not check for them before removing.
    #>
    param (
        [Parameter(Mandatory = $true)]
        [string]$Thumbprint,

        [Parameter(Mandatory = $true)]
        [string]$StoreName
    )

    try {
        Write-Information "Entering PowerShell Script Remove-KeyfactorLdapsCertificate"

        $parts = $StoreName -split '\\', 2
        if ($parts.Count -ne 2) {
            $msg = "StoreName '$StoreName' is not in the expected '<ServiceName>\<StoreName>' form (e.g. 'NTDS\My')."
            Write-Error $msg
            return New-KeyfactorResult -Status Error -Code 710 -Step InvalidStoreName -ErrorMessage $msg
        }
        $serviceName = $parts[0]
        $leafStoreName = $parts[1]

        # --- Step: RemoveNtdsStore ---------------------------------------------
        $removeResult = Remove-NtdsServiceStoreCertificate -ServiceName $serviceName -StoreName $leafStoreName -Thumbprint $Thumbprint

        if (-not $removeResult.Success) {
            Write-Error $removeResult.ErrorMessage
            return New-KeyfactorResult -Status Error -Code 720 -Step RemoveNtdsStore `
                -ErrorMessage $removeResult.ErrorMessage `
                -Details @{ Thumbprint = $Thumbprint }
        }

        Write-Information "The thumbprint '$Thumbprint' was removed from the '$serviceName\$leafStoreName' service store."

        # --- Step: RemovePersonalStore ------------------------------------------
        # Required for LDAPS to actually stop presenting the certificate - see .DESCRIPTION above.
        # Reuses the existing, unmodified Remove-KeyfactorCertificate (Keyfactor.WinCert.Common).
        try {
            Remove-KeyfactorCertificate -Thumbprint $Thumbprint -StorePath 'My' | Out-Null
            Write-Information "The thumbprint '$Thumbprint' was removed from Cert:\LocalMachine\My."
        }
        catch {
            if ($_.Exception.Message -match 'not found') {
                Write-Information "Thumbprint '$Thumbprint' was already absent from Cert:\LocalMachine\My - nothing to remove there."
            }
            else {
                $msg = "Certificate '$Thumbprint' was removed from the '$serviceName\$leafStoreName' service store, but removing it from Cert:\LocalMachine\My failed: $($_.Exception.Message). The LDAPS listener may continue to present this certificate until it is also removed from the Personal store."
                Write-Error $msg
                return New-KeyfactorResult -Status Error -Code 730 -Step RemovePersonalStore `
                    -ErrorMessage $msg `
                    -Details @{ Thumbprint = $Thumbprint }
            }
        }

        return New-KeyfactorResult -Status Success -Code 0 -Step RemovePersonalStore `
            -Message "Certificate '$Thumbprint' removed from the '$serviceName\$leafStoreName' service store and from Cert:\LocalMachine\My." `
            -Details @{ Thumbprint = $Thumbprint }
    }
    catch {
        $msg = "Unexpected error in Remove-KeyfactorLdapsCertificate: $($_.Exception.Message)"
        Write-Error $msg
        return New-KeyfactorResult -Status Error -Code 799 -Step CatchAll `
            -ErrorMessage $msg `
            -Details @{ Thumbprint = $Thumbprint }
    }
}
