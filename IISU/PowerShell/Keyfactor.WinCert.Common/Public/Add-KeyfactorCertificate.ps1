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

	$thumbprint = $null
	$tempPfx    = $null

	try {
		Write-Information "Entering PowerShell Script Add-KeyfactorCertificate"
		Write-Information "[VERBOSE] Add-KeyfactorCertificate - Received: StoreName: '$StoreName', CryptoServiceProvider: '$CryptoServiceProvider'"

		# --- Step: LoadPfx ---------------------------------------------------
		# Parse the PFX and extract the thumbprint. This validates the payload
		# and the private key password before we attempt any store operations.
		try {
			$bytes          = [System.Convert]::FromBase64String($Base64Cert)
			$securePassword = if ($PrivateKeyPassword) { ConvertTo-SecureString -String $PrivateKeyPassword -AsPlainText -Force } else { $null }

			$keyStorageFlags = [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::PersistKeySet -bor `
							   [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::MachineKeySet

			$cert       = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($bytes, $securePassword, $keyStorageFlags)
			$thumbprint = $cert.Thumbprint
		}
		catch {
			$msg = "Failed to parse PFX payload (invalid Base64, corrupt PFX, or wrong password): $($_.Exception.Message)"
			Write-Error $msg
			return New-KeyfactorResult -Status Error -Code 501 -Step LoadPfx -ErrorMessage $msg
		}

		if (-not $thumbprint) {
			$msg = "PFX parsed but no thumbprint was produced. The PFX may be invalid or the password is incorrect."
			Write-Error $msg
			return New-KeyfactorResult -Status Error -Code 501 -Step LoadPfx -ErrorMessage $msg
		}

		# --- Step: ValidateCSP -----------------------------------------------
		# If the caller requested a specific CSP, verify it exists on the
		# target system BEFORE attempting the import. When it is missing,
		# enumerate the installed CSPs so the operator sees what is available.
		if ($CryptoServiceProvider) {
			if (-not (Test-CryptoServiceProvider -CSPName $CryptoServiceProvider)) {
				$available = @()
				try { $available = @(Get-CryptoProviders) } catch { }

				$availableText = if ($available.Count -gt 0) { ($available -join ', ') } else { '(none enumerated)' }
				$msg = "The requested Crypto Service Provider '$CryptoServiceProvider' was not found on the target system. Available CSPs: $availableText"

				Write-Warning $msg
				return New-KeyfactorResult -Status Error -Code 510 -Step ValidateCSP `
					-ErrorMessage $msg `
					-Details @{
						RequestedCSP  = $CryptoServiceProvider
						AvailableCSPs = $available
						Thumbprint    = $thumbprint
					}
			}

			# --- Step: CertUtilImport ---------------------------------------
			# Import via certutil.exe so the requested CSP is honoured.
			Write-Information "Adding certificate with the CSP '$CryptoServiceProvider'"

			$tempPfx = [System.IO.Path]::GetTempFileName() + ".pfx"
			[System.IO.File]::WriteAllBytes($tempPfx, $bytes)

			$arguments = @('-f')
			if ($PrivateKeyPassword) {
				Write-Information "[VERBOSE] Has a private key"
				$arguments += @('-p', $PrivateKeyPassword)
			}
			Write-Information "[VERBOSE] Has a CryptoServiceProvider: $CryptoServiceProvider"
			$arguments += @('-csp', $CryptoServiceProvider, '-importpfx', $StoreName, $tempPfx)

			# Quote any argument containing whitespace
			$argLine = ($arguments | ForEach-Object {
				if ($_ -match '\s') { '"{0}"' -f $_ } else { $_ }
			}) -join ' '

			Write-Information "[VERBOSE] Running certutil with arguments: $argLine"

			$processInfo = New-Object System.Diagnostics.ProcessStartInfo
			$processInfo.FileName               = "certutil.exe"
			$processInfo.Arguments              = $argLine.Trim()
			$processInfo.RedirectStandardOutput = $true
			$processInfo.RedirectStandardError  = $true
			$processInfo.UseShellExecute        = $false
			$processInfo.CreateNoWindow         = $true

			$process = New-Object System.Diagnostics.Process
			$process.StartInfo = $processInfo

			try {
				[void]$process.Start()
			}
			catch {
				$msg = "Failed to launch certutil.exe: $($_.Exception.Message)"
				Write-Error $msg
				return New-KeyfactorResult -Status Error -Code 521 -Step CertUtilImport `
					-ErrorMessage $msg `
					-Details @{ Thumbprint = $thumbprint }
			}

			$stdOut = $process.StandardOutput.ReadToEnd()
			$stdErr = $process.StandardError.ReadToEnd()
			$process.WaitForExit()

			if ($process.ExitCode -ne 0) {
				$msg = "certutil exited with code $($process.ExitCode) while importing PFX with CSP '$CryptoServiceProvider'. StdErr: $stdErr StdOut: $stdOut"
				Write-Error $msg
				return New-KeyfactorResult -Status Error -Code 520 -Step CertUtilImport `
					-ErrorMessage $msg `
					-Details @{
						ExitCode   = $process.ExitCode
						StdOut     = $stdOut
						StdErr     = $stdErr
						Thumbprint = $thumbprint
					}
			}
		}
		else {
			# --- Step: ImportCertificate (managed API path) -----------------
			try {
				$certStore = New-Object System.Security.Cryptography.X509Certificates.X509Store -ArgumentList $StoreName, "LocalMachine"
				Write-Information "Store '$StoreName' is open."

				$openFlags = [System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite -bor `
							 [System.Security.Cryptography.X509Certificates.OpenFlags]::OpenExistingOnly

				$certStore.Open($openFlags)
				$certStore.Add($cert)
				$certStore.Close()
				Write-Information "Store '$StoreName' is closed."
			}
			catch {
				$msg = "Failed to open/write certificate store '$StoreName' on LocalMachine: $($_.Exception.Message)"
				Write-Error $msg
				return New-KeyfactorResult -Status Error -Code 530 -Step ImportCertificate `
					-ErrorMessage $msg `
					-Details @{ Thumbprint = $thumbprint }
			}
		}

		Write-Information "The thumbprint '$thumbprint' was added to store $StoreName."

		return New-KeyfactorResult -Status Success -Code 0 -Step ImportCertificate `
			-Message "Certificate '$thumbprint' added to store '$StoreName'." `
			-Details @{ Thumbprint = $thumbprint }
	}
	catch {
		$msg = "Unexpected error in Add-KeyfactorCertificate: $($_.Exception.Message)"
		Write-Error $msg
		return New-KeyfactorResult -Status Error -Code 300 -Step CatchAll `
			-ErrorMessage $msg `
			-Details @{ Thumbprint = $thumbprint }
	}
	finally {
		if ($tempPfx -and (Test-Path $tempPfx)) {
			Remove-Item $tempPfx -Force -ErrorAction SilentlyContinue
		}
	}
}
