# Version 1.0.0

# Summary
# Contains PowerShell functions to execute administration jobs for Windows ADFS.  This script is loaded in addition to the main WinCert PowerShell scripts.
# There are additional supporting PowerShell functions to support job specific actions.

<#
.SYNOPSIS
    ADFS Inventory and Management Functions
.DESCRIPTION
    Functions for collecting ADFS farm inventory, certificate information,
    and managing ADFS certificates across multiple nodes.
#>

function Get-AdfsFarmProperties {
    <#
    .SYNOPSIS
        Get ADFS farm properties
    #>
    try {
        $props = Get-ADFSProperties
        $farmInfo = Get-AdfsFarmInformation
        
        return [PSCustomObject]@{
            HostName = $props.HostName
            Identifier = $props.Identifier
            ServiceAccountName = $props.ServiceAccountName
            FarmBehaviorLevel = $farmInfo.CurrentFarmBehavior
        }
    }
    catch {
        Write-Error "Failed to get ADFS farm properties: $_"
        throw
    }
}

function Get-AdfsFarmNodeList {
    <#
    .SYNOPSIS
        Get list of all ADFS farm nodes
    #>
    try {
        $farmInfo = Get-AdfsFarmInformation
        return $farmInfo.FarmNodes
    }
    catch {
        Write-Error "Failed to get ADFS farm nodes: $_"
        throw
    }
}

function Get-AdfsNodeDetails {
    <#
    .SYNOPSIS
        Get detailed information for a specific ADFS node
    .PARAMETER NodeName
        Name of the ADFS node to query
    #>
    param(
        [Parameter(Mandatory=$true)]
        [string]$NodeName
    )
    
    try {
        # Get service status
        $service = Get-Service -Name adfssrv -ErrorAction Stop
        
        # Try to get sync properties (only works on secondary nodes)
        try {
            $syncProps = Get-AdfsSyncProperties -ErrorAction Stop
            $role = $syncProps.Role
            $lastSync = $syncProps.LastSyncTime
            $syncStatus = $syncProps.SyncStatus
        }
        catch {
            # This is the primary node
            $role = "PrimaryComputer"
            $lastSync = $null
            $syncStatus = "N/A"
        }
        
        return [PSCustomObject]@{
            NodeName = $env:COMPUTERNAME
            ServiceStatus = $service.Status.ToString()
            Role = $role
            LastSyncTime = $lastSync
            SyncStatus = $syncStatus
        }
    }
    catch {
        Write-Error "Failed to get node details for ${NodeName}: $_"
        throw
    }
}

function Get-AdfsCertificateInventory {
    <#
    .SYNOPSIS
        Get all ADFS certificates with detailed information
    #>
    try {
        $certs = Get-AdfsCertificate
        $now = Get-Date
        
        $results = @()
        foreach ($cert in $certs) {
            $daysUntilExpiry = ($cert.Certificate.NotAfter - $now).Days
            
            $results += [PSCustomObject]@{
                CertificateType = $cert.CertificateType
                IsPrimary = $cert.IsPrimary
                Thumbprint = $cert.Certificate.Thumbprint
                Subject = $cert.Certificate.Subject
                Issuer = $cert.Certificate.Issuer
                NotBefore = $cert.Certificate.NotBefore
                NotAfter = $cert.Certificate.NotAfter
                DaysUntilExpiry = $daysUntilExpiry
                IsExpired = $daysUntilExpiry -lt 0
                IsExpiringSoon = ($daysUntilExpiry -lt 60 -and $daysUntilExpiry -ge 0)
            }
        }
        
        return $results
    }
    catch {
        Write-Error "Failed to get ADFS certificates: $_"
        throw
    }
}

