# Summary:
# Gets a list of SQL instances defined in the Windows Registry

# Setting Preference Variables can have an impact on how the PowerShell Script runs
# Available Preference Variables include: Break, Continue, Ignore, Inquire, SilentlyContinue, Stop, Suspend
# NOTE: Please refer to the PowerShell documentation to learn more about setting and using Preference Variables

$DebugPreference = 'SilentlyContinue'          # Default = 'SilentlyContinue'  NOTE: Debug messages do not get returned over a Remote Connection
$ErrorActionPreference = 'Continue'            # Default = 'Continue'
$InformationPreference = 'Continue'            # Default = 'SilentlyContinue'
$VerbosePreference = 'SilentlyContinue'        # Default = 'SilentlyContinue'  NOTE: Verbose messages do not get returned over a Remote Connection
$WarningPreference = 'Continue'                # Default = 'Continue'          NOTE: Warning messages do not get returned over a Remote Connection

Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server" InstalledInstances