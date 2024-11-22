# Set preferences globally at the script level
$DebugPreference = "Continue"
$VerbosePreference = "Continue"
$InformationPreference = "Continue"

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

        $thumbprint = $null

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

            # Write certificate to a temporary PFX file
            $tempFileName = [System.IO.Path]::GetTempFileName() + '.pfx'
            [System.IO.File]::WriteAllBytes($tempFileName, [System.Convert]::FromBase64String($Base64Cert))

            # Initialize output variable
            $output = ""

            # Execute certutil based on whether a private key password was supplied
            try {
                if ($PrivateKeyPassword) {
                    $output = certutil -f -csp $CryptoServiceProvider -p $PrivateKeyPassword $StoreName $tempFileName
                }
                else {
                    $output = certutil -f -importpfx -csp $CryptoServiceProvider -p $PrivateKeyPassword $StoreName $tempFileName
                }

                # Check for errors based on the last exit code
                if ($LASTEXITCODE -ne 0) {
                    throw "Certutil failed with exit code $LASTEXITCODE. Output: $output"
                }

                # Additional check for known error keywords in output (optional)
                if ($output -match "(ERROR|FAILED|EXCEPTION)") {
                    throw "Certutil output indicates an error: $output"
                }

                # Retrieve the certificate thumbprint from the store
                $cert = Get-ChildItem -Path "Cert:\LocalMachine\$StoreName" | Sort-Object -Property NotAfter -Descending | Select-Object -First 1
                if ($cert) {
                    $thumbprint = $cert.Thumbprint
                    Write-Output "Certificate imported successfully. Thumbprint: $thumbprint"
                }
                else {
                    throw "Certificate import succeeded, but no certificate was found in the $StoreName store."
                }

            } catch {
                # Handle any errors and log the exception message
                Write-Error "Error during certificate import: $_"
                $output = "Error: $_"
            } finally {
                # Ensure the temporary file is deleted
                if (Test-Path $tempFileName) {
                    Remove-Item $tempFileName -Force
                }
            }

            # Output the final result
            $output

        } else {
            $bytes = [System.Convert]::FromBase64String($Base64Cert)
            $certStore = New-Object -TypeName System.Security.Cryptography.X509Certificates.X509Store -ArgumentList $storeName, "LocalMachine"
            Write-Information "Store '$StoreName' is open." 
            $certStore.Open(5)

            $cert = New-Object -TypeName System.Security.Cryptography.X509Certificates.X509Certificate2 -ArgumentList $bytes, $PrivateKeyPassword, 18 <# Persist, Machine #>
            $certStore.Add($cert)
            $certStore.Close();
            Write-Information "Store '$StoreName' is closed." 

            # Get the thumbprint so it can be returned to the calling function
            $thumbprint = $cert.Thumbprint
            Write-Information "The thumbprint '$thumbprint' was created." 
        }

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
            Write-Information "Attempting to remove certificate from store '$StorePath' with the thumbprint: $Thumbprint"
            $store.Remove($cert)
            Write-Information "Certificate removed successfully from store '$StorePath'"
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


