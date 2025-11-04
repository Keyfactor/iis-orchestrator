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

Write-Host "✓ ADFS Inventory and Management functions loaded" -ForegroundColor Green