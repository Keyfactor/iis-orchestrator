function Set-NtdsServiceStoreCertificate {
    <#
    .SYNOPSIS
    Writes a certificate into a Windows service-specific certificate store (e.g. NTDS\My).

    .DESCRIPTION
    Service-specific stores such as NTDS\My are registry-backed at
    HKLM\SOFTWARE\Microsoft\Cryptography\Services\<ServiceName>\SystemCertificates\<StoreName>\
    Certificates. Each certificate is one subkey named by its uppercase SHA1 thumbprint, holding a
    single REG_BINARY value named "Blob". That Blob is exactly what
    [X509Certificate2]::Export([X509ContentType]::SerializedCert) produces - confirmed by comparing
    against a live registry-backed store - so no certutil.exe call or hand-built binary format is
    needed.

    IMPORTANT: -Certificate must be a certificate object read back FROM an actual certificate store
    (e.g. via the Cert: provider), not one constructed directly from raw/PFX bytes in memory - that
    distinction was verified empirically: an X509Certificate2 loaded straight from PFX bytes (even
    with PersistKeySet/MachineKeySet) does not reliably carry the CERT_KEY_PROV_INFO_PROP_ID
    property when exported, so Export(SerializedCert) round-trips HasPrivateKey = False. Only a
    certificate re-read from a store correctly carries that property, which is how the NTDS-store
    copy resolves HasPrivateKey via CAPI/CNG machine-key-container matching without re-importing the
    PFX itself. See the "RereadPersonal" step in Add-KeyfactorLdapsCertificate.ps1, which re-reads
    the certificate from Cert:\LocalMachine\My after staging before calling this function.
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

    try {
        $regPath = "HKLM:\SOFTWARE\Microsoft\Cryptography\Services\$ServiceName\SystemCertificates\$StoreName\Certificates\$($Certificate.Thumbprint.ToUpper())"
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
            ErrorMessage = "Failed to write certificate '$($Certificate.Thumbprint)' into the '$ServiceName\$StoreName' registry store: $($_.Exception.Message)"
        }
    }
}
