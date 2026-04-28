function Add-IISBindingWithSSL {
    [CmdletBinding()]
    [OutputType([pscustomobject])]
    param (
        [Parameter(Mandatory = $true)]
        [string]$SiteName,
        
        [Parameter(Mandatory = $true)]
        [string]$Protocol,
        
        [Parameter(Mandatory = $true)]
        [string]$IPAddress,
        
        [Parameter(Mandatory = $true)]
        [int]$Port,
        
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$Hostname,
        
        [string]$Thumbprint,
        
        [string]$StoreName = "My",
        
        [int]$SslFlags = 0,
        
        [Parameter(Mandatory = $true)]
        [bool]$UseIISDrive
    )

    Write-Verbose "Adding binding: Protocol=$Protocol, IP=$IPAddress, Port=$Port, Host='$Hostname'"

    try {
        if ($UseIISDrive) {
            # Add binding using WebAdministration module
            $bindingParams = @{
                Name = $SiteName
                Protocol = $Protocol
                IPAddress = $IPAddress
                Port = $Port
                SslFlags = $SslFlags
            }
            
            # Only add HostHeader if it's not empty (New-WebBinding doesn't like empty strings)
            if (-not [string]::IsNullOrEmpty($Hostname)) {
                $bindingParams.HostHeader = $Hostname
            }
            
            Write-Verbose "Creating new web binding with parameters: $(($bindingParams.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join ', ')"
            New-WebBinding @bindingParams

            # Bind SSL certificate if HTTPS
            if ($Protocol -eq "https" -and -not [string]::IsNullOrEmpty($Thumbprint)) {
                $searchBindings = "${IPAddress}:${Port}:${Hostname}"
                Write-Verbose "Searching for binding: $searchBindings"
                
                $binding = Get-WebBinding -Name $SiteName -Protocol $Protocol | Where-Object {
                    $_.bindingInformation -eq $searchBindings
                }

                if ($binding) {
                    Write-Verbose "Binding SSL certificate with thumbprint: $Thumbprint"
                    $null = $binding.AddSslCertificate($Thumbprint, $StoreName)
                    Write-Verbose "SSL certificate successfully bound"
                    return New-KeyfactorResult -Status Success -Code 0 -Step BindSSL -Message "Binding and SSL certificate successfully applied"
                } else {
                    return New-KeyfactorResult -Status Error -Code 202 -Step BindSSL -ErrorMessage "No binding found for: $searchBindings"
                }
            }
            else {
                return New-KeyfactorResult -Status Success -Code 0 -Step AddBinding -Message "HTTP binding successfully added"
            }
        }
        else {
            # ServerManager fallback
            Add-Type -Path "$env:windir\System32\inetsrv\Microsoft.Web.Administration.dll"
            $iis = New-Object Microsoft.Web.Administration.ServerManager
            $site = $iis.Sites[$SiteName]

            $searchBindings = "${IPAddress}:${Port}:${Hostname}"
            $newBinding = $site.Bindings.Add($searchBindings, $Protocol)

            if ($Protocol -eq "https" -and -not [string]::IsNullOrEmpty($Thumbprint)) {
                # Clean and convert thumbprint to byte array
                $cleanThumbprint = $Thumbprint -replace '[^a-fA-F0-9]', ''
                $hashBytes = for ($i = 0; $i -lt $cleanThumbprint.Length; $i += 2) {
                    [Convert]::ToByte($cleanThumbprint.Substring($i, 2), 16)
                }

                $newBinding.CertificateStoreName = $StoreName
                $newBinding.CertificateHash = [byte[]]$hashBytes
                $newBinding.SetAttributeValue("sslFlags", $SslFlags)
            }

            $iis.CommitChanges()
            return New-KeyfactorResult -Status Success -Code 0 -Step BindSSL -Message "Binding and certificate successfully applied via ServerManager"
        }
    }
    catch {
        $errorMessage = "Error adding binding with SSL: $($_.Exception.Message)"
        Write-Warning $errorMessage
        return New-KeyfactorResult -Status Error -Code 202 -Step AddBinding -ErrorMessage $errorMessage
    }
}