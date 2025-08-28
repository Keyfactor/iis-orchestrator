# Update notes:
# 8/12/25   Updated functions to manage IIS bindings and certificates
#           Updated script to read CSPs correctly using newer CNG Keys
#		    Fix an error with complex PFX passwords having irregular characters

# Set preferences globally at the script level
$DebugPreference = "Continue"
$VerbosePreference = "Continue"
$InformationPreference = "Continue"

#Standard Step Names
# Step Name	        Purpose
# ValidateInput	    Validate required params and input data
# FindSite	        Checking if the IIS site exists
# CheckBinding	    Looking up existing bindings
# RemoveBinding	    Attempting to remove an old binding
# AddBinding	    Adding the new IIS binding
# LoadCertificate	Fetching or validating the SSL certificate
# CompareThumbprint	Checking if binding needs to be updated
# BindSSL	        Adding SSL cert to a binding
# ImportModules	    Importing IIS-related PowerShell modules
# CatchAll	        Fallback for unexpected or generic errors

# Standard Error Codes
#Code	Status	Description
# 0	    Success	Operation completed successfully
# 100	Skipped	Binding already exists and is up-to-date
# 101	Warning	Binding exists but is invalid
# 200	Error	Site not found
# 201	Error	Failed to remove binding
# 202	Error	Failed to add binding
# 203	Error	Certificate not found
# 204	Error	Certificate already in use elsewhere
# 205	Error	Thumbprint mismatch
# 206	Error	WebAdministration module missing
# 207	Error	IISAdministration module missing
# 300	Error	Unknown or unhandled exception

function New-ResultObject {
    param(
        [ValidateSet("Success", "Warning", "Error", "Skipped")]
        [string]$Status,
        [int]$Code,
        [string]$Step,
        [string]$Message,
        [string]$ErrorMessage = "",
        [hashtable]$Details = @{}
    )

    return [PSCustomObject]@{
        Status       = $Status
        Code         = $Code
        Step         = $Step
        Message      = $Message
        ErrorMessage = $ErrorMessage
        Details      = $Details
    }
}

function Get-KFCertificates {
    param (
        [string]$StoreName = "My"   # Default store name is "My" (Personal)
    )

    # Define the store path using the provided StoreName parameter
    $storePath = "Cert:\LocalMachine\$StoreName"

    # Check if the store path exists to ensure the store is valid
    if (-not (Test-Path $storePath)) {
        # Write an error message and exit the function if the store path is invalid
        Write-Error "The certificate store path '$storePath' does not exist. Please provide a valid store name."
        return
    }

    # Retrieve all certificates from the specified store
    $certificates = Get-ChildItem -Path $storePath

    # Initialize an empty array to store certificate information objects
    $certInfoList = @()

    foreach ($cert in $certificates) {
        try {
            # Create a custom object to store details about the current certificate
            $certInfo = [PSCustomObject]@{
                StoreName      = $StoreName                # Name of the certificate store
                Certificate    = $cert.Subject             # Subject of the certificate
                ExpiryDate     = $cert.NotAfter            # Expiration date of the certificate
                Issuer         = $cert.Issuer              # Issuer of the certificate
                Thumbprint     = $cert.Thumbprint          # Unique thumbprint of the certificate
                HasPrivateKey  = $cert.HasPrivateKey       # Indicates if the certificate has a private key
                SAN            = Get-KFSAN $cert           # Subject Alternative Names (if available)
                ProviderName   = Get-CertificateCSP $cert  # Provider of the certificate
                Base64Data     = [System.Convert]::ToBase64String($cert.RawData) # Encoded raw certificate data
            }

            # Add the certificate information object to the results array
            $certInfoList += $certInfo
        } catch {
            # Write a warning message if there is an error processing the current certificate
            Write-Warning "An error occurred while processing the certificate: $_"
        }
    }

    # Output the results in JSON format if certificates were found
    if ($certInfoList) {
        $certInfoList | ConvertTo-Json -Depth 10
    } else {
        # Write a warning if no certificates were found in the specified store
        Write-Warning "No certificates were found in the store '$StoreName'."
    }
}

function Get-KFIISBoundCertificates {
    $certificates = @()
    $totalBoundCertificates = 0

    try {
        Add-Type -Path "$env:windir\System32\inetsrv\Microsoft.Web.Administration.dll"  #  -AssemblyName "Microsoft.Web.Administration"
        $serverManager = New-Object Microsoft.Web.Administration.ServerManager
    } catch {
        Write-Error "Failed to create ServerManager. IIS might not be installed."
        return
    }

    $websites = $serverManager.Sites
    Write-Information "There were $($websites.Count) websites found."

    foreach ($site in $websites) {
        $siteName = $site.Name
        $siteBoundCertificateCount = 0

        foreach ($binding in $site.Bindings) {
            if ($binding.Protocol -eq 'https' -and $binding.CertificateHash) {

                $certHash = ($binding.CertificateHash | ForEach-Object { $_.ToString("X2") }) -join ""

                $storeName = if ($binding.CertificateStoreName) { $binding.CertificateStoreName } else { "My" }

                try {
                    $cert = Get-ChildItem -Path "Cert:\LocalMachine\$storeName" | Where-Object {
                        $_.Thumbprint -eq $certHash
                    }

                    if (-not $cert) {
                        Write-Warning "Certificate with thumbprint not found in Cert:\LocalMachine\$storeName"
                        continue
                    }

                    $certBase64 = [Convert]::ToBase64String($cert.RawData)

                    $ip, $port, $hostname = $binding.BindingInformation -split ":", 3

                    $certInfo = [PSCustomObject]@{
                        SiteName           = $siteName
                        Binding            = $binding.BindingInformation
                        IPAddress          = $ip
                        Port               = $port
                        Hostname           = $hostname
                        Protocol           = $binding.Protocol
                        SNI                = ($binding.SslFlags -band 1) -eq 1
                        ProviderName       = Get-CertificateCSP $cert
                        SAN                = Get-KFSAN $cert
                        Certificate        = $cert.Subject
                        ExpiryDate         = $cert.NotAfter
                        Issuer             = $cert.Issuer
                        Thumbprint         = $cert.Thumbprint
                        HasPrivateKey      = $cert.HasPrivateKey
                        CertificateBase64  = $certBase64
                    }

                    $certificates += $certInfo
                    $siteBoundCertificateCount++
                    $totalBoundCertificates++
                } catch {
                    Write-Warning "Could not retrieve certificate details for hash $certHash in store $storeName."
                    Write-Warning $_
                }
            }
        }

        Write-Information "Website: $siteName has $siteBoundCertificateCount bindings with certificates."
    }

    Write-Information "A total of $totalBoundCertificates bindings with valid certificates were found."

    if ($totalBoundCertificates -gt 0) {
        $certificates | ConvertTo-Json
    } else {
        Write-Information "No valid certificates were found bound to websites."
    }
}

