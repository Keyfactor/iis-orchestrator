# See Add-KeyfactorLdapsCertificate.ps1 for the shared WinLDAP module result code range.
# Code  Status   Step             Description
# 0     Success  WriteNtdsStore   Operation completed successfully
# 701   Error    Eligibility      Certificate failed the LDAPS eligibility check (Test-LdapsCertificateEligibility)
# 703   Error    WriteNtdsStore   Writing the certificate into the NTDS service store failed
# 710   Error    InvalidStoreName StoreName was not in '<ServiceName>\<StoreName>' form
# 740   Error    RereadPersonal   Certificate could not be read back from Cert:\LocalMachine\My
# 799   Error    CatchAll         Unexpected/unhandled exception

function Register-KeyfactorLdapsCertificate {
    <#
    .SYNOPSIS
    Registers an already-imported certificate into the AD DS (NTDS) LDAPS service certificate store.

    .DESCRIPTION
    Used only by the ReEnrollment (On-Device Key Generation / ODKG) flow. By the time this is
    called, the private key was generated locally by certreq (see New-KeyfactorODKGEnrollment) and
    the Command-signed certificate has already been imported into Cert:\LocalMachine\My by the
    shared Import-KeyfactorSignedCertificate (Keyfactor.WinCert.Common) - see
    IISU/ClientPSCertStoreReEnrollment.cs, which always imports WinLDAP's re-enrolled certificate
    into "My" regardless of the store's actual StoreName ("NTDS\My" is not a real Cert: provider
    path Import-KeyfactorSignedCertificate could target directly).

    This function:
      1. Re-reads the certificate back from Cert:\LocalMachine\My by thumbprint - the same reason
         Add-KeyfactorLdapsCertificate re-reads after its own staging step, rather than trusting a
         directly-constructed certificate object, for reliable HasPrivateKey resolution.
      2. Runs the same Test-LdapsCertificateEligibility check Add uses (defense in depth - the CSR's
         Subject/SAN come from whatever Command/the certificate template configured, with no
         guarantee it matches this DC's FQDN or carries the Server-Auth EKU).
      3. Writes it into the NTDS service store via the existing, unmodified
         Set-NtdsServiceStoreCertificate.

    Does not touch the Personal-store copy itself, and does not restart any service - matches
    WinSql's own ReEnrollment precedent (WinSqlBinding.BindSQLCertificate is called with
    restartSQLService hardcoded to false during ReEnrollment), rather than introducing a new
    restart-after-reenrollment behavior other store types don't have either.
    #>
    param (
        [Parameter(Mandatory = $true)]
        [string]$Thumbprint,

        [Parameter(Mandatory = $true)]
        [string]$StoreName
    )

    try {
        Write-Information "Entering PowerShell Script Register-KeyfactorLdapsCertificate"

        $parts = $StoreName -split '\\', 2
        if ($parts.Count -ne 2) {
            $msg = "StoreName '$StoreName' is not in the expected '<ServiceName>\<StoreName>' form (e.g. 'NTDS\My')."
            Write-Error $msg
            return New-KeyfactorResult -Status Error -Code 710 -Step InvalidStoreName -ErrorMessage $msg
        }
        $serviceName = $parts[0]
        $leafStoreName = $parts[1]

        # --- Step: RereadPersonal ---------------------------------------------
        try {
            $cert = Get-Item "Cert:\LocalMachine\My\$Thumbprint" -ErrorAction Stop
        }
        catch {
            $msg = "Certificate '$Thumbprint' was not found in Cert:\LocalMachine\My. Register-KeyfactorLdapsCertificate must be called after the certificate has already been imported there."
            Write-Error $msg
            return New-KeyfactorResult -Status Error -Code 740 -Step RereadPersonal `
                -ErrorMessage $msg `
                -Details @{ Thumbprint = $Thumbprint }
        }

        # --- Step: Eligibility ----------------------------------------------
        $eligibility = Test-LdapsCertificateEligibility -Certificate $cert
        if (-not $eligibility.Eligible) {
            $msg = "Certificate '$Thumbprint' failed the LDAPS eligibility check: $($eligibility.Reason)"
            Write-Error $msg
            return New-KeyfactorResult -Status Error -Code 701 -Step Eligibility `
                -ErrorMessage $msg `
                -Details @{ Thumbprint = $Thumbprint }
        }

        # --- Step: WriteNtdsStore ---------------------------------------------
        $writeResult = Set-NtdsServiceStoreCertificate -ServiceName $serviceName -StoreName $leafStoreName -Certificate $cert

        if (-not $writeResult.Success) {
            $msg = "Failed to write certificate '$Thumbprint' into the '$serviceName\$leafStoreName' service store: $($writeResult.ErrorMessage)"
            Write-Error $msg
            return New-KeyfactorResult -Status Error -Code 703 -Step WriteNtdsStore `
                -ErrorMessage $msg `
                -Details @{ Thumbprint = $Thumbprint }
        }

        Write-Information "The thumbprint '$Thumbprint' was registered into the '$serviceName\$leafStoreName' service store."

        return New-KeyfactorResult -Status Success -Code 0 -Step WriteNtdsStore `
            -Message "Certificate '$Thumbprint' registered into the '$serviceName\$leafStoreName' service store." `
            -Details @{ Thumbprint = $Thumbprint }
    }
    catch {
        $msg = "Unexpected error in Register-KeyfactorLdapsCertificate: $($_.Exception.Message)"
        Write-Error $msg
        return New-KeyfactorResult -Status Error -Code 799 -Step CatchAll `
            -ErrorMessage $msg `
            -Details @{ Thumbprint = $Thumbprint }
    }
}
