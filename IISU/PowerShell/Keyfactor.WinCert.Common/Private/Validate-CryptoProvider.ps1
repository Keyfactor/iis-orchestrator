function Validate-CryptoProvider {
    param (
        [Parameter(Mandatory)]
        [string]$ProviderName
    )
    Write-Information "[VERBOSE] Validating CSP: $ProviderName"

    $availableProviders = Get-CryptoProviders

    $trimmedProvider = $ProviderName.Trim()
    if (-not ($availableProviders | Where-Object {
            [string]::Equals($_.Trim(), $trimmedProvider, [System.StringComparison]::OrdinalIgnoreCase)
        })) {

        throw "Crypto Service Provider '$ProviderName' is either invalid or not found on this system."
    }

    Write-Information "[VERBOSE] Crypto Service Provider '$ProviderName' is valid."
}