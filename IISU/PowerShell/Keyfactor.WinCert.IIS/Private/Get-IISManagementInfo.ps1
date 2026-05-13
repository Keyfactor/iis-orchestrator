function Get-IISManagementInfo {
    [CmdletBinding()]
    [OutputType([hashtable])]
    param (
        [Parameter(Mandatory = $true)]
        [string]$SiteName
    )

    $hasIISDrive = Test-IISDrive
    Write-Information "[VERBOSE] IIS Drive available: $hasIISDrive"

    if ($hasIISDrive) {
        $null = Import-Module WebAdministration
        $sitePath = "IIS:\Sites\$SiteName"
        
        if (-not (Test-Path $sitePath)) {
            $errorMessage = "Site '$SiteName' not found in IIS drive"
            Write-Error $errorMessage
            return @{
                Success = $false
                UseIISDrive = $true
                Result = New-KeyfactorResult -Status Error -Code 201 -Step FindWebSite -ErrorMessage $errorMessage -Details @{ SiteName = $SiteName }
            }
        }
        
        return @{
            Success = $true
            UseIISDrive = $true
            Result = $null
        }
    }
    else {
        # ServerManager fallback
        Add-Type -Path "$env:windir\System32\inetsrv\Microsoft.Web.Administration.dll"
        $iis = New-Object Microsoft.Web.Administration.ServerManager
        $site = $iis.Sites[$SiteName]

        if ($null -eq $site) {
            $errorMessage = "Site '$SiteName' not found in ServerManager"
            Write-Error $errorMessage
            return @{
                Success = $false
                UseIISDrive = $false
                Result = New-KeyfactorResult -Status Error -Code 201 -Step FindWebSite -ErrorMessage $errorMessage -Details @{ SiteName = $SiteName }
            }
        }
        
        return @{
            Success = $true
            UseIISDrive = $false
            Result = $null
        }
    }
}