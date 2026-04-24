function Add-KeyfactorCertificate {
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
        Write-Information "Entering PowerShell Script Add-KeyfactorCertificateToStore"
        Write-Verbose "Add-KeyfactorCertificateToStore - Received: StoreName: '$StoreName', CryptoServiceProvider: '$CryptoServiceProvider', Base64Cert: '$Base64Cert'"

        # Get the thumbprint of the passed in certificate
        # Convert password to secure string if provided, otherwise use $null
        $bytes = [System.Convert]::FromBase64String($Base64Cert)
        $securePassword = if ($PrivateKeyPassword) { ConvertTo-SecureString -String $PrivateKeyPassword -AsPlainText -Force } else { $null }

        # Set the storage flags and get the certificate's thumbprint
        $keyStorageFlags = [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::PersistKeySet -bor `
                   [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::MachineKeySet

        $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($bytes, $securePassword, $keyStorageFlags)
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

            # Open store with read/write, and don't create the store if it doesn't exist
            $openFlags = [System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite -bor `
                    [System.Security.Cryptography.X509Certificates.OpenFlags]::OpenExistingOnly
            $certStore.Open($openFlags)
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