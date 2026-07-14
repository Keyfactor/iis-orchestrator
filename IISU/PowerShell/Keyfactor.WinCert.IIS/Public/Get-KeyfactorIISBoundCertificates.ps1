function Get-KeyfactorIISBoundCertificates {

    $certificates = @()
    $totalBoundCertificates = 0

    #
    # Verify the current process is running with an elevated token.
    #
    $currentIdentity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($currentIdentity)

    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        Write-Information `
            -Message "Get-KeyfactorIISBoundCertificates requires an elevated PowerShell session (Run as Administrator) or a JEA endpoint configured with administrative privileges." `
            -ErrorAction Stop
    }

    try {
        Add-Type -Path "$env:windir\System32\inetsrv\Microsoft.Web.Administration.dll" -ErrorAction Stop
        $serverManager = [Microsoft.Web.Administration.ServerManager]::new()
    }
    catch {
        Write-Information `
            -Message "Failed to create the IIS ServerManager object. IIS may not be installed or the Microsoft.Web.Administration assembly could not be loaded.`n$($_.Exception.Message)" `
            -ErrorAction Stop
    }

    #
    # Verify IIS sites can actually be enumerated.
    #
    try {
        $websites = $serverManager.Sites

        if ($null -eq $websites) {
            Write-Information `
                -Message "Unable to enumerate IIS websites." `
                -ErrorAction Stop
        }

        if ($websites.Count -eq 0) {

            $siteCount = (Get-Service W3SVC -ErrorAction SilentlyContinue)

            if ($siteCount) {
                Write-Information @"
No IIS websites were returned.

Possible causes include:
  • The current user does not have permission to enumerate IIS configuration.
  • IIS contains no configured websites.
  • The IIS configuration is unavailable.
"@
            }

            return
        }
    }
    catch {
        Write-Error `
            -Message "Access denied while reading IIS configuration. Local Administrator privileges or an appropriately configured JEA endpoint are required.`n$($_.Exception.Message)" `
            -ErrorAction Stop
    }

    Write-Information "There were $($websites.Count) websites found."

    foreach ($site in $websites) {

        $siteName = $site.Name
        $siteBoundCertificateCount = 0

        foreach ($binding in $site.Bindings) {

            if ($binding.Protocol -eq 'https' -and $binding.CertificateHash) {

                $certHash = ($binding.CertificateHash | ForEach-Object { $_.ToString("X2") }) -join ""
                $storeName = if ($binding.CertificateStoreName) { $binding.CertificateStoreName } else { "My" }

                try {

                    $cert = Get-ChildItem "Cert:\LocalMachine\$storeName" |
                        Where-Object Thumbprint -eq $certHash

                    if (-not $cert) {
                        Write-Warning "Certificate with thumbprint '$certHash' was not found in Cert:\LocalMachine\$storeName."
                        continue
                    }

                    $certBase64 = [Convert]::ToBase64String($cert.RawData)
                    $ip, $port, $hostname = $binding.BindingInformation -split ":", 3

                    $certificates += [PSCustomObject]@{
                        SiteName          = $siteName
                        Binding           = $binding.BindingInformation
                        IPAddress         = $ip
                        Port              = $port
                        Hostname          = $hostname
                        Protocol          = $binding.Protocol
                        SNI               = $binding.SslFlags
                        ProviderName      = Get-CertificateCSP $cert
                        SAN               = Get-CertificateSAN $cert
                        Certificate       = $cert.Subject
                        ExpiryDate        = $cert.NotAfter
                        Issuer            = $cert.Issuer
                        Thumbprint        = $cert.Thumbprint
                        HasPrivateKey     = $cert.HasPrivateKey
                        CertificateBase64 = $certBase64
                    }

                    $siteBoundCertificateCount++
                    $totalBoundCertificates++
                }
                catch {
                    Write-Warning "Could not retrieve certificate details for thumbprint '$certHash' in store '$storeName'."
                    Write-Warning $_
                }
            }
        }

        Write-Information "Website '$siteName' has $siteBoundCertificateCount HTTPS binding(s) with certificates."
    }

    Write-Information "A total of $totalBoundCertificates HTTPS binding(s) with valid certificates were found."

    if ($totalBoundCertificates -gt 0) {
        $certificates | ConvertTo-Json
    }
    else {
        Write-Information "No HTTPS bindings with valid certificates were found."
    }
}