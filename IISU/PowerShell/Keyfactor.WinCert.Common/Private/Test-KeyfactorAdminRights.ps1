function Test-KeyfactorAdminRights {
    [CmdletBinding()]
    [OutputType([pscustomobject])]
    param(
        [string]$Step = "AdminCheck",
        [switch]$Force
    )

    if (-not $Force -and $script:__KfAdminCheckDone) {
        return $script:__KfAdminCheckResult
    }

    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    try {
        $isAdmin = [Security.Principal.WindowsPrincipal]::new($identity).IsInRole(
            [Security.Principal.WindowsBuiltInRole]::Administrator)

        if ($isAdmin) {
            $result = [pscustomobject]@{
                IsAdmin      = $true
                Status       = 'Success'
                Step         = $Step
                ErrorMessage = $null
            }
        }
        else {
            $errorMessage = "This operation requires an elevated session (Run as Administrator) " +
                "or a JEA endpoint whose RunAs identity has local Administrator rights on this machine. " +
                "Current identity: $($identity.Name)."
            Write-Warning $errorMessage

            # Preserve existing error object for any PowerShell-side consumers
            $kfResult = New-KeyfactorResult -Status Error -Code 240 -Step $Step -ErrorMessage $errorMessage

            $result = [pscustomobject]@{
                IsAdmin      = $false
                Status       = 'Error'
                Step         = $Step
                ErrorMessage = $errorMessage
                Result       = $kfResult
            }
        }

        $script:__KfAdminCheckResult = $result
        $script:__KfAdminCheckDone = $true
        return $result
    }
    finally {
        $identity.Dispose()
    }
}