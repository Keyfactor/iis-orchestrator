function Get-KeyfactorCertificates {
    param (
        [Parameter(Mandatory = $false)]
        [string]$StoreName = "My",   # Default store name is "My" (Personal)
        
        [Parameter(Mandatory = $false)]
        [string]$Thumbprint          # Optional: specific certificate thumbprint to retrieve
    )

    # Define the store path using the provided StoreName parameter
    $storePath = "Cert:\LocalMachine\$StoreName"

    # Check if the store path exists to ensure the store is valid
    if (-not (Test-Path $storePath)) {
        # Write an error message and exit the function if the store path is invalid
        Write-Error "The certificate store path '$storePath' does not exist. Please provide a valid store name."
        return
    }

    # Retrieve certificates from the specified store
    if ($Thumbprint) {
        # If thumbprint is provided, retrieve only that specific certificate
        # Remove any spaces or special characters from the thumbprint for comparison
        $cleanThumbprint = $Thumbprint -replace '[^a-fA-F0-9]', ''
        $certificates = Get-ChildItem -Path $storePath | Where-Object { 
            ($_.Thumbprint -replace '[^a-fA-F0-9]', '') -eq $cleanThumbprint 
        }
        
        if (-not $certificates) {
            Write-Error "No certificate found with thumbprint '$Thumbprint' in store '$StoreName'."
            return
        }
    } else {
        # Retrieve all certificates from the specified store
        $certificates = Get-ChildItem -Path $storePath
    }

    # Initialize an empty array to store certificate information objects
    $certInfoList = @()

    foreach ($cert in $certificates) {
        try {
            # Create a custom object to store details about the current certificate
            $certInfo = [PSCustomObject]@{
                StoreName      = $StoreName                                         # Name of the certificate store
                Certificate    = $cert.Subject                                      # Subject of the certificate
                ExpiryDate     = $cert.NotAfter                                     # Expiration date of the certificate
                Issuer         = $cert.Issuer                                       # Issuer of the certificate
                Thumbprint     = $cert.Thumbprint                                   # Unique thumbprint of the certificate
                HasPrivateKey  = $cert.HasPrivateKey                                # Indicates if the certificate has a private key
                SAN            = Get-CertificateSAN $cert                           # Subject Alternative Names (if available)
                ProviderName   = Get-CertificateCSP $cert                           # Provider of the certificate
                Base64Data     = [System.Convert]::ToBase64String($cert.RawData)    # Encoded raw certificate data
            }

            # Add the certificate information object to the results array
            $certInfoList += $certInfo
        } catch {
            # Write a warning message if there is an error processing the current certificate
            Write-Warning "An error occurred while processing the certificate: $_"
        }
    }

    # Output the results in JSON format if certificates were found
    if ($certInfoList) {
        $certInfoList | ConvertTo-Json -Depth 10
    } else {
        # Write a warning if no certificates were found in the specified store
        Write-Warning "No certificates were found in the store '$StoreName'."
    }
}