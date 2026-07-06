function Get-CryptoProviders {
    # Retrieves the list of available Crypto Service Providers.
    #
    # Preferred source is the registry because CSP names are stored as
    # culture-invariant subkey names under
    #   HKLM:\SOFTWARE\Microsoft\Cryptography\Defaults\Provider

    try {
        Write-Information "[VERBOSE] Retrieving Crypto Service Providers from registry..."

        $regPath = 'HKLM:\SOFTWARE\Microsoft\Cryptography\Defaults\Provider'
        $cspInfoList = @()

        if (Test-Path -LiteralPath $regPath) {
            $cspInfoList = @(
                Get-ChildItem -LiteralPath $regPath -ErrorAction Stop |
                    Select-Object -ExpandProperty PSChildName
            )
        }

        if ($cspInfoList.Count -eq 0) {
            Write-Information "[VERBOSE] Registry enumeration returned no CSPs; falling back to certutil."

            $prevOutEncoding = [Console]::OutputEncoding
            try {
                [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
                $certUtilOutput = & certutil.exe -csplist 2>&1
            } finally {
                [Console]::OutputEncoding = $prevOutEncoding
            }

            # The "Provider Name:" label is localized, so match on the structural
            # shape of the line instead of the English text: an un-indented
            # "<label>: <value>" line whose value is not a numeric provider type
            # ("1 - PROV_RSA_FULL", etc.).
            foreach ($line in $certUtilOutput) {
                if ($line -match '^\S[^:]+:\s+(\S.+)$') {
                    $value = $Matches[1].Trim()
                    if ($value -notmatch '^\d+\s*-\s*\S') {
                        $cspInfoList += $value
                    }
                }
            }
        }

        if ($cspInfoList.Count -eq 0) {
            throw "No Crypto Service Providers were found."
        }

        Write-Information "[VERBOSE] Retrieved the following CSPs:"
        $cspInfoList | ForEach-Object { Write-Information "[VERBOSE] $_" }

        return $cspInfoList
    } catch {
        throw "Failed to retrieve Crypto Service Providers: $_"
    }
}