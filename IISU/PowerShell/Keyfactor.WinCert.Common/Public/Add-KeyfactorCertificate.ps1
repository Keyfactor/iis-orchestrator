function Add-KeyfactorCertificate {
	param (
		[Parameter(Mandatory = $true)]
		[string]$Base64Cert,

		[Parameter(Mandatory = $false)]
		[string]$PrivateKeyPassword,

		[Parameter(Mandatory = $true)]
		[string]$StoreName,

		[Parameter(Mandatory = $false)]
		[string]$CryptoServiceProvider,

		[Parameter(Mandatory = $false)]
		[switch]$Exportable
	)

	$thumbprint = $null
	$tempPfx    = $null

	try {
		Write-Information "Entering PowerShell Script Add-KeyfactorCertificate"
		Write-Information "[VERBOSE] Is64BitProcess: $([Environment]::Is64BitProcess)"
		Write-Information "[VERBOSE] Add-KeyfactorCertificate - Received: StoreName: '$StoreName', CryptoServiceProvider: '$CryptoServiceProvider', Exportable: '$Exportable'"

		# ---------------------------------------------------------------------
		# Step: LoadPfx
		#
		# Decode the Base64 payload and write the PFX to a temporary file.
		# The PFX file is intentionally retained until the complete import
		# operation has finished because both Import-PfxCertificate and
		# certutil require a file path.
		#
		# X509Certificate2 is used ONLY to read the certificate metadata
		# (thumbprint). It is NOT used to import the private key into the
		# certificate store.
		# ---------------------------------------------------------------------
		try {
			$bytes = [System.Convert]::FromBase64String($Base64Cert)

			$securePassword = $null
			if ($PrivateKeyPassword) {
				$securePassword = ConvertTo-SecureString `
					-String $PrivateKeyPassword `
					-AsPlainText `
					-Force
			}

			$tempPfx = [System.IO.Path]::Combine(
				[System.IO.Path]::GetTempPath(),
				[System.IO.Path]::GetRandomFileName() + ".pfx"
			)

			[System.IO.File]::WriteAllBytes($tempPfx, $bytes)

			Write-Information "[VERBOSE] Temporary PFX file created."

			# Load only enough information to determine the certificate
			# thumbprint. Do not use MachineKeySet, PersistKeySet, or
			# Exportable here because this is not the actual import.
			$cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2(
				$tempPfx,
				$securePassword,
				[System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet
			)

			$thumbprint = $cert.Thumbprint

			$cert.Dispose()
			$cert = $null
		}
		catch {
			$msg = "Failed to parse PFX payload (invalid Base64, corrupt PFX, or wrong password): $($_.Exception.Message)"

			Write-Error $msg

			return New-KeyfactorResult `
				-Status Error `
				-Code 501 `
				-Step LoadPfx `
				-ErrorMessage $msg
		}

		if (-not $thumbprint) {
			$msg = "PFX parsed but no thumbprint was produced. The PFX may be invalid or the password is incorrect."

			Write-Error $msg

			return New-KeyfactorResult `
				-Status Error `
				-Code 501 `
				-Step LoadPfx `
				-ErrorMessage $msg
		}

		Write-Information "[VERBOSE] PFX thumbprint: '$thumbprint'"

		# ---------------------------------------------------------------------
		# Step: SelectImportMethod
		#
		# ProviderName/CSP and Exportable are independent requirements.
		#
		#   No CSP specified
		#       -> Import-PfxCertificate
		#
		#   CSP specified
		#       -> certutil -importPFX -csp
		#
		# Import-PfxCertificate is preferred for the normal Windows
		# certificate-store import because it explicitly supports -Exportable.
		# ---------------------------------------------------------------------

		if ([string]::IsNullOrWhiteSpace($CryptoServiceProvider)) {

			# -----------------------------------------------------------------
			# Step: ImportPfxCertificate
			# -----------------------------------------------------------------
			try {
				Write-Information "Adding certificate using Import-PfxCertificate."

				$certificateStoreLocation = "Cert:\LocalMachine\$StoreName"

				$importParams = @{
					FilePath          = $tempPfx
					CertStoreLocation = $certificateStoreLocation
					ErrorAction       = 'Stop'
				}

				if ($PrivateKeyPassword) {
					Write-Information "[VERBOSE] PFX contains a password-protected private key."

					$importParams.Password = $securePassword
				}
				else {
					Write-Information "[VERBOSE] PFX does not have a password."
				}

				if ($Exportable) {
					Write-Information "[VERBOSE] Private key will be marked exportable."
					$importParams.Exportable = $true
				}
				else {
					Write-Information "[VERBOSE] Private key will not be marked exportable."
				}

				Write-Information "[VERBOSE] Importing PFX into '$certificateStoreLocation'."

				Import-PfxCertificate @importParams | Out-Null
			}
			catch {
				$msg = "Failed to import PFX into '$certificateStoreLocation' using Import-PfxCertificate: $($_.Exception.Message)"

				Write-Error $msg

				return New-KeyfactorResult `
					-Status Error `
					-Code 530 `
					-Step ImportPfxCertificate `
					-ErrorMessage $msg `
					-Details @{
						Thumbprint    = $thumbprint
						StoreName     = $StoreName
						Exportable    = [bool]$Exportable
						ProviderName = $null
					}
			}
		}
		else {

			# -----------------------------------------------------------------
			# Step: ValidateCSP
			#
			# A specific provider was requested. Verify that it exists before
			# attempting the certutil import.
			# -----------------------------------------------------------------
			if (-not (Test-CryptoServiceProvider -CSPName $CryptoServiceProvider)) {

				$available = @()

				try {
					$available = @(Get-CryptoProviders)
				}
				catch {
					Write-Warning "Unable to enumerate installed Crypto Service Providers: $($_.Exception.Message)"
				}

				$availableText = if ($available.Count -gt 0) {
					$available -join ', '
				}
				else {
					'(none enumerated)'
				}

				$msg = "The requested Crypto Service Provider '$CryptoServiceProvider' was not found on the target system. Available CSPs: $availableText"

				Write-Warning $msg

				return New-KeyfactorResult `
					-Status Error `
					-Code 510 `
					-Step ValidateCSP `
					-ErrorMessage $msg `
					-Details @{
						RequestedCSP  = $CryptoServiceProvider
						AvailableCSPs = $available
						Thumbprint    = $thumbprint
					}
			}

			# -----------------------------------------------------------------
			# Step: CertUtilImport
			#
			# certutil is used only when the caller explicitly requested a
			# specific CSP because Import-PfxCertificate does not provide
			# provider selection.
			# -----------------------------------------------------------------
			Write-Information "Adding certificate with the CSP '$CryptoServiceProvider'."

			$arguments = @(
				'-f'
			)

			if ($PrivateKeyPassword) {
				Write-Information "[VERBOSE] PFX contains a password-protected private key."

				$arguments += @(
					'-p',
					$PrivateKeyPassword
				)
			}
			else {
				Write-Information "[VERBOSE] PFX does not have a password."
			}

			$arguments += @(
				'-csp',
				$CryptoServiceProvider,
				'-importpfx',
				$StoreName,
				$tempPfx
			)

			if (-not $Exportable) {
				Write-Information "[VERBOSE] Private key will not be marked exportable."
				$arguments += 'NoExport'
			}
			else {
				Write-Information "[VERBOSE] Private key exportability was requested."
			}

			# -------------------------------------------------------------
			# Create a sanitized command line for logging.
			# NEVER log the PFX password.
			# -------------------------------------------------------------
			$safeArguments = @()

			for ($i = 0; $i -lt $arguments.Count; $i++) {

				if ($PrivateKeyPassword -and
					$arguments[$i] -eq $PrivateKeyPassword) {

					$safeArguments += '********'
				}
				else {
					$safeArguments += $arguments[$i]
				}
			}

			$safeArgLine = ($safeArguments | ForEach-Object {
				if ($_ -match '\s') {
					'"{0}"' -f $_
				}
				else {
					$_
				}
			}) -join ' '

			Write-Information "[VERBOSE] Running certutil with arguments: $safeArgLine"

			# -------------------------------------------------------------
			# Execute certutil.exe
			# -------------------------------------------------------------
			$processInfo = New-Object System.Diagnostics.ProcessStartInfo

			$processInfo.FileName               = "certutil.exe"
			$processInfo.Arguments              = $safeArgLine
			$processInfo.RedirectStandardOutput = $true
			$processInfo.RedirectStandardError  = $true
			$processInfo.UseShellExecute        = $false
			$processInfo.CreateNoWindow         = $true

			# IMPORTANT:
			# The actual arguments, including the password, must be supplied
			# to certutil. The sanitized command line above is only for logging.
			$actualArgLine = ($arguments | ForEach-Object {
				if ($_ -match '\s') {
					'"{0}"' -f $_
				}
				else {
					$_
				}
			}) -join ' '

			$processInfo.Arguments = $actualArgLine.Trim()

			$process = New-Object System.Diagnostics.Process
			$process.StartInfo = $processInfo

			try {
				[void]$process.Start()
			}
			catch {
				$msg = "Failed to launch certutil.exe: $($_.Exception.Message)"

				Write-Error $msg

				return New-KeyfactorResult `
					-Status Error `
					-Code 521 `
					-Step CertUtilImport `
					-ErrorMessage $msg `
					-Details @{
						Thumbprint = $thumbprint
					}
			}

			$stdOut = $process.StandardOutput.ReadToEnd()
			$stdErr = $process.StandardError.ReadToEnd()

			$process.WaitForExit()

			if ($process.ExitCode -ne 0) {

				$msg = "certutil exited with code $($process.ExitCode) while importing PFX with CSP '$CryptoServiceProvider'. StdErr: $stdErr StdOut: $stdOut"

				Write-Error $msg

				return New-KeyfactorResult `
					-Status Error `
					-Code 520 `
					-Step CertUtilImport `
					-ErrorMessage $msg `
					-Details @{
						ExitCode   = $process.ExitCode
						StdOut     = $stdOut
						StdErr     = $stdErr
						Thumbprint = $thumbprint
					}
			}
		}

		# ---------------------------------------------------------------------
		# Step: ImportComplete
		# ---------------------------------------------------------------------
		Write-Information "The thumbprint '$thumbprint' was added to store '$StoreName'."

		return New-KeyfactorResult `
			-Status Success `
			-Code 0 `
			-Step ImportCertificate `
			-Message "Certificate '$thumbprint' added to store '$StoreName'." `
			-Details @{
				Thumbprint    = $thumbprint
				StoreName     = $StoreName
				Exportable    = [bool]$Exportable
				ProviderName  = if ([string]::IsNullOrWhiteSpace($CryptoServiceProvider)) { $null } else { $CryptoServiceProvider }
			}
	}
	catch {
		$msg = "Unexpected error in Add-KeyfactorCertificate: $($_.Exception.Message)"

		Write-Error $msg

		return New-KeyfactorResult `
			-Status Error `
			-Code 300 `
			-Step CatchAll `
			-ErrorMessage $msg `
			-Details @{
				Thumbprint = $thumbprint
			}
	}
	finally {
		# The PFX contains the encrypted private key and should not remain
		# on the target after the import operation has completed.
		if ($tempPfx -and (Test-Path $tempPfx)) {
			Remove-Item $tempPfx -Force -ErrorAction SilentlyContinue
			Write-Information "[VERBOSE] Temporary PFX file removed."
		}
	}
}