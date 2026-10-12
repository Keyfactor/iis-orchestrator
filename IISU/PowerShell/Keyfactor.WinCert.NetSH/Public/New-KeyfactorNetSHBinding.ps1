# WinNetSH module result codes (a separate range from Keyfactor.WinCert.Common's 0-530 range and
# WinLDAP's 700-799 range - see those modules' own header comments for their ranges).
# Code  Status   Step          Description
# 0     Success  BindSslCert   Operation completed successfully
# 801   Error    RemoveBinding Removing an existing/no-longer-needed sslcert binding failed
# 802   Error    BindSslCert   Adding the sslcert binding failed (Set-NetShSslCertBinding)
# 803   Skipped  RemoveCertificate  Certificate is still bound to at least one other sslcert entry
# 804   Skipped  RemoveCertificate  Certificate was not found in the target store
# 805   Error    RemoveCertificate  Unexpected error while removing certificate from store
# 899   Error    CatchAll      Unexpected/unhandled exception

function New-KeyfactorNetSHBinding {
    <#
    .SYNOPSIS
    Binds a certificate to a "netsh http sslcert" IP:Port or Hostname:Port entry.

    .DESCRIPTION
    Deletes any existing sslcert binding at the same key first (netsh has no "update" verb), then
    adds the binding with the new certificate. See Set-NetShSslCertBinding.ps1 for the AppId
    resolution rules used when -AppId is not supplied.
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
        [string]$HostName = "",

        [Parameter(Mandatory = $false)]
        [AllowEmptyString()]
        [string]$AppId = "",

        [Parameter(Mandatory = $true)]
        [string]$Thumbprint,

        [Parameter(Mandatory = $false)]
        [string]$StoreName = "My"
    )

    Write-Information "Entering PowerShell Script New-KeyfactorNetSHBinding"
    Write-Information "[VERBOSE] Parameters: $(($PSBoundParameters.GetEnumerator() | ForEach-Object { "$($_.Key): '$($_.Value)'" }) -join ', ')"

    $bindingKey = if ($HostName) { "${HostName}:${Port}" } else { "${IPAddress}:${Port}" }

    try {
        $setResult = Set-NetShSslCertBinding -IPAddress $IPAddress -Port $Port -HostName $HostName -Thumbprint $Thumbprint -StoreName $StoreName -AppId $AppId

        if (-not $setResult.Success) {
            Write-Error $setResult.ErrorMessage
            return New-KeyfactorResult -Status Error -Code 802 -Step BindSslCert `
                -ErrorMessage $setResult.ErrorMessage `
                -Details @{ Thumbprint = $Thumbprint; AppId = $setResult.AppId; AppIdSource = $setResult.AppIdSource }
        }

        Write-Information "The certificate '$Thumbprint' was bound to '$bindingKey' (AppId $($setResult.AppIdSource): $($setResult.AppId))."

        return New-KeyfactorResult -Status Success -Code 0 -Step BindSslCert `
            -Message "Certificate '$Thumbprint' bound to '$bindingKey'." `
            -Details @{ Thumbprint = $Thumbprint; AppId = $setResult.AppId; AppIdSource = $setResult.AppIdSource }
    }
    catch {
        $errorMessage = "Unexpected error in New-KeyfactorNetSHBinding: $($_.Exception.Message)"
        Write-Error $errorMessage
        return New-KeyfactorResult -Status Error -Code 899 -Step CatchAll `
            -ErrorMessage $errorMessage `
            -Details @{ Thumbprint = $Thumbprint }
    }
}
