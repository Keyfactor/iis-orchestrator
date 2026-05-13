function Set-KeyfactorSQLCertificateBinding {
    <#
    .SYNOPSIS
    Binds a certificate to a SQL Server instance and sets appropriate permissions.
    
    .DESCRIPTION
    This function binds a certificate to a SQL Server instance by updating the registry,
    setting ACL permissions on the private key, and optionally restarting the SQL service.
    Supports both local Windows-to-Windows and SSH-based remote connections.
    Handles both CNG (modern) and CAPI (legacy) certificate key storage.
    
    .PARAMETER InstanceName
    The SQL Server instance name (e.g., "MSSQLSERVER" for default instance)
    
    .PARAMETER NewThumbprint
    The thumbprint of the certificate to bind
    
    .PARAMETER RestartService
    Switch to restart the SQL Server service after binding
    
    .EXAMPLE
    Set-KFSQLCertificateBinding -InstanceName "MSSQLSERVER" -NewThumbprint "ABC123..." -RestartService
    #>
    
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [string]$InstanceName,
        
        [Parameter(Mandatory = $true)]
        [string]$NewThumbprint,
        
        [switch]$RestartService
    )
    
    Write-Information "Starting certificate binding process for instance: $InstanceName"
    Write-Information "Target certificate thumbprint: $NewThumbprint"
    
    try {
        # ============================================================
        # STEP 1: Get SQL Instance Registry Path
        # ============================================================
        Write-Information "Retrieving SQL Server instance information..."
        
        try {
            $fullInstance = Get-ItemPropertyValue -Path "HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL" -Name $InstanceName -ErrorAction Stop
            $RegistryPath = Get-SqlCertRegistryLocation -InstanceName $fullInstance
            
            Write-Information "[VERBOSE] Full instance name: $fullInstance"
            Write-Information "[VERBOSE] Registry path: $RegistryPath"
            Write-Information "SQL Server instance registry path located: $RegistryPath"
        }
        catch {
            Write-Error "Failed to locate SQL Server instance '$InstanceName' in registry: $_"
            throw $_
        }
        
        # ============================================================
        # STEP 2: Update Registry with New Certificate Thumbprint
        # ============================================================
        Write-Information "Updating registry with new certificate thumbprint..."
        
        try {
            # Backup current value
            $currentThumbprint = Get-ItemPropertyValue -Path $RegistryPath -Name "Certificate" -ErrorAction SilentlyContinue
            
            if ($currentThumbprint) {
                Write-Information "[VERBOSE] Current certificate thumbprint: $currentThumbprint"
            } else {
                Write-Information "[VERBOSE] No existing certificate thumbprint found"
            }
            
            # Set new thumbprint
            Set-ItemProperty -Path $RegistryPath -Name "Certificate" -Value $NewThumbprint -ErrorAction Stop
            
            # Verify the change
            $verifyThumbprint = Get-ItemPropertyValue -Path $RegistryPath -Name "Certificate" -ErrorAction Stop
            
            if ($verifyThumbprint -eq $NewThumbprint) {
                Write-Information "Registry updated successfully"
            } else {
                throw "Registry update verification failed. Expected: $NewThumbprint, Got: $verifyThumbprint"
            }
        }
        catch {
            Write-Error "Failed to update registry at '$RegistryPath': $_"
            throw $_
        }
        
        # ============================================================
        # STEP 3: Get SQL Server Service Information
        # ============================================================
        Write-Information "Retrieving SQL Server service information..."
        
        try {
            $serviceName = Get-KeyfactorSQLServiceName -InstanceName $InstanceName
            $serviceInfo = Get-CimInstance -ClassName Win32_Service -Filter "Name='$serviceName'" -ErrorAction Stop
            $SqlServiceUser = $serviceInfo.StartName
            
            if (-not $SqlServiceUser) {
                throw "Unable to retrieve service account for SQL Server instance: $InstanceName"
            }
            
            # Normalize service account name for ACL operations
            if ($SqlServiceUser -eq "LocalSystem") {
                $SqlServiceUser = "NT AUTHORITY\SYSTEM"
                Write-Information "[VERBOSE] Normalized LocalSystem to: $SqlServiceUser"
            } 
            elseif ($SqlServiceUser -match "^NT Service\\") {
                # NT Service accounts are already in correct format
                Write-Information "[VERBOSE] Using NT Service account: $SqlServiceUser"
            }
            elseif ($SqlServiceUser.StartsWith(".\")) {
                # Local account - convert to machine\user format
                $SqlServiceUser = "$env:COMPUTERNAME$($SqlServiceUser.Substring(1))"
                Write-Information "[VERBOSE] Normalized local account to: $SqlServiceUser"
            }
            
            Write-Information "[VERBOSE] Service name: $serviceName"
            Write-Information "SQL Server service account: $SqlServiceUser"
        }
        catch {
            Write-Error "Failed to retrieve SQL Server service information: $_"
            throw $_
        }
        
        # ============================================================
        # STEP 4: Locate Certificate and Private Key
        # ============================================================
        Write-Information "Locating certificate and private key..."

        try {
            # Get the certificate from the LocalMachine\My store
            $Cert = Get-ChildItem Cert:\LocalMachine\My | Where-Object { $_.Thumbprint -eq $NewThumbprint }
    
            if (-not $Cert) {
                throw "Certificate with thumbprint $NewThumbprint not found in LocalMachine\My store"
            }
    
            Write-Information "[VERBOSE] Certificate found: $($Cert.Subject)"
    
            if (-not $Cert.HasPrivateKey) {
                throw "Certificate does not have a private key"
            }
    
            # Detect private key location (CNG vs CAPI)
            $privKeyPath = $null
            $privKey = $null
            $keyStorageType = $null
    
            # Try CNG first (modern certificates)
            try {
                $rsaKey = [System.Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPrivateKey($Cert)
        
                if ($rsaKey -is [System.Security.Cryptography.RSACng]) {
                    $privKey = $rsaKey.Key.UniqueName
                    $keyStorageType = "CNG"
                    Write-Information "[VERBOSE] Certificate uses CNG key storage"
                    Write-Information "[VERBOSE] CNG key unique name: $privKey"
            
                    # CNG keys can be in multiple locations - check them all
                    $possiblePaths = @(
                        "$($env:ProgramData)\Microsoft\Crypto\Keys\$privKey",
                        "$($env:ProgramData)\Microsoft\Crypto\SystemKeys\$privKey",
                        "$($env:ProgramData)\Microsoft\Crypto\RSA\MachineKeys\$privKey"
                    )
            
                    Write-Information "[VERBOSE] Searching for CNG private key in known locations..."
                    foreach ($path in $possiblePaths) {
                        Write-Information "[VERBOSE] Checking: $path"
                        if (Test-Path $path) {
                            $privKeyPath = Get-Item $path -ErrorAction Stop
                            Write-Information "[VERBOSE] Found CNG private key at: $path"
                            break
                        }
                    }
            
                    # If not found in standard locations, search more broadly
                    if (-not $privKeyPath) {
                        Write-Information "[VERBOSE] Key not found in standard locations. Searching all Crypto directories..."
                
                        $searchPaths = @(
                            "$($env:ProgramData)\Microsoft\Crypto\Keys",
                            "$($env:ProgramData)\Microsoft\Crypto\SystemKeys",
                            "$($env:ProgramData)\Microsoft\Crypto\RSA\MachineKeys"
                        )
                
                        foreach ($searchPath in $searchPaths) {
                            if (Test-Path $searchPath) {
                                $found = Get-ChildItem -Path $searchPath -Filter "*$privKey*" -ErrorAction SilentlyContinue | Select-Object -First 1
                                if ($found) {
                                    $privKeyPath = $found
                                    Write-Information "[VERBOSE] Found CNG private key at: $($privKeyPath.FullName)"
                                    break
                                }
                            }
                        }
                    }
            
                    if ($privKeyPath) {
                        Write-Information "Certificate uses CNG (Cryptography Next Generation) key storage"
                    }
                }
            }
            catch {
                Write-Information "[VERBOSE] CNG key detection failed or not applicable: $_"
                Write-Information "[VERBOSE] Exception type: $($_.Exception.GetType().FullName)"
            }
    
            # Fallback to CAPI/CSP (legacy certificates)
            if (-not $privKey -or -not $privKeyPath) {
                Write-Information "[VERBOSE] Attempting CAPI/CSP key detection..."
        
                try {
                    if ($Cert.PrivateKey -and $Cert.PrivateKey.CspKeyContainerInfo) {
                        $privKey = $Cert.PrivateKey.CspKeyContainerInfo.UniqueKeyContainerName
                        $keyPath = "$($env:ProgramData)\Microsoft\Crypto\RSA\MachineKeys\"
                        $keyStorageType = "CAPI/CSP"
                
                        Write-Information "[VERBOSE] CAPI/CSP key unique name: $privKey"
                        Write-Information "[VERBOSE] Expected path: $keyPath$privKey"
                
                        $privKeyPath = Get-Item "$keyPath\$privKey" -ErrorAction Stop
                        Write-Information "Certificate uses CAPI/CSP (legacy) key storage"
                    }
                }
                catch {
                    Write-Information "[VERBOSE] CAPI/CSP key detection failed: $_"
                }
            }
    
            if (-not $privKey) {
                throw "Unable to locate private key for certificate. The certificate may not have an accessible private key."
            }
    
            if (-not $privKeyPath) {
                # Last resort: try to find the key file by searching
                Write-Warning "Private key file not found in expected locations. Attempting comprehensive search..."
        
                $allCryptoPaths = @(
                    "$($env:ProgramData)\Microsoft\Crypto\Keys",
                    "$($env:ProgramData)\Microsoft\Crypto\SystemKeys", 
                    "$($env:ProgramData)\Microsoft\Crypto\RSA\MachineKeys"
                )
        
                foreach ($searchPath in $allCryptoPaths) {
                    if (Test-Path $searchPath) {
                        Write-Information "[VERBOSE] Searching in: $searchPath"
                        $found = Get-ChildItem -Path $searchPath -File -ErrorAction SilentlyContinue | 
                            Where-Object { $_.Name -like "*$privKey*" -or $_.Name -eq $privKey } |
                            Select-Object -First 1
                    
                        if ($found) {
                            $privKeyPath = $found
                            Write-Information "Private key found at: $($privKeyPath.FullName)"
                            break
                        }
                    }
                }
        
                if (-not $privKeyPath) {
                    throw "Unable to locate private key file for certificate with thumbprint $NewThumbprint. Key name: $privKey"
                }
            }
    
            Write-Information "Private key located at: $($privKeyPath.FullName)"
            Write-Information "[VERBOSE] Key storage type: $keyStorageType"
            Write-Information "[VERBOSE] Key file size: $($privKeyPath.Length) bytes"
    
            # Verify we can read the key file
            try {
                $acl = Get-Acl -Path $privKeyPath.FullName -ErrorAction Stop
                Write-Information "[VERBOSE] Successfully accessed private key file ACL"
            }
            catch {
                Write-Warning "Could not read ACL from private key file: $_"
            }
        }
        catch {
            Write-Error "Failed to locate certificate or private key: $_"
            throw $_
        }        

        # ============================================================
        # STEP 5: Set ACL Permissions on Private Key
        # ============================================================
        Write-Information "Setting ACL permissions on private key for SQL service account..."
        
        try {
            $aclSet = $false
            $aclMethod = $null
            
            # Attempt 1: Try Set-Acl (works in most local scenarios and some SSH sessions)
            try {
                Write-Information "[VERBOSE] Attempting ACL update using Set-Acl method..."
                
                $Acl = Get-Acl -Path $privKeyPath -ErrorAction Stop
                $Ar = New-Object System.Security.AccessControl.FileSystemAccessRule(
                    $SqlServiceUser, 
                    "Read", 
                    "Allow"
                )
                $Acl.SetAccessRule($Ar)
                Set-Acl -Path $privKeyPath.FullName -AclObject $Acl -ErrorAction Stop
                
                # Verify the ACL was actually set
                $verifyAcl = Get-Acl -Path $privKeyPath
                $hasPermission = $verifyAcl.Access | Where-Object { 
                    ($_.IdentityReference.Value -eq $SqlServiceUser -or 
                     $_.IdentityReference.Value -like "*$SqlServiceUser*") -and 
                    $_.FileSystemRights -match "Read"
                }
                
                if ($hasPermission) {
                    Write-Information "ACL updated successfully using Set-Acl method"
                    $aclSet = $true
                    $aclMethod = "Set-Acl"
                } else {
                    Write-Warning "Set-Acl completed but verification failed. Permissions may not be set correctly."
                }
            }
            catch {
                Write-Warning "Set-Acl method failed: $_"
                Write-Information "[VERBOSE] Error details: $($_.Exception.Message)"
            }
            
            # Attempt 2: Use icacls (more reliable in SSH sessions)
            if (-not $aclSet) {
                Write-Information "[VERBOSE] Attempting ACL update using icacls method..."
                
                try {
                    # Execute icacls to grant Read permissions
                    $icaclsResult = & icacls.exe $privKeyPath.FullName /grant "${SqlServiceUser}:(R)" 2>&1
                    
                    if ($LASTEXITCODE -eq 0) {
                        Write-Information "[VERBOSE] icacls command executed successfully"
                        
                        # Verify with icacls
                        $verifyResult = & icacls.exe $privKeyPath.FullName 2>&1
                        
                        if ($verifyResult -match [regex]::Escape($SqlServiceUser)) {
                            Write-Information "ACL updated successfully using icacls method"
                            $aclSet = $true
                            $aclMethod = "icacls"
                        } else {
                            Write-Warning "icacls completed but verification failed"
                        }
                    } else {
                        Write-Warning "icacls failed with exit code $LASTEXITCODE"
                        Write-Information "[VERBOSE] icacls output: $icaclsResult"
                    }
                }
                catch {
                    Write-Warning "icacls method failed: $_"
                }
            }
            
            # Attempt 3: Use Scheduled Task (fallback for restricted SSH sessions)
            if (-not $aclSet) {
                Write-Warning "Standard ACL methods failed. Attempting scheduled task method (elevated privileges)..."
                
                try {
                    # Create a temporary script to set the ACL
                    $tempScriptPath = Join-Path $env:TEMP "SetCertACL_$((Get-Date).Ticks).ps1"
                    
                    $scriptContent = @"
try {
    `$privKeyPath = '$($privKeyPath.FullName)'
    `$SqlServiceUser = '$SqlServiceUser'
    
    # Try icacls first
    `$result = & icacls.exe `$privKeyPath /grant "`${SqlServiceUser}:(R)" 2>&1
    
    if (`$LASTEXITCODE -eq 0) {
        Set-Content -Path '$env:TEMP\acl_success.txt' -Value "Success via icacls"
    } else {
        # Fallback to Set-Acl
        `$Acl = Get-Acl -Path `$privKeyPath
        `$Ar = New-Object System.Security.AccessControl.FileSystemAccessRule(
            `$SqlServiceUser, 
            'Read', 
            'Allow'
        )
        `$Acl.SetAccessRule(`$Ar)
        Set-Acl -Path `$privKeyPath -AclObject `$Acl
        Set-Content -Path '$env:TEMP\acl_success.txt' -Value "Success via Set-Acl"
    }
} catch {
    Set-Content -Path '$env:TEMP\acl_error.txt' -Value `$_.Exception.Message
}
"@
                    
                    Set-Content -Path $tempScriptPath -Value $scriptContent
                    Write-Information "[VERBOSE] Created temporary script: $tempScriptPath"
                    
                    # Create and register the scheduled task
                    $taskName = "SetCertACL_$((Get-Date).Ticks)"
                    $action = New-ScheduledTaskAction -Execute "PowerShell.exe" -Argument "-ExecutionPolicy Bypass -NoProfile -File `"$tempScriptPath`""
                    $trigger = New-ScheduledTaskTrigger -Once -At (Get-Date).AddSeconds(2)
                    $principal = New-ScheduledTaskPrincipal -UserId "SYSTEM" -LogonType ServiceAccount -RunLevel Highest
                    $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable
                    
                    Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings -ErrorAction Stop | Out-Null
                    Write-Information "[VERBOSE] Scheduled task registered: $taskName"
                    
                    # Wait for task to complete
                    Write-Information "[VERBOSE] Waiting for scheduled task to complete..."
                    Start-Sleep -Seconds 5
                    
                    # Check results
                    if (Test-Path "$env:TEMP\acl_success.txt") {
                        $successMessage = Get-Content "$env:TEMP\acl_success.txt" -Raw
                        Write-Information "ACL updated successfully using scheduled task method ($successMessage)"
                        Remove-Item "$env:TEMP\acl_success.txt" -Force -ErrorAction SilentlyContinue
                        $aclSet = $true
                        $aclMethod = "Scheduled Task"
                    } 
                    elseif (Test-Path "$env:TEMP\acl_error.txt") {
                        $errorMessage = Get-Content "$env:TEMP\acl_error.txt" -Raw
                        Remove-Item "$env:TEMP\acl_error.txt" -Force -ErrorAction SilentlyContinue
                        throw "Scheduled task failed: $errorMessage"
                    } 
                    else {
                        throw "Scheduled task did not complete or produce expected output"
                    }
                    
                    # Cleanup
                    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue
                    Remove-Item $tempScriptPath -Force -ErrorAction SilentlyContinue
                }
                catch {
                    Write-Warning "Scheduled task method failed: $_"
                }
            }
            
            # Final check
            if (-not $aclSet) {
                throw "Failed to set ACL permissions using all available methods (Set-Acl, icacls, Scheduled Task)"
            }
            
            Write-Information "ACL permissions configured successfully using: $aclMethod"
            
        }
        catch {
            Write-Error "Failed to update ACL on the private key: $_"
            Write-Error "SQL Server may not be able to use this certificate without proper permissions."
            throw $_
        }
        
        # ============================================================
        # STEP 6: Restart SQL Server Service (Optional)
        # ============================================================
        if ($RestartService) {
            Write-Information "Restarting SQL Server service..."
            
            try {
                # Get current service status
                $service = Get-Service -Name $serviceName -ErrorAction Stop
                $originalStatus = $service.Status
                
                Write-Information "[VERBOSE] Current service status: $originalStatus"
                
                # Stop the service if running
                if ($originalStatus -eq 'Running') {
                    Write-Information "Stopping SQL Server service: $serviceName"
                    Stop-Service -Name $serviceName -Force -ErrorAction Stop
                    
                    # Wait for service to stop (with timeout)
                    $stopTimeout = 60
                    $elapsed = 0
                    
                    while ((Get-Service -Name $serviceName).Status -ne 'Stopped' -and $elapsed -lt $stopTimeout) {
                        Start-Sleep -Seconds 2
                        $elapsed += 2
                        Write-Information "[VERBOSE] Waiting for service to stop... ($elapsed seconds)"
                    }
                    
                    if ((Get-Service -Name $serviceName).Status -ne 'Stopped') {
                        throw "Service did not stop within $stopTimeout seconds"
                    }
                    
                    Write-Information "SQL Server service stopped successfully"
                }
                
                # Start the service
                Write-Information "Starting SQL Server service: $serviceName"
                Start-Service -Name $serviceName -ErrorAction Stop
                
                # Wait for service to start (with timeout)
                $startTimeout = 90
                $elapsed = 0
                
                while ((Get-Service -Name $serviceName).Status -ne 'Running' -and $elapsed -lt $startTimeout) {
                    Start-Sleep -Seconds 2
                    $elapsed += 2
                    Write-Information "[VERBOSE] Waiting for service to start... ($elapsed seconds)"
                }
                
                $finalStatus = (Get-Service -Name $serviceName).Status
                
                if ($finalStatus -eq 'Running') {
                    Write-Information "SQL Server service restarted successfully"
                } else {
                    throw "Service did not start within $startTimeout seconds. Current status: $finalStatus"
                }
            }
            catch {
                Write-Error "Failed to restart SQL Server service: $_"
                Write-Warning "Certificate binding completed but service restart failed."
                Write-Warning "Please restart SQL Server manually to apply the certificate binding."
                Write-Warning "You can restart using: Restart-Service -Name '$serviceName' -Force"
                
                # Don't throw here - the certificate binding succeeded
                # Just warn the user to restart manually
            }
        } else {
            Write-Information "Service restart skipped. You must restart SQL Server for the certificate binding to take effect."
            Write-Information "To restart: Restart-Service -Name '$serviceName' -Force"
        }
        
        # ============================================================
        # SUCCESS
        # ============================================================
        Write-Information "=========================================="
        Write-Information "Certificate binding completed successfully!"
        Write-Information "Instance: $InstanceName"
        Write-Information "Certificate: $NewThumbprint"
        Write-Information "Service Account: $SqlServiceUser"
        Write-Information "Key Storage: $keyStorageType"
        Write-Information "ACL Method: $aclMethod"
        
        if ($RestartService) {
            Write-Information "Service Status: Restarted"
        } else {
            Write-Information "Service Status: Restart Required"
        }
        Write-Information "=========================================="
        
        return $true
    }
    catch {
        Write-Error "Certificate binding failed for instance $InstanceName"
        Write-Error "Error: $_"
        Write-Information "[VERBOSE] Stack trace: $($_.ScriptStackTrace)"
        return $false
    }
}