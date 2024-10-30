function Get-KFCertificates
{
    param (
        [string]$StoreName = "My"   # Default store name is "My" (Personal)
    )

    # Get all certificates from the specified store
    $certificates = Get-ChildItem -Path "Cert:\LocalMachine\$StoreName"

    # Initialize an array to store the results
    $certInfoList = @()

    foreach ($cert in $certificates) {
        # Create a custom object to store the certificate information
        $certInfo = [PSCustomObject]@{
            StoreName      = $StoreName
            Certificate    = $cert.Subject
            ExpiryDate     = $cert.NotAfter
            Issuer         = $cert.Issuer
            Thumbprint     = $cert.Thumbprint
            HasPrivateKey  = $cert.HasPrivateKey
            SAN            = Get-KFSAN $cert
            ProviderName   = Get-CertificateCSP $cert 
            Base64Data     = [System.Convert]::ToBase64String($cert.RawData)
        }
        
        # Add the certificate information to the array
        $certInfoList += $certInfo
    }

    # Output the results
    if ($certInfoList) {
        $certInfoList | ConvertTo-Json
    }
}

function Get-KFIISBoundCertificates
{
    # Import the WebAdministration module
    Import-Module IISAdministration
    #Import-Module WebAdministration

    # Get all websites
    #$websites = Get-Website
    $websites = Get-IISSite

    Write-Information "There were ${websites}.count found"

    # Initialize an array to store the results
    $certificates = @()

    foreach ($site in $websites) {
        # Get the site name
        $siteName = $site.name
        
        # Get the bindings for the site
        #$bindings = Get-WebBinding -Name $siteName
        $bindings = Get-IISSiteBinding -Name $siteName
        
        foreach ($binding in $bindings) {
            # Check if the binding has an SSL certificate
            if ($binding.protocol -eq 'https') {
                # Get the certificate hash
                #$certHash = $binding.certificateHash
                $certHash = $binding.RawAttributes.certificateHash
                
                # Get the certificate store
                $StoreName = $binding.certificateStoreName
                
                # Get the certificate details from the certificate store
                $cert = Get-ChildItem -Path "Cert:\LocalMachine\$StoreName\$certHash"

                $certBase64 = [Convert]::ToBase64String($cert.RawData)

                # Create a custom object to store the results
                $certInfo = [PSCustomObject]@{
                    SiteName       = $siteName
                    Binding        = $binding.bindingInformation
                    IPAddress      = ($binding.bindingInformation -split ":")[0]
                    Port           = ($binding.bindingInformation -split ":")[1]
                    Hostname       = ($binding.bindingInformation -split ":")[2]
                    Protocol       = $binding.protocol
                    SNI            = $binding.sslFlags -eq 1
                    ProviderName   = Get-CertificateCSP $cert
                    SAN            = Get-KFSAN $cert
                    Certificate    = $cert.Subject
                    ExpiryDate     = $cert.NotAfter
                    Issuer         = $cert.Issuer
                    Thumbprint     = $cert.Thumbprint
                    HasPrivateKey  = $cert.HasPrivateKey
                    CertificateBase64 = $certBase64
                }
                
                # Add the certificate information to the array
                $certificates += $certInfo
            }
        }
    }

    # Output the results
    if ($certificates) {
        $certificates | ConvertTo-Json
    }
 
}