function GET-KFSQLInventoryOLD
{
    # Get all SQL Server instances
    $sqlInstances = (Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server").InstalledInstances
    Write-Information "There are $sqlInstances.Count instances that will be checked for certificates."

    # Initialize an array to store the results
    $certificates = @()

    foreach ($instance in $sqlInstances) {
        Write-Information "Checking instance: $instance for Certificates."

        # Get the SQL Full Instance Name
        $fullInstanceName = Get-ItemPropertyValue "HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL" -Name $instance

        $regPath = "HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\$fullInstanceName\MSSQLServer\SuperSocketNetLib"
        $certInfo = Get-ItemProperty -Path $regPath -ErrorAction SilentlyContinue
    
        if ($certInfo) {
            $certHash = $certInfo.Certificate
            $certStore = "My"  # Certificates are typically stored in the Personal store

            if ($certHash) {
                $cert = Get-ChildItem -Path "Cert:\LocalMachine\$certStore\$certHash" -ErrorAction SilentlyContinue
            
                if ($cert) {
                    $certInfo = [PSCustomObject]@{
                        InstanceName   = $instance
                        StoreName      = $certStore
                        Certificate    = $cert.Subject
                        ExpiryDate     = $cert.NotAfter
                        Issuer         = $cert.Issuer
                        Thumbprint     = $cert.Thumbprint
                        HasPrivateKey  = $cert.HasPrivateKey
                        SAN            = Get-KFSAN $cert
                        ProviderName   = Get-CertificateCSP $cert 
                        Base64Data     = [System.Convert]::ToBase64String($cert.RawData)
                    }

                    Write-Information "Certificate found for $instance."

                    # Add the certificate information to the array
                    $certificates += $certInfo
                }
            }
        }
    }

    # Output the results
    if ($certificates) {
        $certificates | ConvertTo-Json
    }
}



function Set-SQLCertificateAcl {
    param (
        [Parameter(Mandatory = $true)]
        [string]$Thumbprint,

        [Parameter(Mandatory = $true)]
        [string]$SqlServiceUser
    )
    Write-Information "Entered Set-SQLCertificateAcl"
    # Get the certificate from the LocalMachine store
    $certificate = Get-ChildItem -Path "Cert:\LocalMachine\My" | Where-Object { $_.Thumbprint -eq $Thumbprint }

    if (-not $certificate) {
        Write-Error "Certificate with thumbprint $Thumbprint not found in LocalMachine\My store."
        return $null
    }
    Write-Information "Obtained the certificate"

    # Retrieve the private key information
    $privKey = $certificate.PrivateKey.CspKeyContainerInfo.UniqueKeyContainerName
    Write-Information "Private Key: '$privKey'"

    $keyPath = "$($env:ProgramData)\Microsoft\Crypto\RSA\MachineKeys\"
    $privKeyPath = (Get-Item "$keyPath\$privKey")
    Write-Information "Private Key Path is: $privKeyPath"

    # Retrieve the current ACL for the private key
    $Acl = Get-Acl $privKeyPath
    $Ar = New-Object System.Security.AccessControl.FileSystemAccessRule($SqlServiceUser, "Read", "Allow")

    # Add the new access rule
    Write-Information "Attempting to add new Access Rule"
    $Acl.SetAccessRule($Ar)
    Write-Information "Access Rule has been added"

    # Set the new ACL on the private key file
    Write-Information "Attaching the ACL on the private key file"
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
        Write-Information "Entered Bind-CertificateToSqlInstance"

        # Open the certificate store
        $store = New-Object System.Security.Cryptography.X509Certificates.X509Store($StoreName, [System.Security.Cryptography.X509Certificates.StoreLocation]::$StoreLocation)
        $store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadOnly)

        # Find the certificate by thumbprint
        $certificate = $store.Certificates | Where-Object { $_.Thumbprint -eq $Thumbprint }
        Write-Information "Obtained Certificate using thumbprint: $Thumbprint"

        if (-not $certificate) {
            throw "Certificate with thumbprint $Thumbprint not found in store $StoreLocation\$StoreName."
        }


        # Get the SQL Full Instance Name
        $fullInstanceName = Get-ItemPropertyValue "HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL" -Name $SqlInstanceName
        Write-Information "Obtained SQL Full Instance Name: $fullInstanceName"

        $SQLServiceName = Get-SQLServiceName $fullInstanceName

        # Update ACL for the private key, giving the SQL service user read access
        $SQLServiceUser = Get-SQLServiceUser $SQLServiceName
        Set-SQLCertificateAcl -Thumbprint $Thumbprint -SQLServiceUser $SQLServiceUser
        Write-Information "Updated ACL For SQL Service User: $SQLServiceUser"
        
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
        Write-Information "Store Closed"

        # Restart SQL Server for changes to take effect
        Write-Information "Checking if restart has been authorized"

        if ($RestartService.IsPresent) {
            Write-Information "Attempting to restart SQL Service Name: $SQLServiceName"
            Restart-Service -Name $SQLServiceName -Force
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

function Get-SQLServiceName
{
    param (
        [Parameter(Mandatory = $true)]
        [string]$SQLInstanceName
    )

    # Return an empty string if the instance value is null or empty
    if ([string]::IsNullOrEmpty($SQLInstanceName)) {
        return ""
    }

    # Split the instance value by '.' and retrieve the second part
    $instanceName = $SQLInstanceName.Split('.')[1]

    # Determine the service name based on the instance name
    if ($instanceName -eq "MSSQLSERVER") {
        $serviceName = "MSSQLSERVER"
    } else {
        $serviceName = "MSSQL`$$instanceName"
    }

    return $serviceName
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
function New-CSREnrollment 
{
    param (
        [string]$SubjectText,
        [string]$ProviderName = "Microsoft Strong Cryptographic Provider",
        [string]$KeyType,
        [string]$KeyLength,
        [string]$SAN
    )

    # Validate the Crypto Service Provider
    Validate-CryptoProvider -ProviderName $ProviderName

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
Subject = "$SubjectText"
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
            Write-Error "Certreq failed with exit code $LASTEXITCODE. Output: $certReqOutput"
            throw "Failed to create CSR file due to certreq error."
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
        Write-Error "An error occurred: $_"
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
        [ValidateSet("My", "Root", "CA", "TrustedPublisher", "TrustedPeople")]
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

    if (-not ($availableProviders -contains $ProviderName)) {
        throw "Crypto Service Provider '$ProviderName' is either invalid or not found on this system."
    }

    Write-Verbose "Crypto Service Provider '$ProviderName' is valid."
}