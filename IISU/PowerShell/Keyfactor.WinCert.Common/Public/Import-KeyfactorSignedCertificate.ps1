function Import-KeyfactorSignedCertificate {
    param (
        [Parameter(Mandatory = $true)]
        [byte[]]$RawData,

        [Parameter(Mandatory = $true)]
        [string]$StoreName
    )

    $tempCertFile = $null
    try {
        Write-Information "Entering Import-KeyfactorSignedCertificate"

        # Extract thumbprint from the raw certificate bytes
        $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($RawData)
        $thumbprint = $cert.Thumbprint
        if (-not $thumbprint) {
            throw "Failed to get thumbprint from the signed certificate."
        }
        Write-Information "Certificate thumbprint: $thumbprint"

        # Write to temp .cer file so certreq can process it
        $tempCertFile = [System.IO.Path]::GetTempFileName() + ".cer"
        [System.IO.File]::WriteAllBytes($tempCertFile, $RawData)

        # certreq -accept links the signed certificate to the matching pending private key
        # that was created when New-KeyfactorODKGEnrollment ran certreq -new.
        Write-Information "Running certreq -accept to complete enrollment"
        $output = & certreq -accept $tempCertFile 2>&1
        if ($LASTEXITCODE -ne 0) {
            throw "certreq -accept failed (exit code $LASTEXITCODE). Output: $output"
        }
        Write-Information "certreq -accept completed successfully."

        # certreq -accept installs into the 'My' (Personal) store.
        # If the target store is different, also register the cert there.
        $normalizedStore = $StoreName.Trim()
        if ($normalizedStore -ine 'My' -and $normalizedStore -ine 'Personal') {
            Write-Information "Adding certificate to store '$normalizedStore'"
            $addOutput = & certutil -f -addstore $normalizedStore $tempCertFile 2>&1
            if ($LASTEXITCODE -ne 0) {
                Write-Warning "certutil -addstore for store '$normalizedStore' returned exit code $LASTEXITCODE. Output: $addOutput"
            } else {
                Write-Information "Certificate added to store '$normalizedStore'."
            }
        }

        return $thumbprint
    }
    catch {
        Write-Error "An error occurred in Import-KeyfactorSignedCertificate: $_"
        return $null
    }
    finally {
        if ($tempCertFile -and (Test-Path $tempCertFile)) {
            Remove-Item $tempCertFile -Force
        }
    }
}
