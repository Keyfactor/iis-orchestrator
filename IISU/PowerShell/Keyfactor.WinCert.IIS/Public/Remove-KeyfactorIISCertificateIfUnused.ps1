function Remove-KeyfactorIISCertificateIfUnused {
    [CmdletBinding()]
    [OutputType([pscustomobject])]
    param (
        [Parameter(Mandatory = $true)]
        [string]$Thumbprint,

        [Parameter(Mandatory = $false)]
        [string]$StoreName = "My"
    )

    try {
        Add-Type -Path "$env:windir\System32\inetsrv\Microsoft.Web.Administration.dll"
    }
    catch {
        $errorMessage = "Failed to load Microsoft.Web.Administration. Ensure IIS is installed on the remote server."
        Write-Warning $errorMessage
        return New-KeyfactorResult -Status Error -Code 231 -Step RemoveCertificate -ErrorMessage $errorMessage
    }

    # Normalize thumbprint: strip whitespace and force uppercase for consistent comparison
    $normalizedThumbprint = ($Thumbprint -replace '\s', '').ToUpperInvariant()
    Write-Information "[VERBOSE] Remove-KeyfactorIISCertificateIfUnused: checking thumbprint $normalizedThumbprint in store $StoreName"

    try {
        $serverManager = New-Object Microsoft.Web.Administration.ServerManager
        $bindings = @()

        foreach ($site in $serverManager.Sites) {
            foreach ($binding in $site.Bindings) {
                if ($binding.Protocol -eq 'https' -and $binding.CertificateHash) {
                    $bindingThumbprint = ($binding.CertificateHash | ForEach-Object { $_.ToString("X2") }) -join ""
                    if ($bindingThumbprint -eq $normalizedThumbprint) {
                        $bindings += [PSCustomObject]@{
                            SiteName = $site.Name
                            Binding  = $binding.BindingInformation
                        }
                    }
                }
            }
        }

        if ($bindings.Count -gt 0) {
            $bindingSummary = ($bindings | ForEach-Object { "$($_.SiteName) / $($_.Binding)" }) -join ", "
            Write-Information "[VERBOSE] Certificate $normalizedThumbprint is still active on $($bindings.Count) binding(s) — skipping removal"
            $bindings | ForEach-Object { Write-Warning "  Still bound: $($_.SiteName) / $($_.Binding)" }

            return New-KeyfactorResult -Status Skipped -Code 208 -Step RemoveCertificate `
                -Message "Certificate $normalizedThumbprint is still bound to $($bindings.Count) site(s) ($bindingSummary); removal skipped." `
                -Details @{ Thumbprint = $normalizedThumbprint; StillBoundTo = $bindings }
        }

        $cert = Get-ChildItem -Path "Cert:\LocalMachine\$StoreName" |
                    Where-Object { $_.Thumbprint -eq $normalizedThumbprint }

        if (-not $cert) {
            Write-Information "[VERBOSE] Certificate $normalizedThumbprint not found in Cert:\LocalMachine\$StoreName — nothing to remove"
            return New-KeyfactorResult -Status Skipped -Code 209 -Step RemoveCertificate `
                -Message "Certificate $normalizedThumbprint was not found in Cert:\LocalMachine\$StoreName; nothing to remove." `
                -Details @{ Thumbprint = $normalizedThumbprint }
        }

        Remove-Item -Path "Cert:\LocalMachine\$StoreName\$normalizedThumbprint" -Force
        Write-Information "[VERBOSE] Certificate $normalizedThumbprint removed from Cert:\LocalMachine\$StoreName"

        return New-KeyfactorResult -Status Success -Code 0 -Step RemoveCertificate `
            -Message "Certificate $normalizedThumbprint removed from store '$StoreName'." `
            -Details @{ Thumbprint = $normalizedThumbprint }
    }
    catch {
        $errorMessage = "An error occurred while attempting to remove IIS certificate: $_"
        Write-Warning $errorMessage
        return New-KeyfactorResult -Status Error -Code 232 -Step RemoveCertificate -ErrorMessage $errorMessage -Details @{ Thumbprint = $normalizedThumbprint }
    }
}
