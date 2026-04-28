function Remove-KeyfactorIISSiteBinding {
    [CmdletBinding()]
    [OutputType([pscustomobject])]
    param (
        [Parameter(Mandatory = $true)]
        [string]$SiteName,
        
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$BindingInfo,
        
        [Parameter(Mandatory = $false)]
        [System.Nullable[bool]]$UseIISDrive = $null
    )

    # Auto-detect IIS Drive availability if not explicitly provided
    if ($null -eq $UseIISDrive) {
        $UseIISDrive = Test-IISDrive
    }

    Write-Verbose "Removing existing bindings for exact match: $BindingInfo on site $SiteName (mimics IIS replace behavior)"

    try {
        if ($UseIISDrive) {
            Write-Verbose "Using IIS Drive to remove binding"
            $sitePath = "IIS:\Sites\$SiteName"
            $site = Get-Item $sitePath
            $httpsBindings = $site.Bindings.Collection | Where-Object {
                $_.bindingInformation -eq $BindingInfo -and $_.protocol -eq "https"
            }

            foreach ($binding in $httpsBindings) {
                $bindingInfo = $binding.GetAttributeValue("bindingInformation")
                $protocol = $binding.protocol

                Write-Verbose "Removing binding: $bindingInfo ($protocol)"
                Remove-WebBinding -Name $SiteName -BindingInformation $bindingInfo -Protocol $protocol -Confirm:$false
                Write-Verbose "Successfully removed binding"
            }
        }
        else {
            Write-Verbose "Using Web Administration assembly to remove binding"
            # ServerManager fallback
            Add-Type -Path "$env:windir\System32\inetsrv\Microsoft.Web.Administration.dll"
            $iis = New-Object Microsoft.Web.Administration.ServerManager
            $site = $iis.Sites[$SiteName]

            $httpsBindings = $site.Bindings | Where-Object {
                $_.BindingInformation -eq $BindingInfo -and $_.Protocol -eq "https"
            }

            foreach ($binding in $httpsBindings) {
                Write-Verbose "Removing binding: $($binding.BindingInformation)"
                $site.Bindings.Remove($binding)
                Write-Verbose "Successfully removed binding"
            }
            
            $iis.CommitChanges()
            Write-Verbose "Committed changes to IIS"
        }

        return New-KeyfactorResult -Status Success -Code 0 -Step RemoveBinding -Message "Successfully removed existing bindings"
    }
    catch {
        $errorMessage = "Error removing existing binding: $($_.Exception.Message)"
        Write-Warning $errorMessage
        return New-KeyfactorResult -Status Error -Code 201 -Step RemoveBinding -ErrorMessage $errorMessage
    }
}