function Test-CryptoServiceProvider {
    param(
        [Parameter(Mandatory = $true)]
        [string]$CSPName
    )

    try {
        Validate-CryptoProvider -ProviderName $CSPName -Verbose:$false
        return $true
    }
    catch {
        return $false
    }
}