function Add-KFCertificateToStore
{
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
        # Before we get started, if a CSP has been provided, make sure it exists
        if($CryptoServiceProvider)
        {
            if(-not (Test-CryptoServiceProvider -CSPName $CryptoServiceProvider)){
                Write-Information "INFO: The CSP $CryptoServiceProvider was not found on the system."
                Write-Warning "WARN: CSP $CryptoServiceProvider was not found on the system."
                return
            }
        }

        # Convert Base64 string to byte array
        $certBytes = [Convert]::FromBase64String($Base64Cert)

        # Create a temporary file to store the certificate
        $tempStoreName = [System.IO.Path]::GetTempFileName()
        $tempPfxPath = [System.IO.Path]::ChangeExtension($tempStoreName, ".pfx")
        [System.IO.File]::WriteAllBytes($tempPfxPath, $certBytes)

        #$tempPfxPath = [System.IO.Path]::ChangeExtension($tempStoreName, ".pfx")

        $thumbprint = $null

        if ($CryptoServiceProvider) {
            Write-Information "Adding certificate with the CSP '$CryptoServiceProvider'"
            # Create a temporary PFX file
            $tempPfxPath = [System.IO.Path]::ChangeExtension($tempStoreName, ".pfx")
            $pfxPassword = if ($PrivateKeyPassword) { $PrivateKeyPassword } else { "" }

            # Create the PFX from the certificate
            $pfxCert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($tempStoreName, $pfxPassword, [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::Exportable)
    
            # Export the PFX to the temporary file
            $pfxBytes = $pfxCert.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Pfx, $pfxPassword)
            [System.IO.File]::WriteAllBytes($tempPfxPath, $pfxBytes)

            #$pfxCert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($tempStoreName, $pfxPassword, [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::Exportable)
            #$pfxCert.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Pfx, $pfxPassword) | Set-Content -Encoding Byte -Path $tempPfxPath

            # Use certutil to import the PFX with the specified CSP
            $importCmd = "certutil -f -importpfx -csp '$CryptoServiceProvider' -p '$pfxPassword' $StoreName $tempPfxPath"
            write-host $importCmd
            Invoke-Expression $importCmd

            #$importCmd = "certutil -f -importpfx $tempPfxPath -p $pfxPassword -csp `"$CryptoServiceProvider`""
            #Invoke-Expression $importCmd

            # Retrieve the thumbprint after the import
            $cert = Get-ChildItem -Path "Cert:\LocalMachine\$StoreName" | Where-Object { $_.Subject -like "*$($pfxCert.Subject)*" } | Sort-Object NotAfter -Descending | Select-Object -First 1
            if ($cert) {
                $thumbprint = $cert.Thumbprint
            }

            # Clean up the temporary PFX file
            Remove-Item $tempPfxPath
        } else {
            # Load the certificate from the temporary file
            if ($PrivateKeyPassword) {
                Write-Information "Writing the certificate using a Private Key Password." 
                Write-Information "Temp Store Name: $tempStoreName" 
                Write-Information "Temp PFX Name: $tempPfxPath" 
                #$flags = [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::PersistKeySet -bor [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::Exportable
                $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($tempPfxPath, $PrivateKeyPassword, 18)
            } else { 
                Write-Information "No Private Key Password was provided." 
                $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($tempPfxPath)
            }

            # Open the certificate store
            $store = New-Object System.Security.Cryptography.X509Certificates.X509Store($StoreName, [System.Security.Cryptography.X509Certificates.StoreLocation]::LocalMachine)
            $store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
            Write-Information "Store '$StoreName' is open." 

            # Add the certificate to the store
            Write-Information "About to add the certificate to the store." 
            $store.Add($cert)
            Write-Information "Certificate was added." 

            # Get the thumbprint so it can be returned to the calling function
            $thumbprint = $cert.Thumbprint
            Write-Information "The thumbprint '$thumbprint' was created." 

            # Close the store
            $store.Close()
            Write-Information "Store is closed." 
        }

        # Clean up the temporary file
        Remove-Item $tempStoreName

        Write-Host "Certificate added successfully to $StoreName." 
        return $thumbprint
    } catch {
        Write-Error "An error occurred: $_" 
        return $null
    }
}

function Remove-KFCertificateFromStore 
{
    param (
        [string]$Thumbprint,
        [string]$StorePath,

        [parameter(ParameterSetName = $false)]
        [switch]$IsAlias
    )

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
            $store.Remove($cert)
            Write-Info "Certificate removed successfully from $StorePath."
        } else {
            Write-Error "Certificate not found in $StorePath."
        }

        # Close the store
        $store.Close()
    } catch {
        Write-Error "An error occurred: $_"
    }
}