function Update-AdfsServiceCommunicationsCertificate {
    <#
    .SYNOPSIS
        Update Service-Communications certificate on current node
    .PARAMETER PfxFilePath
        Path to the PFX certificate file
    .PARAMETER PfxPassword
        Password for the PFX file (as SecureString)
    #>
    param(
        [Parameter(Mandatory=$true)]
        [string]$PfxFilePath,
        
        [Parameter(Mandatory=$true)]
        [SecureString]$PfxPassword
    )
    
    try {
        Write-Host "Importing certificate on $env:COMPUTERNAME..."
        
        # Import certificate
        $cert = Import-PfxCertificate -FilePath $PfxFilePath `
            -Password $PfxPassword `
            -CertStoreLocation 'Cert:\LocalMachine\My' `
            -ErrorAction Stop
        
        Write-Host "✓ Certificate imported: $($cert.Thumbprint)"
        
        # Restart ADFS service
        Write-Host "Restarting ADFS service..."
        Restart-Service -Name adfssrv -Force -ErrorAction Stop
        
        Write-Host "✓ ADFS service restarted"
        
        return [PSCustomObject]@{
            Success = $true
            Thumbprint = $cert.Thumbprint
            NodeName = $env:COMPUTERNAME
        }
    }
    catch {
        Write-Error "Failed to update certificate on ${env:COMPUTERNAME}: $_"
        return [PSCustomObject]@{
            Success = $false
            ErrorMessage = $_.Exception.Message
            NodeName = $env:COMPUTERNAME
        }
    }
}

function Set-AdfsFarmCertificateSettings {
    <#
    .SYNOPSIS
        Update ADFS farm certificate settings (run on primary node only)
    .PARAMETER CertificateThumbprint
        Thumbprint of the new certificate
    #>
    param(
        [Parameter(Mandatory=$true)]
        [string]$CertificateThumbprint
    )
    
    try {
        Write-Host "Updating ADFS farm certificate settings..."
        
        # Update SSL certificate
        Set-AdfsSslCertificate -Thumbprint $CertificateThumbprint -ErrorAction Stop
        Write-Host "✓ SSL certificate updated"
        
        # Update Service-Communications certificate
        Set-AdfsCertificate -CertificateType Service-Communications `
            -Thumbprint $CertificateThumbprint -ErrorAction Stop
        Write-Host "✓ Service-Communications certificate updated"
        
        # Check for alternate TLS client binding (certificate authentication)
        $cert = Get-ChildItem -Path "Cert:\LocalMachine\My\$CertificateThumbprint"
        if ($cert.DnsNameList -match 'certauth.') {
            Set-AdfsAlternateTlsClientBinding -Thumbprint $CertificateThumbprint -ErrorAction Stop
            Write-Host "✓ Alternate TLS client binding updated"
        }
        
        return [PSCustomObject]@{
            Success = $true
            Message = "ADFS farm certificate settings updated successfully"
        }
    }
    catch {
        Write-Error "Failed to update ADFS farm settings: $_"
        return [PSCustomObject]@{
            Success = $false
            ErrorMessage = $_.Exception.Message
        }
    }
}

function Test-AdfsNodeConnectivity {
    <#
    .SYNOPSIS
        Test connectivity to an ADFS node
    .PARAMETER NodeName
        Name of the node to test
    #>
    param(
        [Parameter(Mandatory=$true)]
        [string]$NodeName
    )
    
    try {
        $result = Test-NetConnection -ComputerName $NodeName -Port 5985 -InformationLevel Quiet
        
        return [PSCustomObject]@{
            NodeName = $NodeName
            IsReachable = $result
            Port = 5985
        }
    }
    catch {
        return [PSCustomObject]@{
            NodeName = $NodeName
            IsReachable = $false
            ErrorMessage = $_.Exception.Message
        }
    }
}

function Get-AdfsCertificateStatus {
    <#
    .SYNOPSIS
        Get status of a specific certificate type
    .PARAMETER CertificateType
        Type of certificate (Token-Signing, Token-Decrypting, Service-Communications)
    #>
    param(
        [Parameter(Mandatory=$true)]
        [ValidateSet('Token-Signing', 'Token-Decrypting', 'Service-Communications')]
        [string]$CertificateType
    )
    
    try {
        $certs = Get-AdfsCertificate -CertificateType $CertificateType
        $now = Get-Date
        
        $results = @()
        foreach ($cert in $certs) {
            $daysUntilExpiry = ($cert.Certificate.NotAfter - $now).Days
            
            $results += [PSCustomObject]@{
                CertificateType = $cert.CertificateType
                IsPrimary = $cert.IsPrimary
                Thumbprint = $cert.Certificate.Thumbprint
                Subject = $cert.Certificate.Subject
                NotAfter = $cert.Certificate.NotAfter
                DaysUntilExpiry = $daysUntilExpiry
                Status = if ($daysUntilExpiry -lt 0) { "EXPIRED" }
                        elseif ($daysUntilExpiry -lt 30) { "CRITICAL" }
                        elseif ($daysUntilExpiry -lt 60) { "WARNING" }
                        else { "OK" }
            }
        }
        
        return $results
    }
    catch {
        Write-Error "Failed to get certificate status: $_"
        throw
    }
}

