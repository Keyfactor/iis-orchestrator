# Get-NetShSslCertBinding wraps "netsh http show sslcert" and returns parsed binding objects,
# optionally filtered down to a single binding by IPPort or HostnamePort.
function Get-NetShSslCertBinding {
    [CmdletBinding()]
    [OutputType([pscustomobject[]])]
    param (
        [Parameter(Mandatory = $false)]
        [string]$IPPort,

        [Parameter(Mandatory = $false)]
        [string]$HostnamePort
    )

    if ($IPPort) {
        $rawOutput = & netsh http show sslcert "ipport=$IPPort" 2>&1
    }
    elseif ($HostnamePort) {
        $rawOutput = & netsh http show sslcert "hostnameport=$HostnamePort" 2>&1
    }
    else {
        $rawOutput = & netsh http show sslcert 2>&1
    }

    # "show sslcert" with a specific ipport/hostnameport filter that has no binding prints a
    # "...parameters not found..." message rather than an empty list, and may return a non-zero
    # exit code depending on Windows version - both are "no binding found", not an error.
    $outputText = ($rawOutput -join "`n")
    if ($LASTEXITCODE -ne 0 -or $outputText -match 'not found') {
        if ($IPPort -or $HostnamePort) {
            return @()
        }
    }

    return @(ConvertFrom-NetShSslCertOutput -RawOutput $rawOutput)
}
