function Get-CertificateCSP 
{
    param(
        [System.Security.Cryptography.X509Certificates.X509Certificate2]$cert
    )

    # Helper: extract KSP/provider name from a CNG key object
    function Get-CngProviderName {
        param($key)
        try {
            # RSACng / ECDsaCng expose a .Key property (CngKey)
            if ($key.PSObject.Properties['Key']) {
                $cngKey = $key.Key
                if ($cngKey -and $cngKey.Provider -and $cngKey.Provider.Provider) {
                    return [string]$cngKey.Provider.Provider
                }
            }
        }
        catch {
            Write-Verbose "CNG provider lookup failed: $($_.Exception.Message)"
        }
        return $null
    }

    try {
        if (-not $cert.HasPrivateKey) {
            return "No private key"
        }

        # ── 1. Legacy CryptoAPI path (RSACryptoServiceProvider) ──────────────
        $privateKey = $cert.PrivateKey
        if ($privateKey -and $privateKey.CspKeyContainerInfo) {
            $providerName = $privateKey.CspKeyContainerInfo.ProviderName
            if ($providerName) {
                return [string]$providerName
            }
        }

        # ── 2. CNG RSA (RSACng) ───────────────────────────────────────────────
        try {
            $rsaKey = [System.Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPrivateKey($cert)
            if ($rsaKey) {
                $providerName = Get-CngProviderName $rsaKey
                if ($providerName) { return $providerName }
            }
        }
        catch {
            Write-Verbose "RSA CNG detection failed: $($_.Exception.Message)"
        }

        # ── 3. ECC / ECDsa (ECDsaCng) ─────────────────────────────────────────
        # ECC keys always use CNG (KSPs), never legacy CSPs
        try {
            $ecKey = [System.Security.Cryptography.X509Certificates.ECDsaCertificateExtensions]::GetECDsaPrivateKey($cert)
            if ($ecKey) {
                $providerName = Get-CngProviderName $ecKey
                if ($providerName) { return $providerName }

                Write-Verbose "ECC key detected but no resolvable provider name (type: $($ecKey.GetType().Name))"
                return ""
            }
        }
        catch {
            Write-Verbose "ECDsa CNG detection failed: $($_.Exception.Message)"
        }

        # ── 4. DSA (bonus) ────────────────────────────────────────────────────
        try {
            $dsaKey = [System.Security.Cryptography.X509Certificates.DSACertificateExtensions]::GetDSAPrivateKey($cert)
            if ($dsaKey) {
                $providerName = Get-CngProviderName $dsaKey
                if ($providerName) { return $providerName }

                Write-Verbose "DSA key detected but no resolvable provider name (type: $($dsaKey.GetType().Name))"
                return ""
            }
        }
        catch {
            Write-Verbose "DSA CNG detection failed: $($_.Exception.Message)"
        }

        Write-Verbose "No supported key type detected; provider name could not be determined"
        return ""
    }
    catch {
        Write-Warning "Error retrieving CSP for certificate '$($cert.Subject)': $($_.Exception.Message)"
        return ""
    }
}