function New-KFIISSiteBinding
{
    param (
        [Parameter(Mandatory = $true)]
        [string]$Thumbprint,
        [Parameter(Mandatory = $true)]
        [string]$WebSite,
        [Parameter(Mandatory = $true)]
        [string]$Protocol,
        [Parameter(Mandatory = $true)]
        [string]$IPAddress,
        [Parameter(Mandatory = $true)]
        [int32]$Port,
        [Parameter(Mandatory = $False)]
        [string]$HostName,
        [Parameter(Mandatory = $true)]
        [string]$SNIFlag,
        [Parameter(Mandatory = $true)]
        [string]$StoreName
    )

    Write-Information "INFO:  Entered Add-CertificateToWebsites."
    Write-Information "Thumbprint:  $Thumbprint"
    Write-Information "Website: $WebSite"
    Write-Information "IPAddress: $IPAddress"
    Write-Information "Port: $Port"
    Write-Information "HostName: $HostName"
    Write-Information "Store Path: $StoreName"

    Write-Information "Attempting to load WebAdministration"
    Import-Module WebAdministration -ErrorAction Stop
    Write-Information "Web Administration module has been loaded and will be used"

    try {
            # Get the certificate from the store from the supplied thumbprint
            $certificate = Get-KFCertificateByThumbprint -Thumbprint $Thumbprint -StoreName $StoreName
            if (-not $certificate) {
                Write-Error "Certificate with thumbprint: $thumbprint could not be retrieved.  Exiting IIS Binding."
                return
            }

            $bindingInfo = "$($IPAddress):$($Port):$($HostName)"
            $Binding = Get-WebBinding -Name $Website | Where-Object {$_.bindingInformation -eq $bindingInfo}

            if ($binding) {
                $bindingInfo = "*:{$Port}:$HostName"
                $bindingItem = Get-Item "IIS:\SslBindings\$bindingInfo"

                # Check if the binding already has a certificate thumbprint
                $existingThumbprint = (Get-ItemProperty -Path $bindingItem.PSPath -Name CertificateThumbprint).CertificateThumbprint

                if ($existingThumbprint -ne $cert.Thumbprint) {
                    # Update the binding with the new SSL certificate's thumbprint
                    Set-ItemProperty -Path $bindingItem.PSPath -Name CertificateThumbprint -Value $cert.Thumbprint
                    Write-Output "Updated binding with new certificate thumbprint."
                } else {
                    Write-Output "The binding already has the correct certificate thumbprint."
                }
            } else {
                # If the binding doesn't exist, create it
                New-WebBinding -Name $Website -Protocol $Protocol -Port $Port -IPAddress $IPAddress -HostHeader $HostName -SslFlags $SNIFlag
                $NewBinding = Get-WebBinding -Name $Website -Protocol $Protocol -Port $port -IPAddress $IPAddress
                $NewBinding.AddSslCertificate($Certificate.Thumbprint, $StorePath)
                Write-Output "Created new binding and assigned certificate thumbprint."
            }

    } catch {
        Write-Host "ERROR: An error occurred while binding the certificate: $_"
    }
}