function Add-KFCertificateToStore{
    param (
        [Parameter(Mandatory = $true)]
        [string]$Base64Cert,
    
        [Parameter(Mandatory = $false)]
        [string]$PrivateKeyPassword,
    
        [Parameter(Mandatory = $true)]
        [string]$StoreName,
    
        [Parameter(Mandatory = $false)]
        [string]$CryptoServiceProvider
    )

    try {
        Write-Information "Entering PowerShell Script Add-KFCertificate"
        Write-Verbose "Add-KFCertificateToStore - Received: StoreName: '$StoreName', CryptoServiceProvider: '$CryptoServiceProvider', Base64Cert: '$Base64Cert'"

        # Get the thumbprint of the passed in certificate
        # Convert password to secure string if provided, otherwise use $null
        $bytes = [System.Convert]::FromBase64String($Base64Cert)
        $securePassword = if ($PrivateKeyPassword) { ConvertTo-SecureString -String $PrivateKeyPassword -AsPlainText -Force } else { $null }

        # 
        $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($bytes, $securePassword, 18)
        $thumbprint = $cert.Thumbprint
        
        if (-not $thumbprint) { throw "Failed to get the certificate thumbprint.  The PFX may be invalid or the password is incorrect." }

        if ($CryptoServiceProvider) 
        {
            # Test to see if CSP exists
            if(-not (Test-CryptoServiceProvider -CSPName $CryptoServiceProvider))
            {
                Write-Information "INFO: The CSP $CryptoServiceProvider was not found on the system."
                Write-Warning "WARN: CSP $CryptoServiceProvider was not found on the system."
                return
            }

            Write-Information "Adding certificate with the CSP '$CryptoServiceProvider'"

            # Create temporary file for the PFX
            $tempPfx = [System.IO.Path]::GetTempFileName() + ".pfx"
            [System.IO.File]::WriteAllBytes($tempPfx, [Convert]::FromBase64String($Base64Cert))


            # Execute certutil based on whether a private key password was supplied
            try {
                # Start building certutil arguments
                $arguments = @('-f')

                if ($PrivateKeyPassword) {
                    Write-Verbose "Has a private key"
                    $arguments += '-p'
                    $arguments += $PrivateKeyPassword
                }

                if ($CryptoServiceProvider) {
                    Write-Verbose "Has a CryptoServiceProvider: $CryptoServiceProvider"
                    $arguments += '-csp'
                    $arguments += $CryptoServiceProvider
                }

                $arguments += '-importpfx'
                $arguments += $StoreName
                $arguments += $tempPfx

                # Quote any arguments with spaces
                $argLine = ($arguments | ForEach-Object {
                    if ($_ -match '\s') { '"{0}"' -f $_ } else { $_ }
                }) -join ' '

                write-Verbose "Running certutil with arguments: $argLine"

                # Setup process execution
                $processInfo = New-Object System.Diagnostics.ProcessStartInfo
                $processInfo.FileName = "certutil.exe"
                $processInfo.Arguments = $argLine.Trim()
                $processInfo.RedirectStandardOutput = $true
                $processInfo.RedirectStandardError = $true
                $processInfo.UseShellExecute = $false
                $processInfo.CreateNoWindow = $true

                $process = New-Object System.Diagnostics.Process
                $process.StartInfo = $processInfo

                $process.Start() | Out-Null

                $stdOut = $process.StandardOutput.ReadToEnd()
                $stdErr = $process.StandardError.ReadToEnd()

                $process.WaitForExit()

                if ($process.ExitCode -ne 0) {
                    throw "certutil failed with code $($process.ExitCode). Output:`n$stdOut`nError:`n$stdErr"
                }
            } catch {
                Write-Error "ERROR: $_"
            } finally {
                if (Test-Path $tempPfx) {
                    Remove-Item $tempPfx -Force
                }
            }

        } else {
            $certStore = New-Object -TypeName System.Security.Cryptography.X509Certificates.X509Store -ArgumentList $storeName, "LocalMachine"
            Write-Information "Store '$StoreName' is open." 
            $certStore.Open(5)
            $certStore.Add($cert)
            $certStore.Close();
            Write-Information "Store '$StoreName' is closed." 
        }

        Write-Information "The thumbprint '$thumbprint' was added to store $StoreName." 
        return $thumbprint
    } catch {
        Write-Error "An error occurred: $_" 
        return $null
    }
}

function Remove-KFCertificateFromStore {
    param (
        [string]$Thumbprint,
        [string]$StorePath,

        [parameter(ParameterSetName = $false)]
        [switch]$IsAlias
    )

    # Initialize a variable to track success
    $isSuccessful = $false

    try {
        # Open the certificate store
        $store = New-Object System.Security.Cryptography.X509Certificates.X509Store($StorePath, [System.Security.Cryptography.X509Certificates.StoreLocation]::LocalMachine)
        $store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)

        # Find the certificate by thumbprint or alias
        if ($IsAlias) {
            $cert = $store.Certificates | Where-Object { $_.FriendlyName -eq $Thumbprint }
        } else {
            $cert = $store.Certificates | Where-Object { $_.Thumbprint -eq $Thumbprint }
        }

        if ($cert) {
            # Remove the certificate from the store
            Write-Information "Attempting to remove certificate from store '$StorePath' with the thumbprint: $Thumbprint"
            $store.Remove($cert)
            Write-Information "Certificate removed successfully from store '$StorePath'"

            # Mark success
            $isSuccessful = $true
        } else {
            throw [System.Exception]::new("Certificate not found in $StorePath.")
        }

        # Close the store
        $store.Close()
    } catch {
        # Log and rethrow the exception
        Write-Error "An error occurred: $_"
        throw $_
    } finally {
        # Ensure the store is closed
        if ($store) {
            $store.Close()
        }
    }

    # Return the success status
    return $isSuccessful
}

