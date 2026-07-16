function Test-KeyfactorAdminRights {
    [CmdletBinding()]
    [OutputType([pscustomobject])]
    param(
        [string]$Step = "AdminCheck",
        [switch]$Force
    )

    # Return cached result if we've already checked this session (identity won't
    # change mid-session, so this avoids redundant token lookups on repeat calls)
    if (-not $Force -and $script:__KfAdminCheckDone) {
        return $script:__KfAdminCheckResult
    }

    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    try {
        $isAdmin = [Security.Principal.WindowsPrincipal]::new($identity).IsInRole(
            [Security.Principal.WindowsBuiltInRole]::Administrator)

        if ($isAdmin) {
            $result = $null
        }
        else {
            $errorMessage = "This operation requires an elevated session (Run as Administrator) " +
                "or a JEA endpoint whose RunAs identity has local Administrator rights on this machine. " +
                "Current identity: $($identity.Name)."
            Write-Warning $errorMessage
            $result = New-KeyfactorResult -Status Error -Code 240 -Step $Step -ErrorMessage $errorMessage
        }

        $script:__KfAdminCheckResult = $result
        $script:__KfAdminCheckDone = $true
        return $result
    }
    finally {
        $identity.Dispose()
    }
}