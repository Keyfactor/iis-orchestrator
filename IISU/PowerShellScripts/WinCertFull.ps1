function Get-KFCertificate
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
    Import-Module WebAdministration

    # Get all websites
    $websites = Get-Website

    # Initialize an array to store the results
    $certificates = @()

    foreach ($site in $websites) {
        # Get the site name
        $siteName = $site.name
        
        # Get the bindings for the site
        $bindings = Get-WebBinding -Name $siteName
        
        foreach ($binding in $bindings) {
            # Check if the binding has an SSL certificate
            if ($binding.protocol -eq 'https') {
                # Get the certificate hash
                $certHash = $binding.certificateHash
                
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

function Get-KFSQLBoundCertificate
{
    param (
        [string]$SQLInstanceName = "MSSQLSERVER"  # Default instance name
    )

    # Initialize an array to store the results
    $certificates = @()

    try {
        $SQLInstancePath = Get-ItemPropertyValue "HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL" -Name $SQLInstanceName -ErrorAction Stop
        
        # Define the registry path to look for certificate binding information
        $sqlRegPath = "HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\$SQLInstancePath\MSSQLServer\SuperSocketNetLib"

        try {
            # Check if the registry path exists
            if (Test-Path $sqlRegPath) {
                # Get the certificate thumbprint from the registry
                $certThumbprint = (Get-ItemProperty -Path $sqlRegPath -Name "Certificate").Certificate
    
                if ($certThumbprint) {
                    # Clean up the thumbprint (remove any leading/trailing spaces)
                    $certThumbprint = $certThumbprint.Trim()
    
                    # Retrieve the certificate details from the certificate store
                    $cert = Get-ChildItem -Path "Cert:\LocalMachine\My\$certThumbprint"
    
                    if ($cert) {
                        # Create a custom object to store the result
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
                        $certificates += $certInfo
                    }
                }
                
                # Output the results
                if ($certificates) {
                    $certificates | ConvertTo-Json
                }
            }
            else {
                Write-Information "INFO: Certificate is not bound to SQL Server instance: $SQLInstanceName"
            }
        }
        catch {
            Write-Information "ERROR: An error occurred while retrieving certificate information: $_"
        }
    
    }
    catch {
        Write-Host "WARN: Unable to find the SQL Instance: $SQLInstanceName"
        Write-Information "WARN: Unable to find the SQL Instance: $SQLInstanceName"
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
                Write-Host "The CSP $CryptoServiceProvider was not found on the system."
                return
            }
        }

        # Convert Base64 string to byte array
        $certBytes = [Convert]::FromBase64String($Base64Cert)

        # Create a temporary file to store the certificate
        $tempStoreName = [System.IO.Path]::GetTempFileName()
        [System.IO.File]::WriteAllBytes($tempStoreName, $certBytes)

        $thumbprint = $null

        if ($CryptoServiceProvider) {
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
                $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($tempStoreName, $PrivateKeyPassword, [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::Exportable)
            } else {
                $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($tempStoreName)
            }

            # Open the certificate store
            $store = New-Object System.Security.Cryptography.X509Certificates.X509Store($StoreName, [System.Security.Cryptography.X509Certificates.StoreLocation]::LocalMachine)
            $store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)

            # Add the certificate to the store
            $store.Add($cert)

            # Get the thumbprint so it can be returned to the calling function
            $thumbprint = $cert.Thumbprint

            # Close the store
            $store.Close()
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
            Write-Host "Certificate removed successfully from $StorePath."
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

function New-KFSQLBinding
{
    param (
        [Parameter(Mandatory=$true)]
        [string]$SqlInstanceName,          # The name of the SQL Server instance (e.g., "MSSQLSERVER" for default)

        [Parameter(Mandatory=$true)]
        [string]$CertificateThumbprint,    # The thumbprint of the certificate to be bound

        [Parameter(Mandatory=$false)]
        [string]$CertStoreLocation = "Cert:\LocalMachine\My" # Certificate store location (defaults to LocalMachine\My)
    )

    # Load the SQL Server WMI provider
    $wmiNamespace = "root\Microsoft\SqlServer\ComputerManagement14" # For SQL Server 2017 and later
    if (-not (Get-WmiObject -Namespace $wmiNamespace -List | Out-Null)) {
        throw "Could not load WMI namespace for SQL Server. Ensure SQL Server WMI provider is installed."
    }

    try {
        # Get the SQL Server instance using WMI
        $sqlService = Get-WmiObject -Namespace $wmiNamespace -Class SqlService | Where-Object {
            $_.ServiceName -eq $SqlInstanceName -and $_.SQLServiceType -eq 1
        }

        if (-not $sqlService) {
            throw "SQL Server instance '$SqlInstanceName' not found."
        }

        # Ensure the certificate exists in the specified store
        $certificate = Get-ChildItem -Path "$CertStoreLocation" | Where-Object { $_.Thumbprint -eq $CertificateThumbprint }
        if (-not $certificate) {
            throw "Certificate with thumbprint '$CertificateThumbprint' not found in store '$CertStoreLocation'."
        }

        # Bind the certificate to the SQL Server instance using WMI
        $sqlService.SetEncryption($CertificateThumbprint)

        Write-Host "Certificate with thumbprint '$CertificateThumbprint' successfully bound to SQL Server instance '$SqlInstanceName'."
        
    } catch {
        Write-Error "An error occurred: $_"
    }
}

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