function New-KFIISSiteBinding {
    [CmdletBinding()]
    [OutputType([pscustomobject])]
    param (
        [Parameter(Mandatory = $true)]
        [string]$SiteName,
        [string]$IPAddress = "*",
        [int]$Port = 443,
        [string]$Hostname = "",
        [ValidateSet("http", "https")]
        [string]$Protocol = "https",
        [string]$Thumbprint,
        [string]$StoreName = "My",
        [int]$SslFlags = 0
    )

    Write-Information "Entering PowerShell Script: New-KFIISSiteBinding" -InformationAction SilentlyContinue
    Write-Verbose "Entered New-KFIISSiteBinding with values SiteName: '$SiteName', IPAddress: '$IPAddress', Port: $Port, HostName: '$Hostname', Protocol: '$Protocol', Thumbprint: '$Thumbprint', Store Path: '$StoreName', SslFlags: '$SslFlags'"

    $result = $null

    # Check for existing binding conflict
    $conflicts = @(CheckExistingBindings -DesiredIP $IPAddress -DesiredPort $Port -DesiredHost $Hostname -TargetSiteName $SiteName)

    if ($conflicts.Count -gt 0) {
        $conflictMessage = "Binding conflict detected with the following existing bindings:`n" + ($conflicts | ForEach-Object { " - Site: $($_.SiteName), IP: $($_.BindingIP), Port: $($_.BindingPort), Host: $($_.BindingHost)" }) -join "`n"

        Write-Warning $conflictMessage -InformationAction SilentlyContinue

        $result = New-ResultObject -Status Skipped -Code 100 -Step CheckBinding -Message $msg -ErrorMessage $conflictMessage 

        return $result
    }
    Write-Verbose "No binding conflicts found for SiteName: '$SiteName', IPAddress: '$IPAddress', Port: $Port, HostName: '$Hostname'"

    $searchBindings = "${IPAddress}:${Port}:${Hostname}"
    $hasIISDrive = Ensure-IISDrive
    Write-Verbose "IIS Drive is available: $hasIISDrive"

    if ($hasIISDrive) {
        
        Write-Verbose "IIS Drive is available, using WebAdministration module."

        $null = Import-Module WebAdministration
        $sitePath = "IIS:\Sites\$SiteName"
        if (-not (Test-Path $sitePath)) {
            $msg = "Site '$SiteName' not found in IIS drive."
            Write-Error $msg -InformationAction SilentlyContinue
            $result = New-ResultObject -Status Error -Code 201 -Step FindWebSite -Message $msg -Details @{ SiteName = $SiteName; IPAddress = $IPAddress; Port = $Port; HostName = $Hostname }
        } else {
            $site = Get-Item $sitePath
            $httpsBindings = $site.Bindings.Collection | Where-Object {
                $_.bindingInformation -eq $searchBindings -and $_.protocol -eq "https"
        }

            foreach ($binding in $httpsBindings) {
                try {
                    $bindingInfo = $binding.GetAttributeValue("bindingInformation")
                    $protocol    = $binding.protocol

                    Write-Verbose "Calling Remove-WebBinding -Name $SiteName -BindingInformation $bindingInfo -Protocol $protocol -Confirm:$false"
                    Remove-WebBinding -Name $SiteName -BindingInformation $bindingInfo -Protocol $protocol -Confirm:$false
                    Write-Verbose "Completed removing the Web Binding"

                } catch {
                    $msg = "Error removing binding '$($binding.bindingInformation)': $_"
                    Write-Warning $msg -InformationAction SilentlyContinue
                    $result = New-ResultObject -Status Error -Code 201 -Step RemoveBinding -ErrorMessage $msg
                    return $result
                }
            }

            # Site2 then has Test1 cert assigned to it??
            try {
                Write-Verbose "Calling New-WebBinding -Name $SiteName -Protocol $Protocol -IPAddress $IPAddress -Port $Port -HostHeader '$Hostname' -SslFlags $SslFlags"
                New-WebBinding -Name $SiteName -Protocol $Protocol -IPAddress $IPAddress -Port $Port -HostHeader $Hostname -SslFlags $SslFlags
            } catch {
                $msg = "Error adding binding: $_"
                Write-Warning $msg -InformationAction SilentlyContinue
                $result = New-ResultObject -Status Error -Code 202 -Step AddBinding -ErrorMessage $msg
                return $result
            }

            Write-Verbose "Calling Get-WebBinding -Name $SiteName -Protocol $Protocol, Where BindingInformation equals '$searchBindings'"
            $binding = Get-WebBinding -Name $SiteName -Protocol $Protocol | Where-Object {
                $_.bindingInformation -eq $searchBindings
            }

            try
            {
                if ($binding) {
                    Write-Verbose "Binding thumbprint $thumbprint to $binding.bindingInformation in store: $StoreName"
                    $null = $binding.AddSslCertificate($Thumbprint, $StoreName)
                    $result = New-ResultObject -Status Success -Code 0 -Step BindSSL
                } else {
                    $result = New-ResultObject -Status Error -Code 202 -Step BindSSL -Message "No binding found for: $searchBindings"
                }
            }
            catch
            {
                $result = New-ResultObject -Status Error -Code 202 -Step BindSSL -Message $_
            }
        }
    } else {
        # SERVERMANAGER FALLBACK
        Write-Verbose "IIS Drive is not available, using ServerManager fallback."

        Add-Type -Path "$env:windir\System32\inetsrv\Microsoft.Web.Administration.dll"
        $iis = New-Object Microsoft.Web.Administration.ServerManager
        $site = $iis.Sites[$SiteName]

        if ($null -eq $site) {
            $msg = "Site '$SiteName' not found in ServerManager."
            Write-Error $msg -InformationAction SilentlyContinue
            $result = New-ResultObject -Status Error -Code 201 -Step FindWebSite -Message $msg -Details @{ SiteName = $SiteName; IPAddress = $IPAddress; Port = $Port; HostName = $Hostname }
        } else {
            $httpsBindings = $site.Bindings | Where-Object {
                $_.bindingInformation -eq $searchBindings -and $_.protocol -eq "https"
            }

            foreach ($binding in $httpsBindings) {
                try {
                    $site.Bindings.Remove($binding)
                } catch {
                    $msg = "Error removing binding: $_"
                    Write-Warning $msg -InformationAction SilentlyContinue
                    $result = New-ResultObject -Status Error -Code 201 -Step RemoveBinding -ErrorMessage $msg
                    return $result
                }
            }

            $cleanThumbprint = $Thumbprint -replace '[^a-fA-F0-9]', ''
            $hashBytes = -split $cleanThumbprint -replace '..', '$& ' -split ' ' | Where-Object { $_ -ne '' } | ForEach-Object { [Convert]::ToByte($_, 16) }

            try {
                $newBinding = $site.Bindings.Add($searchBindings, $Protocol)
                if ($Protocol -eq "https") {
                    $newBinding.CertificateStoreName = $StoreName
                    $newBinding.CertificateHash = [byte[]]$hashBytes
                    $newBinding.SetAttributeValue("sslFlags", $SslFlags)
                }
                $iis.CommitChanges()
                $result = New-ResultObject -Status Success -Code 0 -Step BindSSL -Message "Binding and certificate successfully applied via ServerManager."
            } catch {
                $msg = "Error adding binding: $_"
                Write-Warning $msg -InformationAction SilentlyContinue
                $result = New-ResultObject -Status Error -Code 202 -Step BindSSL -ErrorMessage $msg
            }
        }
    }

    return $result
}

