# Summary:  This script gets the certificates from the LocalMachine for the given store path.
# Args:     $storePath - Contains the cert path to read the certificates
# 
# Return Value: $certs - contains the specific cert details for each certificate in the given cert path

param(
    [string]$storePath
)

# Setting Preference Variables can have an impact on how the PowerShell Script runs
# Available Preference Variables include: Break, Continue, Ignore, Inquire, SilentlyContinue, Stop, Suspend
# NOTE: Please refer to the PowerShell documentation to learn more about setting and using Preference Variables
$DebugPreference = 'SilentlyContinue'          # Default = 'SilentlyContinue'  NOTE: Debug messages do not get returned over a Remote Connection
$ErrorActionPreference = 'Continue'            # Default = 'Continue'
$InformationPreference = 'Continue'            # Default = 'SilentlyContinue'
$VerbosePreference = 'SilentlyContinue'        # Default = 'SilentlyContinue'  NOTE: Verbose messages do not get returned over a Remote Connection
$WarningPreference = 'Continue'                # Default = 'Continue'          NOTE: Warning messages do not get returned over a Remote Connection
#

Write-Information "WARN: Store path passed from extension: $storePath"

$certStore = New-Object System.Security.Cryptography.X509Certificates.X509Store($storePath,'LocalMachine')
$certStore.Open('ReadOnly')

$certs = $certStore.Certificates

$certStore.Close()
$certStore.Dispose()

$certs | ForEach-Object {
    $certDetails = @{
        Subject = $_.Subject
        Thumbprint = $_.Thumbprint
        HasPrivateKey = $_.HasPrivateKey
        RawData = $_.RawData
        san = $_.Extensions | Where-Object { $_.Oid.FriendlyName -eq "Subject Alternative Name" } | ForEach-Object { $_.Format($false) }
    }

    if ($_.HasPrivateKey) {
        $certDetails.CSP = $_.PrivateKey.CspKeyContainerInfo.ProviderName
    }

    New-Object PSObject -Property $certDetails
}