function Remove-KFIISBinding 
{
    param (
        [Parameter(Mandatory=$true)]
        [string]$SiteName,        # The name of the IIS website

        [Parameter(Mandatory=$true)]
        [string]$IPAddress,       # The IP address of the binding

        [Parameter(Mandatory=$true)]
        [int]$Port,               # The port number (e.g., 443 for HTTPS)

        [Parameter(Mandatory=$false)]
        [string]$Hostname         # The hostname (empty string for binding without hostname)
    )

                # Import WebAdministration module if it's not already imported
    if (-not (Get-Module -ListAvailable -Name WebAdministration)) {
        Import-Module WebAdministration
    }

    try {
        # Build the Binding Information format (IP:Port:Hostname)
        $bindingInfo = "$($IPAddress):$($Port):$($HostName)"

        # Get all HTTPS bindings for the site
        $bindings = Get-WebBinding -Name $SiteName -Protocol "https"

        if (-not $bindings) {
            Write-Host "No HTTPS bindings found for site '$SiteName'."
            return
        }

        # Find the binding that matches the provided IP, Port, and Hostname
        $bindingToRemove = $bindings | Where-Object {
            $_.bindingInformation -eq $bindingInfo
        }

        if (-not $bindingToRemove) {
            Write-Host "No matching HTTPS binding found with IP '$IPAddress', Port '$Port', and Hostname '$Hostname' for site '$SiteName'."
            return
        }

        # Remove the binding from IIS
        Remove-WebBinding -Name $SiteName -IPAddress $IPAddress -Port $Port -HostHeader $Hostname -Protocol "https"
        Write-Host "Removed HTTPS binding from site '$SiteName' (IP: $IPAddress, Port: $Port, Hostname: $Hostname)."

    } catch {
        Write-Error "An error occurred while trying to remove the certificate binding: $_"
    }
}

# Function to get certificate information for a SQL Server instance
function Get-SQLInstanceCertInfo {
    param (
        [string]$instanceName
    )
    
    $regPath = "HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\$instanceName\MSSQLServer\SuperSocketNetLib"
    $certInfo = Get-ItemProperty -Path $regPath -ErrorAction SilentlyContinue
    
    if ($certInfo) {
        $certHash = $certInfo.Certificate
        $certStore = "My"  # Certificates are typically stored in the Personal store

        if ($certHash) {
            $cert = Get-ChildItem -Path "Cert:\LocalMachine\$certStore\$certHash" -ErrorAction SilentlyContinue
            
            if ($cert) {
                return [PSCustomObject]@{
                    InstanceName   = $instanceName
                    Certificate    = $cert.Subject
                    ExpiryDate     = $cert.NotAfter
                    Issuer         = $cert.Issuer
                    Thumbprint     = $cert.Thumbprint
                    HasPrivateKey  = $cert.HasPrivateKey
                    SAN            = ($cert.Extensions | Where-Object { $_.Oid.FriendlyName -eq "Subject Alternative Name" }).Format(1)
                }
            }
        }
    }
}

