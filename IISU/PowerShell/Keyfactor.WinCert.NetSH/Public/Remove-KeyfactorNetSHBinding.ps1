function Remove-KeyfactorNetSHBinding {
    <#
    .SYNOPSIS
    Removes a "netsh http sslcert" IP:Port or Hostname:Port binding.

    .DESCRIPTION
    Only removes the binding itself (the netsh sslcert entry) - it does not remove the certificate
    from the Windows certificate store. See Remove-KeyfactorNetSHCertificateIfUnused for that step.
    #>
    [CmdletBinding()]
    [OutputType([pscustomobject])]
    param (
        [Parameter(Mandatory = $true)]
        [string]$IPAddress,

        [Parameter(Mandatory = $true)]
        [string]$Port,

        [Parameter(Mandatory = $false)]
        [AllowEmptyString()]
        [string]$HostName = ""
    )

    Write-Information "Entering PowerShell Script Remove-KeyfactorNetSHBinding"

    $useHostname = -not [string]::IsNullOrWhiteSpace($HostName)
    $bindingKey = if ($useHostname) { "${HostName}:${Port}" } else { "${IPAddress}:${Port}" }

    try {
        $existingBinding = if ($useHostname) {
            Get-NetShSslCertBinding -HostnamePort $bindingKey | Select-Object -First 1
        }
        else {
            Get-NetShSslCertBinding -IPPort $bindingKey | Select-Object -First 1
        }

        if (-not $existingBinding) {
            Write-Information "[VERBOSE] No sslcert binding found at '$bindingKey' - nothing to remove."
            return New-KeyfactorResult -Status Skipped -Code 0 -Step RemoveBinding `
                -Message "No sslcert binding found at '$bindingKey'; nothing to remove."
        }

        $deleteKeyArg = if ($useHostname) { "hostnameport=$bindingKey" } else { "ipport=$bindingKey" }
        $deleteOutput = & netsh http delete sslcert $deleteKeyArg 2>&1

        if ($LASTEXITCODE -ne 0) {
            $errorMessage = "Failed to remove the sslcert binding at '$bindingKey': $($deleteOutput -join ' ')"
            Write-Error $errorMessage
            return New-KeyfactorResult -Status Error -Code 801 -Step RemoveBinding -ErrorMessage $errorMessage
        }

        Write-Information "[VERBOSE] Removed sslcert binding at '$bindingKey'."
        return New-KeyfactorResult -Status Success -Code 0 -Step RemoveBinding `
            -Message "Removed sslcert binding at '$bindingKey'."
    }
    catch {
        $errorMessage = "Unexpected error in Remove-KeyfactorNetSHBinding: $($_.Exception.Message)"
        Write-Error $errorMessage
        return New-KeyfactorResult -Status Error -Code 899 -Step CatchAll -ErrorMessage $errorMessage
    }
}