function CheckExistingBindings {
    param (
        [string]$DesiredIP,
        [string]$DesiredPort,
        [string]$DesiredHost,
        [string]$TargetSiteName
    )

    $conflicts = @()

    if (Ensure-IISDrive) {
        $null = Import-Module WebAdministration

        Get-Website | Where-Object { $_.Name -ne $TargetSiteName } | ForEach-Object {
            $siteName = $_.Name
            $_.Bindings.Collection | ForEach-Object {
                $parts = $_.bindingInformation.Split(':')
                $bindingIP = $parts[0]
                $bindingPort = $parts[1]
                $bindingHost = if ($_.HostHeader) { $_.HostHeader } else { "" }

                if (
                    $bindingIP -eq $DesiredIP -and
                    $bindingPort -eq $DesiredPort -and
                    $bindingHost -eq $DesiredHost
                ) {
                    $conflicts += [pscustomobject]@{
                        SiteName     = $siteName
                        BindingIP    = $bindingIP
                        BindingPort  = $bindingPort
                        BindingHost  = $bindingHost
                    }
                }
            }
        }

        return @($conflicts)
    }
    else {
        # SERVERMANAGER FALLBACK
        Add-Type -Path "$env:windir\System32\inetsrv\Microsoft.Web.Administration.dll"
        $iis = New-Object Microsoft.Web.Administration.ServerManager

        foreach ($site in $iis.Sites) {
            if ($site.Name -ne $TargetSiteName) {
                foreach ($binding in $site.Bindings) {
                    $bindingInfo = $binding.BindingInformation.Split(':')
                    $bindingIP = $bindingInfo[0]
                    $bindingPort = $bindingInfo[1]
                    $bindingHost = $binding.Host

                    if (
                        $bindingIP -eq $DesiredIP -and
                        $bindingPort -eq $DesiredPort -and
                        ($bindingHost -eq $DesiredHost -or ($bindingHost -eq $null -and $DesiredHost -eq ""))
                    ) {
                        $conflicts += [pscustomobject]@{
                            SiteName     = $site.Name
                            BindingIP    = $bindingIP
                            BindingPort  = $bindingPort
                            BindingHost  = $bindingHost
                        }
                    }
                }
            }
        }

        return $conflicts
    }
}

function CheckExistingBindingsORIG {
    param (
        [string]$DesiredIP,
        [string]$DesiredPort,
        [string]$DesiredHost,
        [string]$TargetSiteName
    )

    if (Ensure-IISDrive) {
        $null = Import-Module WebAdministration
 
        $conflict = $false
 
        Get-Website | Where-Object { $_.Name -ne $TargetSiteName } | ForEach-Object {
            $siteName = $_.Name
            $_.Bindings.Collection | ForEach-Object {
                $parts = $_.bindingInformation.Split(':')
                $bindingIP = $parts[0]
                $bindingPort = $parts[1]
                $bindingHost = if ($_.HostHeader) { $_.HostHeader } else { "" }

                if (
                    $bindingIP -eq $DesiredIP -and
                    $bindingPort -eq $DesiredPort -and
                    $bindingHost -eq $DesiredHost
                ) {
                    Write-Verbose "⚠️ Conflict found in site '$siteName' with binding: $($DesiredIP):$($DesiredPort):$($DesiredHost)"
                    $conflict = $true
                }
            }
        }
 
        return $conflict
    }
    else {
        # SERVERMANAGER FALLBACK
        Add-Type -Path "$env:windir\System32\inetsrv\Microsoft.Web.Administration.dll"
        $iis = New-Object Microsoft.Web.Administration.ServerManager

        $conflict = $false

        $site = $iis.Sites[$SiteName]
        foreach ($site in $iis.Sites) {
            if ($site.Name -ne $TargetSiteName) {
                foreach ($binding in $site.Bindings) {
                    $bindingInfo = $binding.BindingInformation.Split(':')
                    $bindingIP = $bindingInfo[0]
                    $bindingPort = $bindingInfo[1]
                    $bindingHost = $binding.Host

                    if (
                        $bindingIP -eq $DesiredIP -and
                        $bindingPort -eq $DesiredPort -and
                        ($bindingHost -eq $DesiredHost -or ($bindingHost -eq $null -and $DesiredHost -eq ""))
                    ) {
                        $conflict = $true
                    }
                }
            }
        }

        return $conflict
    }
}

function Ensure-IISDrive {
    [CmdletBinding()]
    param ()

    # Try to import the WebAdministration module if not already loaded
    if (-not (Get-Module -Name WebAdministration)) {
        try {
            $null = Import-Module WebAdministration -ErrorAction Stop
        }
        catch {
            Write-Warning "WebAdministration module could not be imported. IIS:\ drive will not be available."
            return $false
        }
    }

    # Check if IIS drive is available
    if (-not (Get-PSDrive -Name 'IIS' -ErrorAction SilentlyContinue)) {
        Write-Warning "IIS:\ drive not available. Ensure IIS is installed and the WebAdministration module is imported."
        return $false
    }

    return $true
}

function Remove-KFIISSiteBinding {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)] [string] $SiteName,
        [Parameter(Mandatory = $true)] [string] $IPAddress,
        [Parameter(Mandatory = $true)] [int]    $Port,
        [Parameter(Mandatory = $false)] [string] $Hostname
    )

    Write-Verbose "Entering PowerShell Scrip Remove-KFIISiteBinding with arguments Sitename: '$SiteName', IP Address: '$IPAddress', Port: $Port, Hostname: '$Hostname'"

    try {
        Add-Type -Path "$env:windir\System32\inetsrv\Microsoft.Web.Administration.dll"
    } catch {
        throw "Failed to load Microsoft.Web.Administration. Ensure IIS is installed on the remote server."
    }

    try {
        $serverManager = New-Object Microsoft.Web.Administration.ServerManager
        $site = $serverManager.Sites | Where-Object { $_.Name -eq $SiteName }

        if (-not $site) {
            Write-Information "Site '$SiteName' not found."
            return $true
        }

        $searchBindingInfo = if ($HostName) { "$IPAddress`:$Port`:$HostName" } else { "$IPAddress`:$Port`:" }

        Write-Verbose "Searching Site for bindings: $searchBindingInfo"
        $httpsBinding = $site.Bindings | Where-Object { $_.bindingInformation -eq $searchBindingInfo -and $_.protocol -eq 'https' }

        if ($httpsBinding)
        {
            $site.Bindings.Remove($httpsBinding)
            $serverManager.CommitChanges()
            Write-Verbose "Removed binding $httpsBinding from site '$SiteName'."

            return $true
        }
        else
        {
            Write-Information "No matching binding found for $searchBindingInfo in site '$SiteName'."
            return $true
        }
    } catch {
        Write-Error "An error occurred while removing the binding."
        Write-Error $_
        throw
    }
}