function Add-AdfsSecondaryCertificate {
    <#
    .SYNOPSIS
        Add a secondary certificate for rollover preparation
    .PARAMETER CertificateType
        Type of certificate (Token-Signing or Token-Decrypting)
    .PARAMETER Thumbprint
        Thumbprint of the certificate to add
    #>
    param(
        [Parameter(Mandatory=$true)]
        [ValidateSet('Token-Signing', 'Token-Decrypting')]
        [string]$CertificateType,
        
        [Parameter(Mandatory=$true)]
        [string]$Thumbprint
    )
    
    try {
        Write-Host "Adding secondary $CertificateType certificate..."
        
        # Check if certificate exists in store
        $cert = Get-ChildItem -Path "Cert:\LocalMachine\My\$Thumbprint" -ErrorAction Stop
        
        # Add as secondary certificate
        Add-AdfsCertificate -CertificateType $CertificateType `
            -Thumbprint $Thumbprint -ErrorAction Stop
        
        Write-Host "✓ Secondary certificate added successfully"
        Write-Host ""
        Write-Host "IMPORTANT: Next Steps for Certificate Rollover" -ForegroundColor Yellow
        Write-Host "1. Wait 2-4 weeks for relying parties to update from metadata" -ForegroundColor Yellow
        Write-Host "2. Notify Office 365 / external partners if needed" -ForegroundColor Yellow
        Write-Host "3. Promote to primary: Set-AdfsCertificate -CertificateType $CertificateType -Thumbprint $Thumbprint -IsPrimary" -ForegroundColor Yellow
        Write-Host "4. After promotion, remove old certificate" -ForegroundColor Yellow
        
        return [PSCustomObject]@{
            Success = $true
            CertificateType = $CertificateType
            Thumbprint = $Thumbprint
            Message = "Secondary certificate added. Wait 2-4 weeks before promoting to primary."
        }
    }
    catch {
        Write-Error "Failed to add secondary certificate: $_"
        return [PSCustomObject]@{
            Success = $false
            ErrorMessage = $_.Exception.Message
        }
    }
}

function Set-AdfsPrimaryCertificate {
    <#
    .SYNOPSIS
        Promote a secondary certificate to primary
    .PARAMETER CertificateType
        Type of certificate (Token-Signing or Token-Decrypting)
    .PARAMETER Thumbprint
        Thumbprint of the certificate to promote
    #>
    param(
        [Parameter(Mandatory=$true)]
        [ValidateSet('Token-Signing', 'Token-Decrypting')]
        [string]$CertificateType,
        
        [Parameter(Mandatory=$true)]
        [string]$Thumbprint
    )
    
    try {
        Write-Host "Promoting certificate to primary..."
        
        Set-AdfsCertificate -CertificateType $CertificateType `
            -Thumbprint $Thumbprint -IsPrimary -ErrorAction Stop
        
        Write-Host "✓ Certificate promoted to primary"
        Write-Host ""
        Write-Host "Next Steps:" -ForegroundColor Yellow
        Write-Host "1. Monitor for any issues with relying parties" -ForegroundColor Yellow
        Write-Host "2. After 1-2 weeks, remove old certificate if no issues" -ForegroundColor Yellow
        
        return [PSCustomObject]@{
            Success = $true
            CertificateType = $CertificateType
            Thumbprint = $Thumbprint
            Message = "Certificate promoted to primary successfully"
        }
    }
    catch {
        Write-Error "Failed to promote certificate: $_"
        return [PSCustomObject]@{
            Success = $false
            ErrorMessage = $_.Exception.Message
        }
    }
}

