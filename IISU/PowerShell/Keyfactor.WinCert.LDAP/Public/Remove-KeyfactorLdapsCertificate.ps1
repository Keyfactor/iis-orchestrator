# See Add-KeyfactorLdapsCertificate.ps1 for the shared WinLDAP module result code range.
# Code  Status   Step             Description
# 0     Success  RemoveNtdsStore  Operation completed successfully
# 710   Error    InvalidStoreName StoreName was not in '<ServiceName>\<StoreName>' form
# 720   Error    RemoveNtdsStore  Removing the certificate from the NTDS service store failed
# 799   Error    CatchAll         Unexpected/unhandled exception

function Remove-KeyfactorLdapsCertificate {
    <#
    .SYNOPSIS
    Removes a certificate from the AD DS (NTDS) LDAPS service certificate store, by thumbprint.

    .DESCRIPTION
    Removes ONLY from the NTDS service store - symmetric with Get-KeyfactorLdapCertificates being
    scoped to that store as the single source of truth. Does NOT remove the corresponding copy from
    the Personal ("My") store that Add-KeyfactorLdapsCertificate stages during Add; this is a
    documented trade-off, not an oversight (see docsource/winldap.md).

    IMPORTANT: removing the certificate currently in use by the LDAPS listener carries real
    operational risk (see docsource/winldap.md validation checklist item on Remove-of-active-cert
    behavior) - this has not been verified against a live DC.
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

        $removeResult = Remove-NtdsServiceStoreCertificate -ServiceName $serviceName -StoreName $leafStoreName -Thumbprint $Thumbprint

        if (-not $removeResult.Success) {
            $msg = "certutil exited with code $($removeResult.ExitCode) while removing certificate '$Thumbprint' from the '$serviceName\$leafStoreName' service store. StdErr: $($removeResult.StdErr) StdOut: $($removeResult.StdOut)"
            Write-Error $msg
            return New-KeyfactorResult -Status Error -Code 720 -Step RemoveNtdsStore `
                -ErrorMessage $msg `
                -Details @{
                    Thumbprint = $Thumbprint
                    ExitCode   = $removeResult.ExitCode
                    StdOut     = $removeResult.StdOut
                    StdErr     = $removeResult.StdErr
                }
        }

        Write-Information "The thumbprint '$Thumbprint' was removed from the '$serviceName\$leafStoreName' service store."

        return New-KeyfactorResult -Status Success -Code 0 -Step RemoveNtdsStore `
            -Message "Certificate '$Thumbprint' removed from the '$serviceName\$leafStoreName' service store." `
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