function Remove-KFIISCertificateIfUnused {
    param (
        [Parameter(Mandatory = $true)]
        [string]$Thumbprint,

        [Parameter(Mandatory = $false)]
        [string]$StoreName = "My"  # Default to the personal store

    )

    try {
        Add-Type -Path "$env:windir\System32\inetsrv\Microsoft.Web.Administration.dll"
    } catch {
        throw "Failed to load Microsoft.Web.Administration. Ensure IIS is installed on the remote server."
    }

    # Normalize thumbprint (remove spaces and make uppercase)
    $thumbprint = $Thumbprint -replace '\s', '' | ForEach-Object { $_.ToUpperInvariant() }

    try {
        $serverManager = New-Object Microsoft.Web.Administration.ServerManager

        $bindings = @()

        foreach ($site in $serverManager.Sites) {
            foreach ($binding in $site.Bindings) {
                if ($binding.Protocol -eq 'https' -and $binding.CertificateHash) {
                    $bindingThumbprint = ($binding.CertificateHash | ForEach-Object { $_.ToString("X2") }) -join ""
                    if ($bindingThumbprint -eq $thumbprint) {
                        $bindings += [PSCustomObject]@{
                            SiteName  = $site.Name
                            Binding   = $binding.BindingInformation
                        }
                    }
                }
            }
        }

        if ($bindings.Count -gt 0) {
            Write-Warning "The certificate with thumbprint $thumbprint is still used by the following bindings:"
            $bindings | Format-Table -AutoSize | Out-String | Write-Warning
            return
        }

        # Certificate is not used in any bindings
        $cert = Get-ChildItem -Path "Cert:\LocalMachine\$StoreName" | Where-Object { $_.Thumbprint -eq $thumbprint }

        if (-not $cert) {
            Write-Warning "Certificate with thumbprint $thumbprint not found in Cert:\LocalMachine\$StoreName"
            return
        }

        Remove-Item -Path $cert.PSPath -Force
        Write-Information "Certificate $thumbprint has been removed from the store."

    } catch {
        Write-Error "An error occurred while attempting to delete IIS Certificate: $_"
    }
}