function Copy-FileToNode {
    <#
    .SYNOPSIS
        Copy a file to a remote node
    .PARAMETER SourcePath
        Path to source file
    .PARAMETER DestinationPath
        Destination path on remote machine
    .PARAMETER NodeName
        Target node name
    #>
    param(
        [Parameter(Mandatory=$true)]
        [string]$SourcePath,
        
        [Parameter(Mandatory=$true)]
        [string]$DestinationPath,
        
        [Parameter(Mandatory=$true)]
        [string]$NodeName
    )
    
    try {
        Write-Host "Copying file to $NodeName..."
        
        # Read file content as bytes
        $fileBytes = [System.IO.File]::ReadAllBytes($SourcePath)
        
        # Write to destination
        [System.IO.File]::WriteAllBytes($DestinationPath, $fileBytes)
        
        Write-Host "✓ File copied successfully to $DestinationPath"
        
        return [PSCustomObject]@{
            Success = $true
            SourcePath = $SourcePath
            DestinationPath = $DestinationPath
            NodeName = $env:COMPUTERNAME
        }
    }
    catch {
        Write-Error "Failed to copy file: $_"
        return [PSCustomObject]@{
            Success = $false
            ErrorMessage = $_.Exception.Message
            NodeName = $env:COMPUTERNAME
        }
    }
}

function Install-AdfsCertificateOnNode {
    <#
    .SYNOPSIS
        Install PFX certificate on the current node
    .PARAMETER PfxFilePath
        Path to the PFX certificate file
    .PARAMETER PfxPasswordText
        Password for the PFX file (as plain text - will be converted to SecureString)
    #>
    param(
        [Parameter(Mandatory=$true)]
        [string]$PfxFilePath,
        
        [Parameter(Mandatory=$true)]
        [string]$PfxPasswordText
    )
    
    try {
        Write-Host "Installing certificate on $env:COMPUTERNAME..."
        
        # Convert password to SecureString
        $securePassword = ConvertTo-SecureString -String $PfxPasswordText -AsPlainText -Force
        
        # Import certificate
        $cert = Import-PfxCertificate -FilePath $PfxFilePath `
            -Password $securePassword `
            -CertStoreLocation 'Cert:\LocalMachine\My' `
            -Exportable `
            -ErrorAction Stop
        
        Write-Host "✓ Certificate imported successfully"
        Write-Host "  Thumbprint: $($cert.Thumbprint)"
        Write-Host "  Subject: $($cert.Subject)"
        Write-Host "  Expires: $($cert.NotAfter)"
        
        return [PSCustomObject]@{
            Success = $true
            Thumbprint = $cert.Thumbprint
            Subject = $cert.Subject
            NotAfter = $cert.NotAfter
            NodeName = $env:COMPUTERNAME
        }
    }
    catch {
        Write-Error "Failed to install certificate on ${env:COMPUTERNAME}: $_"
        return [PSCustomObject]@{
            Success = $false
            ErrorMessage = $_.Exception.Message
            NodeName = $env:COMPUTERNAME
        }
    }
}

