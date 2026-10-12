function Remove-NtdsServiceStoreCertificate {
    <#
    .SYNOPSIS
    Removes a certificate from a Windows service-specific certificate store (e.g. NTDS\My), by
    thumbprint.

    .DESCRIPTION
    Removes the registry subkey directly - see Set-NtdsServiceStoreCertificate.ps1 for the registry
    layout this assumes (one subkey per certificate, named by its uppercase SHA1 thumbprint).

    This only removes the certificate from the service store - it does NOT touch the Personal ("My")
    store copy created by Add-KeyfactorLdapsCertificate's staging step. That second removal is
    handled by the caller, Remove-KeyfactorLdapsCertificate.ps1 (Public), which lab testing showed is
    actually required for the LDAPS listener to stop presenting the certificate - see that file's
    .DESCRIPTION for details. This function stays scoped to the service store only, symmetric with
    Get-KeyfactorLdapCertificates reading only from the service store.

    IMPORTANT OPERATIONAL RISK (see docsource/winldap.md): removing the certificate the LDAPS
    listener is currently using is disruptive by design - once both stores are cleared, LDAPS will
    no longer be able to present this certificate at all.
    #>
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [string]$ServiceName,

        [Parameter(Mandatory = $true)]
        [string]$StoreName,

        [Parameter(Mandatory = $true)]
        [string]$Thumbprint
    )

    $cleanThumbprint = ($Thumbprint -replace '[^a-fA-F0-9]', '').ToUpper()
    $regPath = "HKLM:\SOFTWARE\Microsoft\Cryptography\Services\$ServiceName\SystemCertificates\$StoreName\Certificates\$cleanThumbprint"

    try {
        if (-not (Test-Path $regPath)) {
            return [PSCustomObject]@{
                Success      = $false
                ErrorMessage = "Certificate '$cleanThumbprint' was not found in the '$ServiceName\$StoreName' service store."
            }
        }

        Remove-Item -Path $regPath -Recurse -Force

        return [PSCustomObject]@{
            Success      = $true
            ErrorMessage = ""
        }
    }
    catch {
        return [PSCustomObject]@{
            Success      = $false
            ErrorMessage = "Failed to remove certificate '$cleanThumbprint' from the '$ServiceName\$StoreName' registry store: $($_.Exception.Message)"
        }
    }
}
