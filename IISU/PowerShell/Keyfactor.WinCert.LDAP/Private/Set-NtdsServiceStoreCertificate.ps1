function Set-NtdsServiceStoreCertificate {
    <#
    .SYNOPSIS
    Writes a certificate into a Windows service-specific certificate store (e.g. NTDS\My).

    .DESCRIPTION
    Writes directly to the registry rather than shelling out to certutil.exe - certutil's
    -addstore/-delstore verbs never supported -service in the first place (confirmed via
    `certutil -addstore -?`/`-delstore -?`; only the read-only -store verb documents it), so a
    registry-based implementation is used for all three operations (Get/Set/Remove) instead.

    Mirrors what Windows itself stores under this key: one subkey per certificate, named by its
    uppercase SHA1 thumbprint, holding a "Blob" REG_BINARY value that is exactly an
    [X509Certificate2]::Export([X509ContentType]::SerializedCert) blob - see
    Get-NtdsServiceStoreCertificate.ps1 for the read side of this same layout.

    The full certificate object (including its private-key association, when present) is written
    here via SerializedCert - unlike a plain -addstore .cer import, this preserves HasPrivateKey on
    the NTDS-store copy without relying on CAPI/CNG key-container matching across stores.
    #>
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [string]$ServiceName,

        [Parameter(Mandatory = $true)]
        [string]$StoreName,

        [Parameter(Mandatory = $true)]
        [System.Security.Cryptography.X509Certificates.X509Certificate2]$Certificate
    )

    $thumbprint = $Certificate.Thumbprint.ToUpper()
    $regPath = "HKLM:\SOFTWARE\Microsoft\Cryptography\Services\$ServiceName\SystemCertificates\$StoreName\Certificates\$thumbprint"

    try {
        $blob = $Certificate.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::SerializedCert)

        New-Item -Path $regPath -Force | Out-Null
        Set-ItemProperty -Path $regPath -Name 'Blob' -Value $blob -Type Binary

        return [PSCustomObject]@{
            Success      = $true
            ErrorMessage = ""
        }
    }
    catch {
        return [PSCustomObject]@{
            Success      = $false
            ErrorMessage = "Failed to write certificate '$thumbprint' into the '$ServiceName\$StoreName' registry store: $($_.Exception.Message)"
        }
    }
}
