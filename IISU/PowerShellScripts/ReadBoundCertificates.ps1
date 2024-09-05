# Summary:
# Args: $isRemote - flag to indicate whether the connection is being executed on a remote machine.
# Sets additional modules based on this flag.

param(
    [bool]$isRemote
)

# Example of setting $isRemote directly for testing purposes
# In actual use, you would pass this parameter when calling the script
#$isRemote = $true

# Setting Preference Variables can have an impact on how the PowerShell Script runs
# Available Preference Variables include: Break, Continue, Ignore, Inquire, SilentlyContinue, Stop, Suspend
# NOTE: Please refer to the PowerShell documentation to learn more about setting and using Preference Variables

$DebugPreference = 'SilentlyContinue'          # Default = 'SilentlyContinue'  NOTE: Debug messages do not get returned over a Remote Connection
$ErrorActionPreference = 'Continue'            # Default = 'Continue'
$InformationPreference = 'Continue'            # Default = 'SilentlyContinue'
$VerbosePreference = 'SilentlyContinue'        # Default = 'SilentlyContinue'  NOTE: Verbose messages do not get returned over a Remote Connection
$WarningPreference = 'Continue'                # Default = 'Continue'          NOTE: Warning messages do not get returned over a Remote Connection

# Import modules based on the $isRemote flag
if ($isRemote)
{
    Write-Information "Running in remote mode. Importing remote modules."
    Import-Module -Name 'WebAdministration'
}
else
{
    Write-Information "Running in local mode. Setting execution policy and importing local modules."
    Set-ExecutionPolicy RemoteSigned -Scope Process -Force
    Import-Module WebAdministration
}

# Iterate through websites and their bindings
Foreach ($Site in Get-Website)
{
    Foreach ($Bind in $Site.bindings.collection)
    {
        [pscustomobject]@{
            Name       = $Site.name
            Protocol   = $Bind.Protocol
            Bindings   = $Bind.BindingInformation
            Thumbprint = $Bind.certificateHash
            SniFlg     = $Bind.sslFlags
        }
    }
}
