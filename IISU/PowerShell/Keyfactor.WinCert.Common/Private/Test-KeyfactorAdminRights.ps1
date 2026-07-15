function Test-KeyfactorAdminRights {
    [CmdletBinding()]
    [OutputType([pscustomobject])]
    param(
        [string]$Step = "AdminCheck"
    )

    $currentIdentity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($currentIdentity)

    if ($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        return $null
    }

    $errorMessage = "This operation requires an elevated session (Run as Administrator) " +
        "or a JEA endpoint whose RunAs identity has local Administrator rights on this machine. " +
        "Current identity: $($currentIdentity.Name)."

    Write-Warning $errorMessage

    return New-KeyfactorResult -Status Error -Code 240 -Step $Step -ErrorMessage $errorMessage
}
