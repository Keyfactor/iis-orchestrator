param (
    [string]$StoreName = "My"  # Default store name is "My" (Personal)
)

# Function to get SAN (Subject Alternative Names) from a certificate
function Get-SAN($cert) {
    $san = $cert.Extensions | Where-Object { $_.Oid.FriendlyName -eq "Subject Alternative Name" }
    if ($san) {
        return ($san.Format(1) -split ", " -join "; ")
    }
    return $null
}

# Get all certificates from the specified store
$certificates = Get-ChildItem -Path "Cert:\LocalMachine\$StoreName"

# Initialize an array to store the results
$certInfoList = @()

foreach ($cert in $certificates) {
    # Create a custom object to store the certificate information
    $certInfo = [PSCustomObject]@{
        StoreName      = $StoreName
        Certificate    = $cert.Subject
        ExpiryDate     = $cert.NotAfter
        Issuer         = $cert.Issuer
        Thumbprint     = $cert.Thumbprint
        HasPrivateKey  = $cert.HasPrivateKey
        SAN            = Get-SAN $cert
        ProviderName   = $cert.ProviderName
        Base64Data     = [System.Convert]::ToBase64String($cert.RawData)
    }
    
    # Add the certificate information to the array
    $certInfoList += $certInfo
}

# Output the results
$certInfoList | ConvertTo-Json
