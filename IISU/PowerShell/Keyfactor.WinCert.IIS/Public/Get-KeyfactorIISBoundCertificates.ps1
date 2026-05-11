function Get-KeyfactorIISBoundCertificates{
    $certificates = @()
    $totalBoundCertificates = 0

    try {
        Add-Type -Path "$env:windir\System32\inetsrv\Microsoft.Web.Administration.dll"  #  -AssemblyName "Microsoft.Web.Administration"
        $serverManager = New-Object Microsoft.Web.Administration.ServerManager
    } catch {
        Write-Error "Failed to create ServerManager. IIS might not be installed."
        return
    }

    $websites = $serverManager.Sites
    Write-Information "There were $($websites.Count) websites found."

    foreach ($site in $websites) {
        $siteName = $site.Name
        $siteBoundCertificateCount = 0

        foreach ($binding in $site.Bindings) {
            if ($binding.Protocol -eq 'https' -and $binding.CertificateHash) {
                $certHash = ($binding.CertificateHash | ForEach-Object { $_.ToString("X2") }) -join ""
                $storeName = if ($binding.CertificateStoreName) { $binding.CertificateStoreName } else { "My" }

                try {
                    $cert = Get-ChildItem -Path "Cert:\LocalMachine\$storeName" | Where-Object {
                        $_.Thumbprint -eq $certHash
                    }

                    if (-not $cert) {
                        Write-Warning "Certificate with thumbprint not found in Cert:\LocalMachine\$storeName"
                        continue
                    }

                    $certBase64 = [Convert]::ToBase64String($cert.RawData)
                    $ip, $port, $hostname = $binding.BindingInformation -split ":", 3

                    $certInfo = [PSCustomObject]@{
                        SiteName           = $siteName
                        Binding            = $binding.BindingInformation
                        IPAddress          = $ip
                        Port               = $port
                        Hostname           = $hostname
                        Protocol           = $binding.Protocol
                        SNI                = $binding.SslFlags
                        ProviderName       = Get-CertificateCSP $cert
                        SAN                = Get-CertificateSAN $cert
                        Certificate        = $cert.Subject
                        ExpiryDate         = $cert.NotAfter
                        Issuer             = $cert.Issuer
                        Thumbprint         = $cert.Thumbprint
                        HasPrivateKey      = $cert.HasPrivateKey
                        CertificateBase64  = $certBase64
                    }

                    $certificates += $certInfo
                    $siteBoundCertificateCount++
                    $totalBoundCertificates++
                } catch {
                    Write-Warning "Could not retrieve certificate details for hash $certHash in store $storeName."
                    Write-Warning $_
                }
            }
        }

        Write-Information "Website: $siteName has $siteBoundCertificateCount bindings with certificates."
    }

    Write-Information "A total of $totalBoundCertificates bindings with valid certificates were found."

    if ($totalBoundCertificates -gt 0) {
        $certificates | ConvertTo-Json
    } else {
        Write-Information "No valid certificates were found bound to websites."
    }
}