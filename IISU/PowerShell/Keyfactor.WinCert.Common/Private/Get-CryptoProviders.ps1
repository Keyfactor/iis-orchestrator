function Get-CryptoProviders {
    <#
    .SYNOPSIS
        Retrieves the available Windows cryptographic providers.

    .DESCRIPTION
        Returns both legacy CryptoAPI CSPs and modern CNG KSPs.

        Legacy CSPs are retrieved from:
            HKLM:\SOFTWARE\Microsoft\Cryptography\Defaults\Provider

        CNG KSPs are retrieved from certutil -csplist.

        The function intentionally returns a list of provider names as strings
        so existing callers such as Validate-CryptoProvider remain compatible.
    #>

    try {
        Write-Information "[VERBOSE] Retrieving Crypto Service Providers and Key Storage Providers..."

        $providerList = [System.Collections.Generic.List[string]]::new()

        #
        # ------------------------------------------------------------
        # Legacy CryptoAPI CSPs
        # ------------------------------------------------------------
        #
        $cspRegistryPaths = @(
            'HKLM:\SOFTWARE\Microsoft\Cryptography\Defaults\Provider',
            'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Cryptography\Defaults\Provider'
        )

        foreach ($regPath in $cspRegistryPaths) {

            if (Test-Path -LiteralPath $regPath) {

                Write-Information "[VERBOSE] Enumerating legacy CSPs from: $regPath"

                Get-ChildItem -LiteralPath $regPath -ErrorAction Stop |
                    ForEach-Object {

                        $name = $_.PSChildName

                        if (-not [string]::IsNullOrWhiteSpace($name)) {
                            if (-not $providerList.Contains($name)) {
                                $providerList.Add($name)
                            }
                        }
                    }
            }
        }

        #
        # ------------------------------------------------------------
        # CNG KSPs
        # ------------------------------------------------------------
        #
        # certutil -csplist lists both legacy CSPs and CNG providers.
        # We only need the Provider Name entries here.  We do not
        # depend on the Provider Type field because KSPs don't use
        # the legacy CSP Provider Type mechanism.
        #
        Write-Information "[VERBOSE] Enumerating CNG KSPs using certutil..."

        $certUtilOutput = @(
            & certutil.exe -csplist 2>&1 |
                ForEach-Object { $_.ToString() }
        )

        foreach ($line in $certUtilOutput) {

            $trimmedLine = $line.Trim()

            #
            # Provider Name lines may have leading whitespace.
            #
            if ($trimmedLine -match '^Provider Name:\s*(.+)$') {

                $name = $Matches[1].Trim()

                if (-not [string]::IsNullOrWhiteSpace($name)) {

                    if (-not $providerList.Contains($name)) {
                        $providerList.Add($name)
                    }
                }
            }
        }

        #
        # Remove any accidental duplicates and return strings.
        #
        $providers = @(
            $providerList |
                Sort-Object -Unique
        )

        if ($providers.Count -eq 0) {
            throw "No cryptographic providers were found."
        }

        Write-Information "[VERBOSE] Retrieved the following cryptographic providers:"

        foreach ($provider in $providers) {
            Write-Information "[VERBOSE] $provider"
        }

        return $providers
    }
    catch {
        throw "Failed to retrieve cryptographic providers: $($_.Exception.Message)"
    }
}