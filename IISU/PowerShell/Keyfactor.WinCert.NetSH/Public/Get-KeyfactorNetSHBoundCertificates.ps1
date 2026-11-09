function Get-KeyfactorNetSHBoundCertificates {
    <#
    .SYNOPSIS
    Returns every "netsh http sslcert" binding whose certificate lives in the given store, as JSON.

    .DESCRIPTION
    Inventory is scoped to bindings whose "Certificate Store Name" (netsh's certstorename) matches
    -StoreName, the same "one Certificate Store definition = one scope" convention WinLDAP documents
    for its own inventory. netsh's sslcert entries only carry the certificate's hash and which store
    to find it in, not the certificate itself, so each binding's certificate is re-read from
    Cert:\LocalMachine\<StoreName> to get the actual certificate bytes/HasPrivateKey/CSP/SAN.
    #>
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [string]$StoreName
    )

    $certificates = @()
    $totalBoundCertificates = 0

    try {
        $allBindings = @(Get-NetShSslCertBinding)
    }
    catch {
        Write-Error -Message "Failed to enumerate netsh http sslcert bindings.`n$($_.Exception.Message)" -ErrorAction Stop
    }

    Write-Information "There were $($allBindings.Count) total sslcert binding(s) found."

    $scopedBindings = @($allBindings | Where-Object { $_.CertificateStoreName -and $_.CertificateStoreName -eq $StoreName })

    foreach ($binding in $scopedBindings) {

        $isHostname = [string]::IsNullOrEmpty($binding.IPPort) -and -not [string]::IsNullOrEmpty($binding.HostnamePort)
        $bindingKey = if ($isHostname) { $binding.HostnamePort } else { $binding.IPPort }

        # Split on the LAST colon, not the first - an IPv6 address (e.g. "[::]:443") contains colons
        # of its own, so the port is always what follows the final one.
        if ($bindingKey -notmatch '^(?<addr>.+):(?<port>\d+)$') {
            Write-Warning "Could not parse binding key '$bindingKey' into address/hostname and port - skipping."
            continue
        }
        $addressOrHost = $Matches['addr']
        $port = $Matches['port']

        try {
            $cert = Get-ChildItem "Cert:\LocalMachine\$StoreName" |
                Where-Object Thumbprint -eq $binding.CertificateHash

            if (-not $cert) {
                Write-Warning "Certificate with thumbprint '$($binding.CertificateHash)' (bound at '$bindingKey') was not found in Cert:\LocalMachine\$StoreName."
                continue
            }

            $certBase64 = [Convert]::ToBase64String($cert.RawData)

            $certificates += [PSCustomObject]@{
                IPAddress         = if ($isHostname) { "0.0.0.0" } else { $addressOrHost }
                Port              = $port
                HostName          = if ($isHostname) { $addressOrHost } else { $null }
                AppId             = $binding.ApplicationId
                ProviderName      = Get-CertificateCSP $cert
                SAN               = Get-CertificateSAN $cert
                Certificate       = $cert.Subject
                ExpiryDate        = $cert.NotAfter
                Issuer            = $cert.Issuer
                Thumbprint        = $cert.Thumbprint
                HasPrivateKey     = $cert.HasPrivateKey
                CertificateBase64 = $certBase64
            }

            $totalBoundCertificates++
        }
        catch {
            Write-Warning "Could not retrieve certificate details for thumbprint '$($binding.CertificateHash)' bound at '$bindingKey'."
            Write-Warning $_
        }
    }

    Write-Information "A total of $totalBoundCertificates sslcert binding(s) with valid certificates were found in store '$StoreName'."

    if ($totalBoundCertificates -gt 0) {
        $certificates | ConvertTo-Json
    }
    else {
        Write-Information "No sslcert bindings with valid certificates were found in store '$StoreName'."
    }
}
