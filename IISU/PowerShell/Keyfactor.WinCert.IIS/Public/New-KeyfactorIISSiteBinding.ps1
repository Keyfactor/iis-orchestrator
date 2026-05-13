function New-KeyfactorIISSiteBinding {
    [CmdletBinding()]
    [OutputType([pscustomobject])]
    param (
        [Parameter(Mandatory = $true)]
        [string]$SiteName,
        [string]$IPAddress = "*",
        [int]$Port = 443,
        [AllowEmptyString()]
        [string]$Hostname = "",
        [ValidateSet("http", "https")]
        [string]$Protocol = "https",
        [ValidateScript({
            if ($Protocol -eq 'https' -and [string]::IsNullOrEmpty($_)) {
                throw "Thumbprint is required when Protocol is 'https'"
            }
            $true
        })]
        [string]$Thumbprint,
        [string]$StoreName = "My",
        [int]$SslFlags = 0
    )

    Write-Information "Entering PowerShell Script: New-KFIISSiteBinding" -InformationAction SilentlyContinue
    Write-Information "[VERBOSE] Parameters: $(($PSBoundParameters.GetEnumerator() | ForEach-Object { "$($_.Key): '$($_.Value)'" }) -join ', ')"

    try {
        # Step 1: Perform verifications and get management info
        # Check SslFlags
        $sslValidationResult = Test-ValidSslFlags -Flags $SslFlags
        if (-not $sslValidationResult.IsValid) {
            return New-KeyfactorResult -Status Error -Code 400 -Step "SSL Validation" -ErrorMessage $sslValidationResult.Message
        }

        $managementInfo = Get-IISManagementInfo -SiteName $SiteName
        if (-not $managementInfo.Success) {
            return $managementInfo.Result
        }

        # Step 2: Remove existing HTTPS bindings for this binding info
        $searchBindings = "${IPAddress}:${Port}:${Hostname}"
        Write-Information "[VERBOSE] Removing existing HTTPS bindings for: $searchBindings"
    
        $removalResult = Remove-KeyfactorIISSiteBinding -SiteName $SiteName -BindingInfo $searchBindings -UseIISDrive $managementInfo.UseIISDrive
        if ($removalResult.Status -eq 'Error') {
            return $removalResult
        }

        # Step 3: Determine SslFlags supported by Microsoft.Web.Administration
        if ($SslFlags -gt 3) {
            Write-Information "[VERBOSE] SslFlags value $SslFlags exceeds managed API range (0-3). Applying reduced flags for creation."
            $SslFlagsApplied = ($SslFlags -band 3)
        } else {
            $SslFlagsApplied = $SslFlags
        }

        # Step 4: Add the new binding with the reduced flag set
        Write-Information "[VERBOSE] Adding new binding with SSL certificate (SslFlagsApplied=$SslFlagsApplied)"
    
        $addParams = @{
            SiteName    = $SiteName
            Protocol    = $Protocol
            IPAddress   = $IPAddress
            Port        = $Port
            Hostname    = $Hostname
            Thumbprint  = $Thumbprint
            StoreName   = $StoreName
            SslFlags    = $SslFlagsApplied
            UseIISDrive = $managementInfo.UseIISDrive
        }
    
        $addResult = Add-IISBindingWithSSL @addParams

        if ($addResult.Status -eq 'Error') {
            return $addResult
        }

        # Step 5: If extended flags, update via appcmd.exe
        if ($SslFlags -gt 3) {
            Write-Information "[VERBOSE] Applying full SslFlags=$SslFlags via appcmd"

            $appcmd = Join-Path $env:windir "System32\inetsrv\appcmd.exe"

            # Escape any single quotes in hostname
            $safeHostname = $Hostname -replace "'", "''"
            $bindingInfo = "${IPAddress}:${Port}:${safeHostname}"

            # Quote site name only if it contains spaces
            if ($SiteName -match '\s') {
                $siteArg = "/site.name:`"$SiteName`""
            } else {
                $siteArg = "/site.name:$SiteName"
            }

            # Build binding argument for appcmd
            $bindingArg = "/bindings.[protocol='https',bindingInformation='$bindingInfo'].sslFlags:$SslFlags"

            Write-Information "[VERBOSE] Running appcmd: $appcmd $siteArg $bindingArg"
            $appcmdOutput = & $appcmd set site $siteArg $bindingArg 2>&1
            Write-Information "[VERBOSE] appcmd output: $appcmdOutput"
        
            #& $appcmd set site $siteArg $bindingArg | Out-Null

            if ($LASTEXITCODE -ne 0) {
                Write-Warning "appcmd failed to set extended SslFlags ($SslFlags) for binding $bindingInfo."
            } else {
                Write-Information "[VERBOSE] Successfully updated SslFlags to $SslFlags via appcmd."
            }
        }

        return $addResult
    }
    catch {
        $errorMessage = "Unexpected error in New-KFIISSiteBinding: $($_.Exception.Message)"
        Write-Error $errorMessage
        return New-KeyfactorResult -Status Error -Code 999 -Step UnexpectedError -ErrorMessage $errorMessage
    }
}