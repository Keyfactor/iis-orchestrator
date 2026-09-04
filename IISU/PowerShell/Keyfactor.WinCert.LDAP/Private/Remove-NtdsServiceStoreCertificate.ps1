function Remove-NtdsServiceStoreCertificate {
    <#
    .SYNOPSIS
    Removes a certificate from a Windows service-specific certificate store (e.g. NTDS\My), by
    thumbprint.

    .DESCRIPTION
    See Invoke-CertUtilNtdsStore.ps1 for the certutil argument shape used here
    ("-service -delstore <ServiceName>\<StoreName> <Thumbprint>").

    This only removes the certificate from the service store - it deliberately does NOT touch the
    Personal ("My") store copy created by Add-KeyfactorLdapsCertificate's staging step. This is a
    documented trade-off (see docsource/winldap.md), symmetric with Get-KeyfactorLdapCertificates
    only reading from the service store.

    IMPORTANT OPERATIONAL RISK (see docsource/winldap.md): removing the certificate the LDAPS
    listener is currently using may cause LDAPS (port 636) to stop responding, fall back to another
    eligible certificate, or require a service restart to notice the removal - this has not been
    verified against a live DC and should be treated as high-risk until it has.
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

    $result = Invoke-CertUtilNtdsStore -Arguments @('-service', '-delstore', "$ServiceName\$StoreName", $cleanThumbprint)

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
