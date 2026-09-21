# WinLDAP module result codes (a separate range from Keyfactor.WinCert.Common's 0-530 range -
# see that module's New-KeyfactorResult.ps1 header for the shared range).
# Code  Status   Step             Description
# 0     Success  ImportNtdsStore  Operation completed successfully
# 700   Error    LoadPfx          PFX payload could not be decoded or password was incorrect
# 701   Error    Eligibility      Certificate failed the LDAPS eligibility check (Test-LdapsCertificateEligibility)
# 702   Error    StagePersonal    Staging into the Personal (My) store failed - see Details.StageResult for the underlying Add-KeyfactorCertificate result
# 704   Error    RereadPersonal   Could not re-read the just-staged certificate back from Cert:\LocalMachine\My
# 703   Error    WriteNtdsStore   Writing the certificate into the NTDS service store failed
# 710   Error    InvalidStoreName StoreName was not in '<ServiceName>\<StoreName>' form
# 799   Error    CatchAll         Unexpected/unhandled exception

function Add-KeyfactorLdapsCertificate {
    <#
    .SYNOPSIS
    Deploys an LDAPS (AD DS SSL) server certificate to the local Domain Controller.

    .DESCRIPTION
    Implements the two-stage write sequence documented in docsource/winldap.md:
      1. Fail-fast eligibility check (Test-LdapsCertificateEligibility) - rejects clearly ineligible
         certificates before touching any store.
      2. Stage the certificate into Cert:\LocalMachine\My via the existing, unmodified
         Add-KeyfactorCertificate (Keyfactor.WinCert.Common) - matches the customer's own current
         manual runbook and keeps the certificate visible to AD DS's native selection logic as a
         safety net.
      3. Explicitly write the same certificate into the NTDS service store
         (Set-NtdsServiceStoreCertificate) - does NOT wait for or rely on AD DS's own ~10-minute
         automatic detection, so this operation's success/failure is deterministic and job-synchronous.
      4. Optionally restart the NTDS service if -RestartService is set (default off - this briefly
         takes AD DS offline on this one DC via "Restartable AD DS"). A restart failure is reported
         as a warning, not a failure, since the certificate was already successfully deployed to both
         stores at that point.

    Supports both local-agent and remote WinRM/JEA/SSH execution (see docsource/winldap.md), but the
    ACLs on the NTDS registry hive have not been validated for a JEA identity specifically, and
    whether the LDAPS listener picks up the new certificate without a restart has not been
    lab-validated either. Treat this function's current behavior as pre-production until that
    validation is complete.
    #>
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
        [bool]$RestartService = $false
    )

    $thumbprint = $null

    try {
        Write-Information "Entering PowerShell Script Add-KeyfactorLdapsCertificate"

        # --- Step: InvalidStoreName --------------------------------------
        $parts = $StoreName -split '\\', 2
        if ($parts.Count -ne 2) {
            $msg = "StoreName '$StoreName' is not in the expected '<ServiceName>\<StoreName>' form (e.g. 'NTDS\My')."
            Write-Error $msg
            return New-KeyfactorResult -Status Error -Code 710 -Step InvalidStoreName -ErrorMessage $msg
        }
        $serviceName = $parts[0]
        $leafStoreName = $parts[1]

        # --- Step: LoadPfx -------------------------------------------------
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
            return New-KeyfactorResult -Status Error -Code 700 -Step LoadPfx -ErrorMessage $msg
        }

        # --- Step: Eligibility ----------------------------------------------
        $eligibility = Test-LdapsCertificateEligibility -Certificate $cert
        if (-not $eligibility.Eligible) {
            $msg = "Certificate '$thumbprint' failed the LDAPS eligibility check: $($eligibility.Reason)"
            Write-Error $msg
            return New-KeyfactorResult -Status Error -Code 701 -Step Eligibility `
                -ErrorMessage $msg `
                -Details @{ Thumbprint = $thumbprint }
        }

        # --- Step: StagePersonal ---------------------------------------------
        # Reuses the existing, unmodified Add-KeyfactorCertificate (Keyfactor.WinCert.Common).
        Write-Information "Staging certificate '$thumbprint' into Cert:\LocalMachine\My"
        $stageParams = @{
            Base64Cert = $Base64Cert
            StoreName  = 'My'
        }
        if ($PrivateKeyPassword)    { $stageParams['PrivateKeyPassword']    = $PrivateKeyPassword }
        if ($CryptoServiceProvider) { $stageParams['CryptoServiceProvider'] = $CryptoServiceProvider }

        $stageResult = Add-KeyfactorCertificate @stageParams

        if (-not $stageResult.Status -or $stageResult.Status -ne 'Success') {
            $msg = "Failed to stage certificate '$thumbprint' into the Personal store before writing it to the NTDS service store: $($stageResult.ErrorMessage)"
            Write-Error $msg
            return New-KeyfactorResult -Status Error -Code 702 -Step StagePersonal `
                -ErrorMessage $msg `
                -Details @{ Thumbprint = $thumbprint; StageResult = $stageResult }
        }

        # --- Step: RereadPersonal ---------------------------------------------
        # An X509Certificate2 object constructed directly from PFX bytes (like $cert above) does NOT
        # reliably carry its CERT_KEY_PROV_INFO_PROP_ID property when exported, even when loaded with
        # PersistKeySet/MachineKeySet - verified empirically: only a certificate object obtained by
        # reading it back FROM an actual certificate store (e.g. via the Cert: provider) carries that
        # property, which is what lets the NTDS-store copy resolve HasPrivateKey. So the certificate
        # used for the NTDS write below must be re-read from Cert:\LocalMachine\My after staging, not
        # the original in-memory $cert.
        try {
            $stagedCert = Get-Item "Cert:\LocalMachine\My\$thumbprint" -ErrorAction Stop
        }
        catch {
            $msg = "Certificate '$thumbprint' was staged into Cert:\LocalMachine\My but could not be re-read back from it: $($_.Exception.Message)"
            Write-Error $msg
            return New-KeyfactorResult -Status Error -Code 704 -Step RereadPersonal `
                -ErrorMessage $msg `
                -Details @{ Thumbprint = $thumbprint }
        }

        # --- Step: WriteNtdsStore ---------------------------------------------
        Write-Information "Writing certificate '$thumbprint' into the '$serviceName\$leafStoreName' service store"
        $writeResult = Set-NtdsServiceStoreCertificate -ServiceName $serviceName -StoreName $leafStoreName -Certificate $stagedCert

        if (-not $writeResult.Success) {
            Write-Error $writeResult.ErrorMessage
            return New-KeyfactorResult -Status Error -Code 703 -Step WriteNtdsStore `
                -ErrorMessage $writeResult.ErrorMessage `
                -Details @{ Thumbprint = $thumbprint }
        }

        # --- Step: RestartService (optional, non-fatal) -----------------------
        $restartWarning = $null
        if ($RestartService) {
            try {
                Write-Information "Restarting the NTDS service to apply the new LDAPS certificate..."
                Restart-Service -Name "NTDS" -Force -ErrorAction Stop
                Write-Information "NTDS service restarted successfully."
            }
            catch {
                $restartWarning = "Certificate was added successfully, but restarting the NTDS service failed: $($_.Exception.Message). The LDAPS listener may not use the new certificate until NTDS is restarted or the DC is rebooted."
                Write-Warning $restartWarning
            }
        }
        else {
            Write-Information "Service restart skipped (RestartService not set). AD DS's own certificate detection, or a manual/scheduled NTDS restart, will determine when the LDAPS listener picks up this certificate."
        }

        Write-Information "The thumbprint '$thumbprint' was added to the '$serviceName\$leafStoreName' service store."

        return New-KeyfactorResult -Status Success -Code 0 -Step ImportNtdsStore `
            -Message $(if ($restartWarning) { $restartWarning } else { "Certificate '$thumbprint' added to the '$serviceName\$leafStoreName' service store." }) `
            -Details @{ Thumbprint = $thumbprint }
    }
    catch {
        $msg = "Unexpected error in Add-KeyfactorLdapsCertificate: $($_.Exception.Message)"
        Write-Error $msg
        return New-KeyfactorResult -Status Error -Code 799 -Step CatchAll `
            -ErrorMessage $msg `
            -Details @{ Thumbprint = $thumbprint }
    }
}