function Grant-AdfsCertificatePermissions {
    <#
    .SYNOPSIS
        Grant ADFS service account access to certificate private key
    .PARAMETER CertificateThumbprint
        Thumbprint of the certificate
    .PARAMETER ServiceAccountName
        Name of the ADFS service account (optional - will try to detect)
    #>
    param(
        [Parameter(Mandatory=$true)]
        [string]$CertificateThumbprint,
        
        [Parameter(Mandatory=$false)]
        [string]$ServiceAccountName
    )
    
    try {
        Write-Information "Checking certificate permissions on $env:COMPUTERNAME..."
        
        # Get the certificate
        $cert = Get-ChildItem -Path "Cert:\LocalMachine\My\$CertificateThumbprint" -ErrorAction Stop
        
        if (-not $cert.HasPrivateKey) {
            Write-Warning "Certificate does not have a private key"
            return [PSCustomObject]@{
                Success = $false
                ErrorMessage = "Certificate does not have a private key"
                NodeName = $env:COMPUTERNAME
            }
        }
        
        # If no service account provided, try to get it
        if ([string]::IsNullOrWhiteSpace($ServiceAccountName)) {
            Write-Verbose "  Service account not provided, attempting to detect..."
            
            try {
                # Try to get from ADFS properties
                $adfsProps = Get-ADFSProperties -ErrorAction Stop
                $ServiceAccountName = $adfsProps.ServiceAccountName
                Write-Verbose "  Detected service account: $ServiceAccountName"
            }
            catch {
                Write-Warning "Could not detect ADFS service account"
            }
            
            # If still null, try to get from service
            if ([string]::IsNullOrWhiteSpace($ServiceAccountName)) {
                try {
                    $service = Get-WmiObject Win32_Service -Filter "Name='adfssrv'" -ErrorAction Stop
                    $ServiceAccountName = $service.StartName
                    Write-Verbose "  Detected from service: $ServiceAccountName"
                }
                catch {
                    Write-Warning "Could not detect service account from Windows service"
                }
            }
        }
        
        # Check if we have a valid service account
        if ([string]::IsNullOrWhiteSpace($ServiceAccountName)) {
            Write-Warning "No service account specified and could not auto-detect"
            Write-Warning "ADFS service may need manual permission grant if it runs as a domain user"
            
            return [PSCustomObject]@{
                Success = $true
                Skipped = $true
                Message = "Service account not available - permissions not granted. May require manual intervention."
                NodeName = $env:COMPUTERNAME
            }
        }
        
        # Check if service account is a built-in account (which doesn't need explicit permissions)
        $builtInAccounts = @('NT AUTHORITY\SYSTEM', 'NT AUTHORITY\NETWORK SERVICE', 'LocalSystem', 'SYSTEM')
        if ($builtInAccounts -contains $ServiceAccountName) {
            Write-Verbose "  Service runs as built-in account ($ServiceAccountName) - explicit permissions not needed"
            
            return [PSCustomObject]@{
                Success = $true
                Skipped = $true
                Message = "Service runs as built-in account - explicit permissions not needed"
                ServiceAccount = $ServiceAccountName
                NodeName = $env:COMPUTERNAME
            }
        }
        
        Write-Verbose "  Granting permissions to: $ServiceAccountName"
        
        # Get the private key
        $rsaCert = [System.Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPrivateKey($cert)
        $fileName = $rsaCert.Key.UniqueName
        
        # Private keys are stored here
        $privateKeyPath = "$env:ProgramData\Microsoft\Crypto\RSA\MachineKeys\$fileName"
        
        if (Test-Path $privateKeyPath) {
            # Get current ACL
            $acl = Get-Acl -Path $privateKeyPath
            
            # Check if account already has permissions
            $existingRule = $acl.Access | Where-Object { $_.IdentityReference -eq $ServiceAccountName }
            if ($existingRule) {
                Write-Verbose "  ✓ Service account already has permissions"
                return [PSCustomObject]@{
                    Success = $true
                    AlreadyGranted = $true
                    ServiceAccount = $ServiceAccountName
                    NodeName = $env:COMPUTERNAME
                }
            }
            
            # Create access rule for service account
            $accessRule = New-Object System.Security.AccessControl.FileSystemAccessRule(
                $ServiceAccountName,
                "Read",
                "Allow"
            )
            
            # Add the access rule
            $acl.AddAccessRule($accessRule)
            
            # Set the ACL
            Set-Acl -Path $privateKeyPath -AclObject $acl
            
            Write-Verbose "  ✓ Permissions granted to $ServiceAccountName"
            
            return [PSCustomObject]@{
                Success = $true
                ServiceAccount = $ServiceAccountName
                PrivateKeyPath = $privateKeyPath
                NodeName = $env:COMPUTERNAME
            }
        }
        else {
            throw "Private key file not found at $privateKeyPath"
        }
    }
    catch {
        Write-Warning "Failed to grant permissions: $_"
        return [PSCustomObject]@{
            Success = $false
            ErrorMessage = $_.Exception.Message
            NodeName = $env:COMPUTERNAME
        }
    }
}

function Grant-AdfsCertificatePermissionsOLD {
    <#
    .SYNOPSIS
        Grant ADFS service account access to certificate private key
    .PARAMETER CertificateThumbprint
        Thumbprint of the certificate
    .PARAMETER ServiceAccountName
        Name of the ADFS service account
    #>
    param(
        [Parameter(Mandatory=$true)]
        [string]$CertificateThumbprint,
        
        [Parameter(Mandatory=$true)]
        [string]$ServiceAccountName
    )
    
    try {
        Write-Verbose "Granting permissions to service account on $env:COMPUTERNAME..."
        
        # Get the certificate
        $cert = Get-ChildItem -Path "Cert:\LocalMachine\My\$CertificateThumbprint" -ErrorAction Stop
        
        if (-not $cert.HasPrivateKey) {
            throw "Certificate does not have a private key"
        }
        
        # Get the private key
        $rsaCert = [System.Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPrivateKey($cert)
        $fileName = $rsaCert.Key.UniqueName
        
        # Private keys are stored here
        $privateKeyPath = "$env:ProgramData\Microsoft\Crypto\RSA\MachineKeys\$fileName"
        
        if (Test-Path $privateKeyPath) {
            # Get current ACL
            $acl = Get-Acl -Path $privateKeyPath
            
            # Create access rule for service account
            $accessRule = New-Object System.Security.AccessControl.FileSystemAccessRule(
                $ServiceAccountName,
                "Read",
                "Allow"
            )
            
            # Add the access rule
            $acl.AddAccessRule($accessRule)
            
            # Set the ACL
            Set-Acl -Path $privateKeyPath -AclObject $acl
            
            Write-Host "✓ Permissions granted to $ServiceAccountName"
            
            return [PSCustomObject]@{
                Success = $true
                ServiceAccount = $ServiceAccountName
                PrivateKeyPath = $privateKeyPath
                NodeName = $env:COMPUTERNAME
            }
        }
        else {
            throw "Private key file not found at $privateKeyPath"
        }
    }
    catch {
        Write-Error "Failed to grant permissions: $_"
        return [PSCustomObject]@{
            Success = $false
            ErrorMessage = $_.Exception.Message
            NodeName = $env:COMPUTERNAME
        }
    }
}

function Update-AdfsFarmCertificateSettings {
    <#
    .SYNOPSIS
        Update ADFS farm certificate settings (PRIMARY NODE ONLY)
    .PARAMETER CertificateThumbprint
        Thumbprint of the new certificate
    #>
    param(
        [Parameter(Mandatory=$true)]
        [string]$CertificateThumbprint
    )
    
    try {
        Write-Information "Updating ADFS farm certificate settings on primary node..."
        
        # Update SSL certificate
        Write-Information "  Updating SSL certificate..."

        # Get current computer name for -Member parameter
        $currentMember = $env:COMPUTERNAME

        Set-AdfsSslCertificate -Thumbprint $CertificateThumbprint -Member $currentMember -ErrorAction Stop
        
        # Update Service-Communications certificate
        Write-Information "  Updating Service-Communications certificate..."
        Set-AdfsCertificate -CertificateType Service-Communications `
            -Thumbprint $CertificateThumbprint -ErrorAction Stop
        
        # Check for alternate TLS client binding (certificate authentication)
        $cert = Get-ChildItem -Path "Cert:\LocalMachine\My\$CertificateThumbprint"
        if ($cert.DnsNameList -match 'certauth.') {
            Write-Information "  Updating alternate TLS client binding..."
            Set-AdfsAlternateTlsClientBinding -Thumbprint $CertificateThumbprint -ErrorAction Stop
        }
        
        Write-Information "✓ ADFS farm certificate settings updated successfully"
        
        return [PSCustomObject]@{
            Success = $true
            Message = "ADFS farm certificate settings updated"
            Thumbprint = $CertificateThumbprint
        }
    }
    catch {
        Write-Error "Failed to update ADFS farm settings: $_"
        return [PSCustomObject]@{
            Success = $false
            ErrorMessage = $_.Exception.Message
        }
    }
}

function Restart-AdfsServiceOnNode {
    <#
    .SYNOPSIS
        Restart ADFS service on current node
    #>
    try {
        Write-Host "Restarting ADFS service on $env:COMPUTERNAME..."
        
        Restart-Service -Name adfssrv -Force -ErrorAction Stop
        
        # Wait a moment and verify it's running
        Start-Sleep -Seconds 2
        $service = Get-Service -Name adfssrv
        
        if ($service.Status -eq 'Running') {
            Write-Host "✓ ADFS service restarted successfully"
            
            return [PSCustomObject]@{
                Success = $true
                ServiceStatus = $service.Status.ToString()
                NodeName = $env:COMPUTERNAME
            }
        }
        else {
            throw "ADFS service is not running after restart. Status: $($service.Status)"
        }
    }
    catch {
        Write-Error "Failed to restart ADFS service: $_"
        return [PSCustomObject]@{
            Success = $false
            ErrorMessage = $_.Exception.Message
            NodeName = $env:COMPUTERNAME
        }
    }
}

function Remove-OldAdfsCertificate {
    <#
    .SYNOPSIS
        Remove old certificate from node
    .PARAMETER CertificateSubject
        Subject of the certificate to match
    .PARAMETER NewCertificateNotAfter
        NotAfter date of the new certificate (to avoid removing it)
    #>
    param(
        [Parameter(Mandatory=$true)]
        [string]$CertificateSubject,
        
        [Parameter(Mandatory=$true)]
        [DateTime]$NewCertificateNotAfter
    )
    
    try {
        Write-Host "Removing old certificates on $env:COMPUTERNAME..."
        
        # Find old certificates with same subject but earlier expiration
        $oldCerts = Get-ChildItem "Cert:\LocalMachine\My" | 
            Where-Object { 
                $_.Subject -match [regex]::Escape($CertificateSubject) -and 
                $_.NotAfter -lt $NewCertificateNotAfter 
            }
        
        $removedCount = 0
        foreach ($cert in $oldCerts) {
            Write-Host "  Removing certificate: $($cert.Thumbprint) (expires: $($cert.NotAfter))"
            Remove-Item -Path "Cert:\LocalMachine\My\$($cert.Thumbprint)" -Force
            $removedCount++
        }
        
        if ($removedCount -gt 0) {
            Write-Host "✓ Removed $removedCount old certificate(s)"
        }
        else {
            Write-Host "  No old certificates found to remove"
        }
        
        return [PSCustomObject]@{
            Success = $true
            RemovedCount = $removedCount
            NodeName = $env:COMPUTERNAME
        }
    }
    catch {
        Write-Error "Failed to remove old certificates: $_"
        return [PSCustomObject]@{
            Success = $false
            ErrorMessage = $_.Exception.Message
            NodeName = $env:COMPUTERNAME
        }
    }
}

function Remove-TempFileOnNode {
    <#
    .SYNOPSIS
        Remove temporary file from node
    .PARAMETER FilePath
        Path to file to remove
    #>
    param(
        [Parameter(Mandatory=$true)]
        [string]$FilePath
    )
    
    try {
        if (Test-Path $FilePath) {
            Remove-Item -Path $FilePath -Force -ErrorAction Stop
            Write-Host "✓ Temporary file removed: $FilePath"
            
            return [PSCustomObject]@{
                Success = $true
                FilePath = $FilePath
                NodeName = $env:COMPUTERNAME
            }
        }
        else {
            Write-Host "  Temporary file not found: $FilePath"
            return [PSCustomObject]@{
                Success = $true
                Message = "File not found"
                NodeName = $env:COMPUTERNAME
            }
        }
    }
    catch {
        Write-Error "Failed to remove temporary file: $_"
        return [PSCustomObject]@{
            Success = $false
            ErrorMessage = $_.Exception.Message
            NodeName = $env:COMPUTERNAME
        }
    }
}

function Test-AdfsCertificateInstalled {
    <#
    .SYNOPSIS
        Verify certificate is installed on node
    .PARAMETER CertificateThumbprint
        Thumbprint of certificate to check
    #>
    param(
        [Parameter(Mandatory=$true)]
        [string]$CertificateThumbprint
    )
    
    try {
        $cert = Get-ChildItem -Path "Cert:\LocalMachine\My\$CertificateThumbprint" -ErrorAction Stop
        
        return [PSCustomObject]@{
            Success = $true
            IsInstalled = $true
            HasPrivateKey = $cert.HasPrivateKey
            Subject = $cert.Subject
            Thumbprint = $cert.Thumbprint
            NotAfter = $cert.NotAfter
            NodeName = $env:COMPUTERNAME
        }
    }
    catch {
        return [PSCustomObject]@{
            Success = $true
            IsInstalled = $false
            NodeName = $env:COMPUTERNAME
        }
    }
}

Write-Host "✓ ADFS Inventory and Management functions loaded" -ForegroundColor Green