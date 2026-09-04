function Get-NtdsServiceStoreCertificate {
    <#
    .SYNOPSIS
    Enumerates certificates in a Windows service-specific certificate store (e.g. NTDS\My, the
    registry-backed store the LDAPS listener reads from).

    .DESCRIPTION
    Reads directly from the registry rather than shelling out to certutil.exe. See
    Set-NtdsServiceStoreCertificate.ps1 for the registry layout this assumes (one subkey per
    certificate, named by its uppercase SHA1 thumbprint, holding a "Blob" REG_BINARY value that is
    exactly an [X509Certificate2]::Export([X509ContentType]::SerializedCert) blob).

    Constructing an X509Certificate2 directly from that blob gives full metadata (Subject/Issuer/
    NotAfter/HasPrivateKey/RawData) straight from the NTDS store itself - unlike the previous
    certutil-based implementation, there is no need to cross-reference Cert:\LocalMachine\My for
    metadata, and no partial-record case for a certificate found here but not there.
    #>
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [string]$ServiceName,

        [Parameter(Mandatory = $true)]
        [string]$StoreName
    )

    $result = Invoke-CertUtilNtdsStore -Arguments @('-service', '-store', "$ServiceName\$StoreName")

    if (-not (Test-Path $certsKeyPath)) {
        Write-Information "No certificates found in the '$ServiceName\$StoreName' service store."
        return @()
    }

    $items = @()
    Get-ChildItem -Path $certsKeyPath -ErrorAction SilentlyContinue | ForEach-Object {
        $thumbprint = $_.PSChildName
        try {
            $blob = (Get-ItemProperty -Path $_.PSPath -Name 'Blob' -ErrorAction Stop).Blob
            $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2(, [byte[]]$blob)

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
        catch {
            Write-Warning "Skipping registry entry '$thumbprint' in the '$ServiceName\$StoreName' service store - its 'Blob' value could not be read or parsed as a certificate: $($_.Exception.Message)"
        }
    }

    if ($items.Count -eq 0) {
        Write-Information "No certificates found in the '$ServiceName\$StoreName' service store."
    }

    return $items
}