# Function to get certificate information for a SQL Server instance
function GET-KFSQLInventory {
    # Retrieve all SQL Server instances
    $sqlInstances = (Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server").InstalledInstances
    Write-Information "There are $($sqlInstances.Count) instances that will be checked for certificates."

    # Dictionary to store instance names by thumbprint
    $commonInstances = @{}

    # First loop: gather thumbprints for each instance
    foreach ($instance in $sqlInstances) {
        Write-Information "Checking instance: $instance for Certificates."

        # Get the registry path for the SQL instance
        $fullInstanceName = Get-ItemPropertyValue "HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL" -Name $instance
        $regLocation = "HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\$fullInstanceName\MSSQLServer\SuperSocketNetLib"
        
        try {
            # Retrieve the certificate thumbprint from the registry
            $thumbprint = (Get-ItemPropertyValue -Path $regLocation -Name "Certificate" -ErrorAction Stop).ToUpper()
            
            if ($thumbprint) {
                # Store instance names by thumbprint
                if ($commonInstances.ContainsKey($thumbprint)) {
                    $commonInstances[$thumbprint] += ",$instance"
                } else {
                    $commonInstances[$thumbprint] = $instance
                }
            }
        } catch {
            Write-Information "No certificate found for instance: $instance."
        }
    }

    # Array to store results
    $myBoundCerts = @()

    # Second loop: process each unique thumbprint and gather certificate data
    foreach ($kp in $commonInstances.GetEnumerator()) {
        $thumbprint = $kp.Key
        $instanceNames = $kp.Value

        # Find the certificate in the local machine store
        $certStore = "My"
        $cert = Get-ChildItem -Path "Cert:\LocalMachine\$certStore\$thumbprint" -ErrorAction SilentlyContinue

        if ($cert) {
            # Create a hashtable with the certificate parameters
            $sqlSettingsDict = @{
                InstanceName = $instanceNames
                ProviderName = $cert.PrivateKey.CspKeyContainerInfo.ProviderName
            }

            # Build the inventory item for this certificate
            $inventoryItem = [PSCustomObject]@{
                Certificates     = [Convert]::ToBase64String($cert.RawData)
                Alias            = $thumbprint
                PrivateKeyEntry  = $cert.HasPrivateKey
                UseChainLevel    = $false
                ItemStatus       = "Unknown"  # OrchestratorInventoryItemStatus.Unknown equivalent
                Parameters       = $sqlSettingsDict
            }

            # Add the inventory item to the results array
            $myBoundCerts += $inventoryItem
        }
    }

    # Return the array of inventory items
    return $myBoundCerts  | ConvertTo-Json
}

function Bind-KFSqlCertificate { 
    param (
        [string]$SQLInstance,
        [string]$RenewalThumbprint,
        [string]$NewThumbprint,
        [switch]$RestartService = $false
    )

    function Get-SqlCertRegistryLocation($InstanceName) {
        return "HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\$InstanceName\MSSQLServer\SuperSocketNetLib"
    }

    $bindingSuccess = $true  # Track success/failure

    try {
        $SQLInstances = $SQLInstance -split ',' | ForEach-Object { $_.Trim() }

        foreach ($instance in $SQLInstances) {
            try {
                $fullInstance = Get-ItemPropertyValue -Path "HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL" -Name $instance -ErrorAction Stop
                $regLocation = Get-SqlCertRegistryLocation -InstanceName $fullInstance

                if (-not (Test-Path $regLocation)) {
                    Write-Error "Registry location not found: $regLocation"
                    $bindingSuccess = $false
                    continue
                }
                Write-Verbose "Instance: $instance"
                Write-Verbose "Full Instance: $fullInstance"
                Write-Verbose "Registry Location: $regLocation"
                Write-Verbose "Current Thumbprint: $currentThumbprint"

                $currentThumbprint = Get-ItemPropertyValue -Path $regLocation -Name "Certificate" -ErrorAction SilentlyContinue

                if ($RenewalThumbprint -and $RenewalThumbprint -contains $currentThumbprint) {
                    Write-Information "Renewal thumbprint matches for instance: $fullInstance"
                    $result = Set-KFCertificateBinding -InstanceName $instance -NewThumbprint $NewThumbprint -RestartService:$RestartService
                } elseif (-not $RenewalThumbprint) {
                    Write-Information "No renewal thumbprint provided. Binding certificate to instance: $fullInstance"
                    $result = Set-KFCertificateBinding -InstanceName $instance -NewThumbprint $NewThumbprint -RestartService:$RestartService
                }

                if (-not $result) {
                    Write-Error "Failed to bind certificate for instance: $instance"
                    $bindingSuccess = $false
                }
            }
            catch {
                Write-Error "Error processing instance '$instance': $($_.Exception.Message)"
                $bindingSuccess = $false
            }
        }
    } 
    catch {
        Write-Error "An unexpected error occurred: $($_.Exception.Message)"
        return $false
    }

    return $bindingSuccess
}

function Set-KFCertificateBinding {
    param (
        [string]$InstanceName,
        [string]$NewThumbprint,
        [switch]$RestartService
    )

    Write-Information "Binding certificate with thumbprint $NewThumbprint to instance $InstanceName..."

    try {
        # Get the full SQL instance name from the registry
        $fullInstance = Get-ItemPropertyValue "HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL" -Name $InstanceName -ErrorAction Stop
        $RegistryPath = Get-SqlCertRegistryLocation -InstanceName $fullInstance

        Write-Verbose "Full instance: $fullInstance"
        Write-Verbose "Registry Path: $RegistryPath"

        # Attempt to update the registry
        try {
            Set-ItemProperty -Path $RegistryPath -Name "Certificate" -Value $NewThumbprint -ErrorAction Stop
            Write-Information "Updated registry for instance $InstanceName with new thumbprint."
        }
        catch {
            Write-Error "Failed to update registry at {$RegistryPath}: $_"
            throw $_  # Rethrow the error to ensure it's caught at a higher level
        }

        # Retrieve SQL Server service user
        $serviceName = Get-SqlServiceName -InstanceName $InstanceName
        $serviceInfo = Get-CimInstance -ClassName Win32_Service -Filter "Name='$serviceName'" -ErrorAction Stop
        $SqlServiceUser = $serviceInfo.StartName

        if (-not $SqlServiceUser) {
            throw "Unable to retrieve service account for SQL Server instance: $InstanceName"
        }

        Write-Verbose "Service Name: $serviceName"
        Write-Verbose "SQL Service User: $SqlServiceUser"
        Write-Information "SQL Server service account for ${InstanceName}: $SqlServiceUser"

        # Retrieve the certificate
        $Cert = Get-ChildItem Cert:\LocalMachine\My | Where-Object { $_.Thumbprint -eq $NewThumbprint }
        if (-not $Cert) {
            throw "Certificate with thumbprint $NewThumbprint not found in LocalMachine\My store."
        }

        # Retrieve private key path
        $privKey = $Cert.PrivateKey.CspKeyContainerInfo.UniqueKeyContainerName
        $keyPath = "$($env:ProgramData)\Microsoft\Crypto\RSA\MachineKeys\"
        $privKeyPath = Get-Item "$keyPath\$privKey" -ErrorAction Stop
        Write-Information "Private Key Path is: $privKeyPath"

        try {
            # Set ACL for the certificate private key
            $Acl = Get-Acl -Path $privKeyPath -ErrorAction Stop
            $Ar = New-Object System.Security.AccessControl.FileSystemAccessRule($SqlServiceUser, "Read", "Allow")
            $Acl.SetAccessRule($Ar)
        
            Set-Acl -Path $privKeyPath.FullName -AclObject $Acl -ErrorAction Stop
            Write-Information "Updated ACL for private key at $privKeyPath."
        }
        catch {
            Write-Error "Failed to update ACL on the private key: $_"
            throw $_
        }

        # Optionally restart the SQL Server service
        if ($RestartService) {
            try {
                Write-Information "Restarting SQL Server service: $serviceName..."
                Restart-Service -Name $serviceName -Force -ErrorAction Stop
                Write-Information "SQL Server service restarted successfully."
            }
            catch {
                Write-Error "Failed to restart SQL Server service: $_"
                throw $_
            }
        }

        Write-Information "Certificate binding completed for instance $InstanceName."
    }
    catch {
        Write-Error "An error occurred: $_"
        return $false
    }

    return $true
}

function Unbind-KFSqlCertificate {
    param (
        [string]$SQLInstanceNames,   # Comma-separated list of SQL instances
        [switch]$RestartService      # Restart SQL Server after unbinding
    )

    $unBindingSuccess = $true  # Track success/failure

    try {
        
        $instances = $SQLInstanceNames -split ',' | ForEach-Object { $_.Trim() }

        foreach ($instance in $instances) {
            try {
                # Resolve full instance name from registry
                $fullInstance = Get-ItemPropertyValue "HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL" -Name $instance
                $regPath = "HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\$fullInstance\MSSQLServer\SuperSocketNetLib"

                # Get current thumbprint
                $certificateThumbprint = Get-ItemPropertyValue -Path $regPath -Name "Certificate" -ErrorAction SilentlyContinue

                if ($certificateThumbprint) {
                    Write-Information "Unbinding certificate from SQL Server instance: $instance (Thumbprint: $certificateThumbprint)"
                
                    # Instead of deleting, set to an empty string to prevent SQL startup issues
                    Set-ItemProperty -Path $regPath -Name "Certificate" -Value ""

                    Write-Information "Successfully unbound certificate from SQL Server instance: $instance."
                } else {
                    Write-Warning "No certificate bound to SQL Server instance: $instance."
                }

                # Restart service if required
                if ($RestartService) {
                    $serviceName = Get-SqlServiceName -InstanceName $instance
                    Write-Information "Restarting SQL Server service: $serviceName..."
                    Restart-Service -Name $serviceName -Force
                    Write-Information "SQL Server service restarted successfully."
                }
            }
            catch {
                Write-Error "Failed to unbind certificate from instance: $instance. Error: $_"
                $unBindingSuccess = $false
            }
        }
    }
    catch {
        Write-Error "An unexpected error occurred: $($_.Exception.Message)"
        return $false
    }

    return $unBindingSuccess
}

# Example usage:
# Bind-CertificateToSqlInstance -Thumbprint "123ABC456DEF" -SqlInstanceName "MSSQLSERVER"

function Get-SqlServiceName {
    param (
        [string]$InstanceName
    )
    if ($InstanceName -eq "MSSQLSERVER") {
        return "MSSQLSERVER" # Default instance
    } else {
        return "MSSQL`$$InstanceName" # Named instance (escape $ for PowerShell strings)
    }
}

function Get-SQLServiceUser {
    param (
        [Parameter(Mandatory = $true)]
        [string]$SQLServiceName
    )

    # Use Get-CimInstance instead of Get-WmiObject
    $serviceUser = (Get-CimInstance -ClassName Win32_Service -Filter "Name='$SQLServiceName'").StartName

    if ($serviceUser) {
        return $serviceUser
    } else {
        Write-Error "SQL Server instance '$SQLInstanceName' not found or no service user associated."
        return $null
    }
}

# Example usage:
# Get-SQLServiceUser -SQLInstanceName "MSSQLSERVER"

##### ReEnrollment functions
function New-CSREnrollment {
    param (
        [string]$SubjectText,
        [string]$ProviderName = "Microsoft Strong Cryptographic Provider",
        [string]$KeyType,
        [string]$KeyLength,
        [string]$SAN
    )

    if ([string]::IsNullOrWhiteSpace($ProviderName)) {
        $ProviderName = "Microsoft Strong Cryptographic Provider"
    }

    # Validate the Crypto Service Provider
    Validate-CryptoProvider -ProviderName $ProviderName

    # Parse Subject for any escaped commas
    $parsedSubject = Parse-DNSubject $SubjectText

    # Build the SAN entries if provided
    $sanContent = ""
    if ($SAN) {
        $sanEntries = $SAN -split "&"
        $sanDirectives = $sanEntries | ForEach-Object { "_continue_ = `"$($_)&`"" }
        $sanContent = @"
[Extensions]
2.5.29.17 = `"{text}`"
$($sanDirectives -join "`n")
"@
    }

    # Generate INF file content for the CSR
    $infContent = @"
[Version]
Signature=`"$`Windows NT$`"

[NewRequest]
Subject = "$parsedSubject"
ProviderName = "$ProviderName"
MachineKeySet = True
HashAlgorithm = SHA256
KeyAlgorithm = $KeyType
KeyLength = $KeyLength
KeySpec = 0

$sanContent
"@

    Write-Verbose "INF Contents: $infContent"

    # Path to temporary INF file
    $infFile = [System.IO.Path]::GetTempFileName() + ".inf"
    $csrOutputFile = [System.IO.Path]::GetTempFileName() + ".csr"

    Set-Content -Path $infFile -Value $infContent
    Write-Information "Generated INF file at: $infFile"

    try {
        # Run certreq to generate CSR
        $certReqCommand = "certreq -new -q `"$infFile`" `"$csrOutputFile`""
        Write-Information "Running certreq: $certReqCommand"

        # Capture the output and errors
        $certReqOutput = & certreq -new -q $infFile $csrOutputFile 2>&1

        # Check the exit code of the command
        if ($LASTEXITCODE -ne 0) {
            $errMsg = "Certreq failed with exit code $LASTEXITCODE. Output: $certReqOutput"
            throw $errMsg
        }

        # If successful, proceed
        Write-Information "Certreq completed successfully."

        # Read CSR file
        if (Test-Path $csrOutputFile) {
            $csrContent = Get-Content -Path $csrOutputFile -Raw
            Write-Information "CSR successfully created at: $csrOutputFile"
            return $csrContent
        } else {
            throw "Failed to create CSR file."
        }
    } catch {
        Write-Error $_
    } finally {
        # Clean up temporary files
        if (Test-Path $infFile) {
            Remove-Item -Path $infFile -Force
            Write-Information "Deleted temporary INF file."
        }

        if (Test-Path $csrOutputFile) {
            Remove-Item -Path $csrOutputFile -Force
            Write-Information "Deleted temporary CSR file."
        }
    }
}

function Import-SignedCertificate {
    param (
        [Parameter(Mandatory = $true)]
        [byte[]]$RawData,               # RawData from the certificate

        [Parameter(Mandatory = $true)]
        [string]$StoreName              # Store to which the certificate should be imported
    )

    try {
        # Step 1: Convert raw certificate data to Base64 string with line breaks
        Write-Verbose "Converting raw certificate data to Base64 string."
        $csrData = [System.Convert]::ToBase64String($RawData, [System.Base64FormattingOptions]::InsertLineBreaks)

        # Step 2: Create PEM-formatted certificate content
        Write-Verbose "Creating PEM-formatted certificate content."
        $certContent = @(
            "-----BEGIN CERTIFICATE-----"
            $csrData
            "-----END CERTIFICATE-----"
        ) -join "`n"

        # Step 3: Create a temporary file for the certificate
        Write-Verbose "Creating a temporary file for the certificate."
        $cerFilename = [System.IO.Path]::GetTempFileName()
        Set-Content -Path $cerFilename -Value $certContent -Force
        Write-Verbose "Temporary certificate file created at: $cerFilename"

        # Step 4: Import the certificate into the specified store
        Write-Verbose "Importing the certificate to the store: Cert:\LocalMachine\$StoreName"
        Set-Location -Path "Cert:\LocalMachine\$StoreName"

        $importResult = Import-Certificate -FilePath $cerFilename
        if ($importResult) {
            Write-Verbose "Certificate successfully imported to Cert:\LocalMachine\$StoreName."
        } else {
            throw "Certificate import failed."
        }

        # Step 5: Cleanup temporary file
        if (Test-Path $cerFilename) {
            Remove-Item -Path $cerFilename -Force
            Write-Verbose "Temporary file deleted: $cerFilename"
        }

        # Step 6: Return the imported certificate's thumbprint
        return $importResult.Thumbprint

    } catch {
        Write-Error "An error occurred during the certificate export and import process: $_"
    }
}
#####

# Shared Functions
# Function to return the certificate's thumbprint
function Get-PfxThumbprint {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [string]$Base64Cert,

        [Parameter(Mandatory = $false)]
        [string]$Password
    )

    try {
        # Convert Base64 to byte array
        $pfxBytes = [Convert]::FromBase64String($Base64Cert)

        # Convert password to secure string if provided, otherwise use $null
        $securePassword = if ($Password) { ConvertTo-SecureString -String $Password -AsPlainText -Force } else { $null }

        # Import certificate
        $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2
        $cert.Import($pfxBytes, $securePassword, [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::Exportable)

        # Return thumbprint (formatted)
        return $cert.Thumbprint.Replace(" ", "").ToUpper()
    }
    catch {
        Write-Error "Failed to load PFX: $_"
        return $null
    }
}

# Function to get SAN (Subject Alternative Names) from a certificate
function Get-KFSAN($cert) {
    $san = $cert.Extensions | Where-Object { $_.Oid.FriendlyName -eq "Subject Alternative Name" }
    if ($san) {
        return ($san.Format(1) -split ", " -join "; ")
    }
    return $null
}

#Function to verify if the given CSP is found on the computer
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

# Function that takes an x509 certificate object and returns the csp
function Get-CertificateCSP {
    param (
        [Parameter(Mandatory = $true)]
        [System.Security.Cryptography.X509Certificates.X509Certificate2]$Cert
    )

    # Check if the certificate has a private key
    if (-not $Cert.HasPrivateKey) {
        Write-Warning "Certificate does not have a private key associated with it"
        return $null
    }

    $privateKey = $Cert.PrivateKey
    if ($privateKey) {
        # For older .NET Framework
        $cspKeyContainerInfo = $privateKey.CspKeyContainerInfo

        if ($cspKeyContainerInfo) {
            return $cspKeyContainerInfo.ProviderName
        }
    }

    try {
        $key = [System.Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPrivateKey($cert)
        if ($key -and $key.GetType().Name -eq "RSACng") {
            $cngKey = $key.Key
                
            return $cngKey.Provider.Provider
        }
    }
    catch {
        Write-Warning "CNG key detection failed: $($_.Exception.Message)"
        return $null
    }
}

# Function that takes an x509 certificate object and returns the csp
function Get-CertificateCSPOLD {
    param (
        [Parameter(Mandatory = $true)]
        [System.Security.Cryptography.X509Certificates.X509Certificate2]$Cert
    )

    # Check if the certificate has a private key
    if ($Cert -and $Cert.HasPrivateKey) {
        try {
            $key = $Cert.PrivateKey

            if ($key -is [System.Security.Cryptography.RSACryptoServiceProvider]) {
                # CAPI-based key
                return $key.CspKeyContainerInfo.ProviderName
            }
            elseif ($key -is [System.Security.Cryptography.RSACng]) {
                # CNG-based key
                return $key.Key.Provider
            }
            else {
                return "Unknown provider type: $($key.GetType().FullName)"
            }
        } catch {
            Write-Warning "Could not access provider information: $_"
            return $null
        }
    } else {
        Write-Warning "Certificate has no private key."
        return $null
    }
}

function Get-CryptoProviders {
    # Retrieves the list of available Crypto Service Providers using certutil
    try {
        Write-Verbose "Retrieving Crypto Service Providers using certutil..."
        $certUtilOutput = certutil -csplist
        
        # Parse the output to extract CSP names
        $cspInfoList = @()
        foreach ($line in $certUtilOutput) {
            if ($line -match "Provider Name:") {
                $cspName = ($line -split ":")[1].Trim()
                $cspInfoList += $cspName
            }
        }

        if ($cspInfoList.Count -eq 0) {
            throw "No Crypto Service Providers were found. Ensure certutil is functioning properly."
        }

        Write-Verbose "Retrieved the following CSPs:"
        $cspInfoList | ForEach-Object { Write-Verbose $_ }

        return $cspInfoList
    } catch {
        throw "Failed to retrieve Crypto Service Providers: $_"
    }
}

function Validate-CryptoProvider {
    param (
        [Parameter(Mandatory)]
        [string]$ProviderName
    )
    Write-Verbose "Validating CSP: $ProviderName"

    $availableProviders = Get-CryptoProviders

    if (-not ($availableProviders | Where-Object { $_.Trim().ToLowerInvariant() -eq $ProviderName.Trim().ToLowerInvariant() })) {

        throw "Crypto Service Provider '$ProviderName' is either invalid or not found on this system."
    }

    Write-Verbose "Crypto Service Provider '$ProviderName' is valid."
}

function Parse-DNSubject {
    <#
    .SYNOPSIS
        Parses a Distinguished Name (DN) subject string and properly quotes RDN values containing escaped commas.
    
    .DESCRIPTION
        This function takes a DN subject string and parses the Relative Distinguished Name (RDN) components,
        adding proper quotes around values that contain escaped commas and escaping quotes for use in 
        PowerShell here-strings. Only RDN values with escaped commas get quoted.
    
    .PARAMETER Subject
        The DN subject string to parse (e.g., "CN=Keyfactor,O=Keyfactor\, Inc")
    
    .EXAMPLE
        Parse-DNSubject -Subject "CN=Keyfactor,O=Keyfactor\, Inc"
        Returns: CN=Keyfactor,O=""Keyfactor, Inc""
    
    .EXAMPLE
        Parse-DNSubject -Subject "CN=Test User,O=Company\, LLC,OU=IT Department\, Security"
        Returns: CN=Test User,O=""Company, LLC"",OU=""IT Department, Security""
    #>
    
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true, ValueFromPipeline = $true)]
        [string]$Subject
    )
    
    # Initialize variables
    $parsedComponents = @()
    $currentComponent = ""
    $i = 0
    
    # Convert string to character array for easier parsing
    $chars = $Subject.ToCharArray()
    
    while ($i -lt $chars.Length) {
        $char = $chars[$i]
        
        # Check if we hit a comma
        if ($char -eq ',') {
            # Look back to see if it's escaped
            $isEscaped = $false
            if ($i -gt 0 -and $chars[$i-1] -eq '\') {
                $isEscaped = $true
            }
            
            if ($isEscaped) {
                # This is an escaped comma, add it to current component
                $currentComponent += $char
            } else {
                # This is a separator comma, finish current component
                if ($currentComponent.Trim() -ne "") {
                    $parsedComponents += $currentComponent.Trim()
                    $currentComponent = ""
                }
            }
        } else {
            # Regular character, add to current component
            $currentComponent += $char
        }
        
        $i++
    }
    
    # Add the last component
    if ($currentComponent.Trim() -ne "") {
        $parsedComponents += $currentComponent.Trim()
    }
    
    # Process each component to add quotes where needed
    $processedComponents = @()
    
    foreach ($component in $parsedComponents) {
        # Split on first equals sign to get attribute and value
        $equalIndex = $component.IndexOf('=')
        if ($equalIndex -gt 0) {
            $attribute = $component.Substring(0, $equalIndex).Trim()
            $value = $component.Substring($equalIndex + 1).Trim()
            
            # Clean up escaped commas first
            $cleanValue = $value -replace '\\,', ','
            
            # Check if original value had escaped commas (needs quotes)
            if ($value -match '\\,') {
                # This RDN value had escaped commas, so wrap in doubled quotes and escape quotes
                $escapedValue = $cleanValue -replace '"', '""'
                $processedComponents += "$attribute=`"`"$escapedValue`"`""
            } else {
                # No escaped commas, keep as simple value but escape any existing quotes
                $escapedValue = $cleanValue -replace '"', '""'
                $processedComponents += "$attribute=$escapedValue"
            }
        } else {
            # Invalid component format, keep as is
            $processedComponents += $component
        }
    }
    
    # Join components back together (no outer quotes needed since it goes in PowerShell string)
    $subjectString = ($processedComponents -join ',')
    return $subjectString
}