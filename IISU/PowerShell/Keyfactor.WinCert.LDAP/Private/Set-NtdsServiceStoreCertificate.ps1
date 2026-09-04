function Set-NtdsServiceStoreCertificate {
    <#
    .SYNOPSIS
    Writes a certificate into a Windows service-specific certificate store (e.g. NTDS\My).

    .DESCRIPTION
    See Invoke-CertUtilNtdsStore.ps1 for the certutil argument shape used here
    ("-service -addstore <ServiceName>\<StoreName> <CertFile>").

    Only the certificate's public bytes are written here (a .cer, not a .pfx) - the private key
    itself is not re-imported. This assumes Windows resolves a certificate's private-key association
    via CAPI/CNG machine-key-container matching (thumbprint/key match), independent of which logical
    store lists the certificate object - the same assumption already relied upon implicitly by every
    other store type in this repo that can bind one imported certificate into more than one place
    (e.g. IIS binding a cert already staged in the machine store). This must be confirmed for the
    NTDS store specifically by reading back HasPrivateKey on the NTDS-store copy in lab testing (see
    docsource/winldap.md validation checklist) - if it does not hold, this function will need to
    import the PFX directly into the service store instead of just the public certificate.
    #>
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [string]$ServiceName,

        [Parameter(Mandatory = $true)]
        [string]$StoreName,

        [Parameter(Mandatory = $true)]
        [byte[]]$RawCertificateBytes
    )

    $tempCerFile = [System.IO.Path]::GetTempFileName() + ".cer"

    try {
        [System.IO.File]::WriteAllBytes($tempCerFile, $RawCertificateBytes)

        $result = Invoke-CertUtilNtdsStore -Arguments @('-f', '-service', '-addstore', "$ServiceName\$StoreName", $tempCerFile)

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
    finally {
        if (Test-Path $tempCerFile) {
            Remove-Item $tempCerFile -Force -ErrorAction SilentlyContinue
        }
    }
}
