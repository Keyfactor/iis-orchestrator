function Remove-NtdsServiceStoreCertificate {
    <#
    .SYNOPSIS
    Removes a certificate from a Windows service-specific certificate store (e.g. NTDS\My), by
    thumbprint.

    .DESCRIPTION
    UNVERIFIED - MUST BE LAB-VALIDATED. See Invoke-CertUtilNtdsStore.ps1 for the certutil argument
    shape this assumes ("-delstore -service <ServiceName> <StoreName> <Thumbprint>").

    This only removes the certificate from the service store - it deliberately does NOT touch the
    Personal ("My") store copy created by Add-KeyfactorLdapsCertificate's staging step. This is a
    documented trade-off (see docsource/winldap.md), symmetric with Get-KeyfactorLdapCertificates
    only reading from the service store.

    IMPORTANT OPERATIONAL RISK (see docsource/winldap.md validation checklist): removing the
    certificate the LDAPS listener is currently using may cause LDAPS (port 636) to stop responding,
    fall back to another eligible certificate, or require a service restart to notice the removal -
    this has not been verified against a live DC and should be treated as high-risk until it has.
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

    $cleanThumbprint = $Thumbprint -replace '[^a-fA-F0-9]', ''

    $result = Invoke-CertUtilNtdsStore -Arguments @('-delstore', '-service', $ServiceName, $StoreName, $cleanThumbprint)

    if (-not $result.Started) {
        return [PSCustomObject]@{
            Success  = $false
            ExitCode = -1
            StdOut   = ""
            StdErr   = $result.StdErr
        }
    }

    return [PSCustomObject]@{
        Success  = ($result.ExitCode -eq 0)
        ExitCode = $result.ExitCode
        StdOut   = $result.StdOut
        StdErr   = $result.StdErr
    }
}
