# Set-NetShSslCertBinding creates or replaces a single "netsh http sslcert" binding.
#
# netsh has no "update" verb - rebinding an existing ipport/hostnameport means deleting it first,
# then adding it again with the new certificate. This function also resolves which AppId to use
# when the caller doesn't supply one, per the behavior documented in docsource/winnetsh.md:
#   1. Caller-supplied AppId - used as-is.
#   2. No AppId supplied, but a binding already exists at this key - reuse ITS AppId, so a renewal
#      doesn't silently change the AppId an existing consumer may be relying on.
#   3. No AppId supplied and no existing binding - generate a new GUID for this brand-new binding.
# The resolved AppId (and which of the three cases produced it) is always returned to the caller so
# it can be logged/reported back, even when it wasn't supplied.
function Set-NetShSslCertBinding {
    [CmdletBinding()]
    [OutputType([pscustomobject])]
    param (
        [Parameter(Mandatory = $true)]
        [string]$IPAddress,

        [Parameter(Mandatory = $true)]
        [string]$Port,

        [Parameter(Mandatory = $false)]
        [AllowEmptyString()]
        [string]$HostName = "",

        [Parameter(Mandatory = $true)]
        [string]$Thumbprint,

        [Parameter(Mandatory = $true)]
        [string]$StoreName,

        [Parameter(Mandatory = $false)]
        [AllowEmptyString()]
        [string]$AppId = ""
    )

    $useHostname = -not [string]::IsNullOrWhiteSpace($HostName)
    $bindingKey = if ($useHostname) { "${HostName}:${Port}" } else { "${IPAddress}:${Port}" }

    $existingBinding = if ($useHostname) {
        Get-NetShSslCertBinding -HostnamePort $bindingKey | Select-Object -First 1
    }
    else {
        Get-NetShSslCertBinding -IPPort $bindingKey | Select-Object -First 1
    }

    if (-not [string]::IsNullOrWhiteSpace($AppId)) {
        $resolvedAppId = $AppId
        $appIdSource = 'Supplied'
    }
    elseif ($existingBinding -and $existingBinding.ApplicationId) {
        $resolvedAppId = $existingBinding.ApplicationId
        $appIdSource = 'Reused'
    }
    else {
        $resolvedAppId = [guid]::NewGuid().ToString()
        $appIdSource = 'Generated'
    }

    # netsh requires the AppId wrapped in braces.
    $trimmedAppId = $resolvedAppId.Trim('{', '}')
    $resolvedAppId = "{$trimmedAppId}"

    if ($existingBinding) {
        Write-Information "[VERBOSE] Removing existing sslcert binding at '$bindingKey' before rebinding"
        $deleteKeyArg = if ($useHostname) { "hostnameport=$bindingKey" } else { "ipport=$bindingKey" }
        $deleteOutput = & netsh http delete sslcert $deleteKeyArg 2>&1

        if ($LASTEXITCODE -ne 0) {
            return [PSCustomObject]@{
                Success      = $false
                ErrorMessage = "Failed to remove the existing sslcert binding at '$bindingKey' before rebinding: $($deleteOutput -join ' ')"
                AppId        = $resolvedAppId
                AppIdSource  = $appIdSource
            }
        }
    }

    Write-Information "[VERBOSE] Adding sslcert binding at '$bindingKey' (certhash=$Thumbprint, appid=$resolvedAppId, certstorename=$StoreName)"
    $addKeyArg = if ($useHostname) { "hostnameport=$bindingKey" } else { "ipport=$bindingKey" }
    $addOutput = & netsh http add sslcert $addKeyArg "certhash=$Thumbprint" "appid=$resolvedAppId" "certstorename=$StoreName" 2>&1

    if ($LASTEXITCODE -ne 0) {
        return [PSCustomObject]@{
            Success      = $false
            ErrorMessage = "Failed to add the sslcert binding at '$bindingKey': $($addOutput -join ' ')"
            AppId        = $resolvedAppId
            AppIdSource  = $appIdSource
        }
    }

    return [PSCustomObject]@{
        Success      = $true
        ErrorMessage = $null
        AppId        = $resolvedAppId
        AppIdSource  = $appIdSource
    }
}
