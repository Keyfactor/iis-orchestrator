param (
    [Parameter(Mandatory = $true)]
    [string]$Base64Cert,

    [Parameter(Mandatory = $false)]
    [string]$PrivateKeyPassword,

    [Parameter(Mandatory = $true)]
    [string]$StorePath,

    [Parameter(Mandatory = $false)]
    [string]$CryptoServiceProvider
)

function Add-CertificateToStore {
    param (
        [string]$Base64Cert,
        [string]$PrivateKeyPassword,
        [string]$StorePath,
        [string]$CryptoServiceProvider
    )

    try {
        # Convert Base64 string to byte array
        $certBytes = [Convert]::FromBase64String($Base64Cert)

        # Create a temporary file to store the certificate
        $tempCertPath = [System.IO.Path]::GetTempFileName()
        [System.IO.File]::WriteAllBytes($tempCertPath, $certBytes)

        if ($CryptoServiceProvider) {
            # Create a temporary PFX file
            $tempPfxPath = [System.IO.Path]::ChangeExtension($tempCertPath, ".pfx")
            $pfxPassword = if ($PrivateKeyPassword) { $PrivateKeyPassword } else { "" }
            $pfxCert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($tempCertPath, $pfxPassword, [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::Exportable)
            $pfxCert.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Pfx, $pfxPassword) | Set-Content -Encoding Byte -Path $tempPfxPath

            # Use certutil to import the PFX with the specified CSP
            $importCmd = "certutil -f -importpfx $tempPfxPath -p $pfxPassword -csp `"$CryptoServiceProvider`""
            Invoke-Expression $importCmd

            # Clean up the temporary PFX file
            Remove-Item $tempPfxPath
        } else {
            # Load the certificate from the temporary file
            if ($PrivateKeyPassword) {
                $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($tempCertPath, $PrivateKeyPassword, [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::Exportable)
            } else {
                $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($tempCertPath)
            }

            # Open the certificate store
            $store = New-Object System.Security.Cryptography.X509Certificates.X509Store($StorePath, [System.Security.Cryptography.X509Certificates.StoreLocation]::LocalMachine)
            $store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)

            # Add the certificate to the store
            $store.Add($cert)

            # Close the store
            $store.Close()
        }

        # Clean up the temporary file
        Remove-Item $tempCertPath

        Write-Host "Certificate added successfully to $StorePath."
    } catch {
        Write-Error "An error occurred: $_"
    }
}

Add-CertificateToStore -Base64Cert $Base64Cert -PrivateKeyPassword $PrivateKeyPassword -StorePath $StorePath -CryptoServiceProvider $CryptoServiceProvider