function GET-KFSQLInventory
{
    # Get all SQL Server instances
    $sqlInstances = (Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server").InstalledInstances

    # Initialize an array to store the results
    $certificates = @()

    foreach ($instance in $sqlInstances) {
        # Get the SQL Full Instance Name
        $fullInstanceName = Get-ItemPropertyValue "HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL" -Name $instance

        $certInfo = Get-SQLInstanceCertInfo -instanceName $fullInstanceName
        $certificates += $certInfo
    }

    # Output the results
    $certificates | Format-Table -AutoSize
}

function Set-SQLCertificateAcl {
    param (
        [Parameter(Mandatory = $true)]
        [string]$Thumbprint,

        [Parameter(Mandatory = $true)]
        [string]$SqlServiceUser
    )

    # Get the certificate from the LocalMachine store
    $certificate = Get-ChildItem -Path "Cert:\LocalMachine\My" | Where-Object { $_.Thumbprint -eq $Thumbprint }

    if (-not $certificate) {
        Write-Error "Certificate with thumbprint $Thumbprint not found in LocalMachine\My store."
        return $null
    }

    # Retrieve the private key information
    $privKey = $certificate.PrivateKey.CspKeyContainerInfo.UniqueKeyContainerName
    $keyPath = "$($env:ProgramData)\Microsoft\Crypto\RSA\MachineKeys\"
    $privKeyPath = (Get-Item "$keyPath\$privKey")

    # Retrieve the current ACL for the private key
    $Acl = Get-Acl $privKeyPath
    $Ar = New-Object System.Security.AccessControl.FileSystemAccessRule($SqlServiceUser, "Read", "Allow")

    # Add the new access rule
    $Acl.SetAccessRule($Ar)

    # Set the new ACL on the private key file
    Set-Acl -Path $privKeyPath.FullName -AclObject $Acl

    Write-Output "ACL updated successfully for the private key."
}

function Bind-CertificateToSqlInstance {
    param (
        [Parameter(Mandatory = $true)]
        [string]$Thumbprint,            # Thumbprint of the certificate to bind
        [Parameter(Mandatory = $true)]
        [string]$SqlInstanceName,       # Name of the SQL Server instance (e.g., 'MSSQLSERVER' or 'SQLInstanceName')
        [string]$StoreName = "My",      # Certificate store name (default is 'My')
        [ValidateSet("LocalMachine", "CurrentUser")]
        [string]$StoreLocation = "LocalMachine", # Store location (default is 'LocalMachine')
        [Parameter(Mandatory = $false)]
        [switch]$RestartService         # Optional, restart sql instance if set to true
    )

    try {
        # Open the certificate store
        $store = New-Object System.Security.Cryptography.X509Certificates.X509Store($StoreName, [System.Security.Cryptography.X509Certificates.StoreLocation]::$StoreLocation)
        $store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadOnly)

        # Find the certificate by thumbprint
        $certificate = $store.Certificates | Where-Object { $_.Thumbprint -eq $Thumbprint }

        if (-not $certificate) {
            throw "Certificate with thumbprint $Thumbprint not found in store $StoreLocation\$StoreName."
        }

        # Get the SQL Full Instance Name
        $fullInstanceName = Get-ItemPropertyValue "HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL" -Name $SqlInstanceName

        # Update ACL for the private key, giving the SQL service user read access
        $SQLServiceUser = Get-SQLServiceUser $fullInstanceName
        Set-SQLCertificalACL -Thumbprint $Thumbprint -SQLServiceUser $SQLServiceUser
        
        # Get the SQL Server instance registry path
        $regPath = "HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\${fullInstanceName}\MSSQLServer\SuperSocketNetLib"
        if (-not (Test-Path $regPath)) {
            throw "Could not find registry path for SQL instance: $fullInstanceName."
        }

        # Set the certificate thumbprint in SQL Server's registry
        Set-ItemProperty -Path $regPath -Name "Certificate" -Value $Thumbprint
        Write-Information "Certificate thumbprint $Thumbprint successfully bound to SQL Server instance $SqlInstanceName."

        # Close the certificate store
        $store.Close()

        # Restart SQL Server for changes to take effect
        if ($RestartService.IsPresent) {
            Write-Information "Restarting SQL Server service..."
            Restart-Service -Name $SQLServiceUser -Force
            Write-Information "SQL Server service restarted."
        } else {
            Write-Information "Please restart SQL Server service manually for changes to take effect."
        }

    } catch {
        Write-Error "An error occurred: $_"
    }
}

# Example usage:
# Bind-CertificateToSqlInstance -Thumbprint "123ABC456DEF" -SqlInstanceName "MSSQLSERVER"

function UnBind-KFSqlServerCertificate {
    param (
        [Parameter(Mandatory = $true)]
        [string]$SqlInstanceName       # Name of the SQL Server instance (e.g., 'MSSQLSERVER' or 'SQLInstanceName')
    )

    try {

        # Get the SQL Full Instance Name
        $fullInstanceName = Get-ItemPropertyValue "HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL" -Name $SqlInstanceName

        # Get the SQL Server instance registry path
        $regPath = "HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\${fullInstanceName}\MSSQLServer\SuperSocketNetLib"
        if (-not (Test-Path $regPath)) {
            throw "Could not find registry path for SQL instance: $fullInstanceName."
        }

        # Check if the registry contains the certificate thumbprint
        $certificateThumbprint = (Get-ItemProperty -Path $regPath -Name "Certificate").Certificate

        if ($certificateThumbprint) 
        {
            Write-Host "Found certificate thumbprint: $certificateThumbprint bound to SQL Server instance $SqlInstanceName."
            Remove-ItemProperty -Path $regPath -Name "Certificate"
            Write-Output "Certificate thumbprint unbound from SQL Server instance $SqlInstanceName."
            return
        } else {
            Write-Output "No certificate is bound to SQL Server instance $SqlInstanceName."
            return
        }

    } catch {
        Write-Error "An error occurred: $_"
    }
}

