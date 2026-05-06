function Remove-KeyfactorIISCertificateIfUnused {
    param (
        [Parameter(Mandatory = $true)]
        [string]$Thumbprint,

        [Parameter(Mandatory = $false)]
        [string]$StoreName = "My"
    )

    try {
        Add-Type -Path "$env:windir\System32\inetsrv\Microsoft.Web.Administration.dll"
    } catch {
        throw "Failed to load Microsoft.Web.Administration. Ensure IIS is installed on the remote server."
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
            Write-Information "[VERBOSE] Certificate $normalizedThumbprint is still active on $($bindings.Count) binding(s) — skipping removal"
            $bindings | ForEach-Object { Write-Warning "  Still bound: $($_.SiteName) / $($_.Binding)" }
            return
        }

        $cert = Get-ChildItem -Path "Cert:\LocalMachine\$StoreName" |
                    Where-Object { $_.Thumbprint -eq $normalizedThumbprint }

        if (-not $cert) {
            Write-Information "[VERBOSE] Certificate $normalizedThumbprint not found in Cert:\LocalMachine\$StoreName — nothing to remove"
            return
        }

        Remove-Item -Path $cert.PSPath -Force
        Write-Information "[VERBOSE] Certificate $normalizedThumbprint removed from Cert:\LocalMachine\$StoreName"
    }
    catch {
        Write-Error "An error occurred while attempting to remove IIS certificate: $_"
    }
}
