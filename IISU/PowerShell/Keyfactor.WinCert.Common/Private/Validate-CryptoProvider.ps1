function Validate-CryptoProvider {
    param (
        [Parameter(Mandatory)]
        [string]$ProviderName
    )
    Write-Information "[VERBOSE] Validating CSP: $ProviderName"

    $availableProviders = Get-CryptoProviders

    if (-not ($availableProviders | Where-Object { $_.Trim().ToLowerInvariant() -eq $ProviderName.Trim().ToLowerInvariant() })) {

        throw "Crypto Service Provider '$ProviderName' is either invalid or not found on this system."
    }

    Write-Information "[VERBOSE] Crypto Service Provider '$ProviderName' is valid."
}