# Example usage:
# Clear-SqlServerCertificate -SqlInstanceName "MSSQLSERVER"


function Get-SQLServiceUser {
    param (
        [Parameter(Mandatory = $true)]
        [string]$SQLInstanceName
    )

    # Construct the SQL service name (assuming default MSSQL naming convention)
    $serviceName = if ($SQLInstanceName -eq "MSSQLSERVER") { "MSSQLSERVER" } else { "MSSQL$SQLInstanceName" }

    # Use Get-CimInstance instead of Get-WmiObject
    $serviceUser = (Get-CimInstance -ClassName Win32_Service -Filter "Name='$serviceName'").StartName

    if ($serviceUser) {
        return $serviceUser
    } else {
        Write-Error "SQL Server instance '$SQLInstanceName' not found or no service user associated."
        return $null
    }
}

# Example usage:
# Get-SQLServiceUser -SQLInstanceName "MSSQLSERVER"



# Shared Functions
# Function to get SAN (Subject Alternative Names) from a certificate
function Get-KFSAN($cert) 
{
    $san = $cert.Extensions | Where-Object { $_.Oid.FriendlyName -eq "Subject Alternative Name" }
    if ($san) {
        return ($san.Format(1) -split ", " -join "; ")
    }
    return $null
}

#Function to verify if the given CSP is found on the computer
function Test-CryptoServiceProvider
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$CSPName
    )

    # Function to get the list of installed Cryptographic Service Providers from the registry
    function Get-CryptoServiceProviders {
        $cspRegistryPath = "HKLM:\SOFTWARE\Microsoft\Cryptography\Defaults\Provider"

        # Retrieve all CSP names from the registry
        $providers = Get-ChildItem -Path $cspRegistryPath | Select-Object -ExpandProperty PSChildName

        return $providers
    }

    # Get the list of installed CSPs
    $installedCSPs = Get-CryptoServiceProviders

    # Check if the user-provided CSP exists in the list
    if ($installedCSPs -contains $CSPName) {
        return $true
    }
    else {
        return $false
    }
}

# Function that takes an x509 certificate object and returns the csp
function Get-CertificateCSP 
{
    param (
        [Parameter(Mandatory = $true)]
        [System.Security.Cryptography.X509Certificates.X509Certificate2]$Cert
    )

    # Check if the certificate has a private key
    if ($Cert -and $Cert.HasPrivateKey) {
        try {
            # Access the certificate's private key to get CSP info
            $key = $Cert.PrivateKey

            # Retrieve the Provider Name from the private key
            $cspName = $key.Key.Provider.Provider
            
            # Return the CSP name
            return $cspName
        } catch {
            return $null
        }
    } else {
        return $null
    }
}

# This function returns a certificate object based upon the store and thumbprint received
function Get-KFCertificateByThumbprint 
{
    param (
        [Parameter(Mandatory = $true)]
        [string]$Thumbprint,

        [Parameter(Mandatory = $true)]
        [string]$StoreName
    )

    try {
        
        [System.Security.Cryptography.X509Certificates.StoreLocation]$StoreLocation = [System.Security.Cryptography.X509Certificates.StoreLocation]::LocalMachine
   
        # Open the specified certificate store
        $store = New-Object System.Security.Cryptography.X509Certificates.X509Store($StoreName, [System.Security.Cryptography.X509Certificates.StoreLocation]::$StoreLocation)
        $store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadOnly)

        # Find the certificate by thumbprint
        $certificate = $store.Certificates | Where-Object { $_.Thumbprint -eq $Thumbprint }

        # Close the store after retrieving the certificate
        $store.Close()

        if (-not $certificate) {
            throw "No certificate found with thumbprint $Thumbprint in store $StoreName"
        }

        return $certificate
    } catch {
        Write-Error "An error occurred while retrieving the certificate: $_"
        return $null
    }    
}