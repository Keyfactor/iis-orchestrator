function Validate-CryptoProvider {
    param (
        [Parameter(Mandatory)]
        [string]$ProviderName
    )

    Write-Information "[VERBOSE] Validating cryptographic provider: $ProviderName"

    $availableProviders = Get-CryptoProviders

    $trimmedProvider = $ProviderName.Trim()

    $providerFound = $availableProviders | Where-Object {
        [string]::Equals(
            $_.Trim(),
            $trimmedProvider,
            [System.StringComparison]::OrdinalIgnoreCase
        )
    }

    if (-not $providerFound) {

        $availableProviderList = @(
            $availableProviders |
                Sort-Object -Unique |
                ForEach-Object { "'$_'" }
        ) -join ", "

        throw "The requested Crypto Service Provider '$ProviderName' was not found on the target system. Available providers: $availableProviderList"
    }

    Write-Information "[VERBOSE] Cryptographic provider '$ProviderName' is valid."
}